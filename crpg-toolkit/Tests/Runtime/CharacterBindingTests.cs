using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using CRPG.Actors;
using CRPG.Core;
using CRPG.Inventory;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace CRPG.Tests.PlayMode
{
    /// <summary>
    /// Task 1.1 (Production Foundation): CharacterPlayableAnimator and CharacterVisualEquipment both subscribe
    /// to an IGameEventBus in Initialize() with only destruction-time unsubscribe (see
    /// Docs/Design/Production-Foundation.md's repository-grounded-baseline gap table). Revised after code
    /// review to match the design's actual binding contract (Production-Foundation.md sec.4.2): rebinding the
    /// identical (character, dependencies) tuple is a no-op; rebinding to a different one without an explicit
    /// Unbind() first is an error; Unbind() clears the binding identity so a later disable/enable cycle can't
    /// silently resurrect it; a disabled/inactive component still stores an Initialize() call but defers the
    /// actual subscribe until it becomes active. These tests are this package's first real PlayMode coverage
    /// (Tests/Runtime/CRPG.Tests.PlayMode.asmdef existed with zero test files before this).
    /// </summary>
    public static class CharacterBindingTests
    {
        // Tests/Editor's TestDefinitionFactory isn't visible from this PlayMode assembly (its asmdef is
        // Editor-only platform), so this mirrors just the two reflection helpers this file needs.
        private static void SetPrivateField(object target, string fieldName, object value) => PrivateField(target, fieldName).SetValue(target, value);

        private static object GetPrivateField(object target, string fieldName) => PrivateField(target, fieldName).GetValue(target);

        private static FieldInfo PrivateField(object target, string fieldName)
        {
            var type = target.GetType();
            while (type != null)
            {
                var field = type.GetField(fieldName, BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.DeclaredOnly);
                if (field != null) return field;

                type = type.BaseType;
            }

            throw new Exception($"Field '{fieldName}' not found on {target.GetType().Name} or its base types.");
        }

        /// <summary>Counts net Subscribe/Unsubscribe calls per event rather than delivering them -- the most
        /// direct way to prove subscription symmetry (repeat-init doesn't double-subscribe, disable/destroy
        /// actually unsubscribes) without needing valid AnimationClips or other heavy fixture setup.</summary>
        private sealed class RecordingGameEventBus : IGameEventBus
        {
            private readonly Dictionary<GameEvent, int> netSubscribersByEvent = new();

            public void Subscribe(GameEvent gameEvent, Action<GameEventContext> handler)
            {
                netSubscribersByEvent.TryGetValue(gameEvent, out var count);
                netSubscribersByEvent[gameEvent] = count + 1;
            }

            public void Unsubscribe(GameEvent gameEvent, Action<GameEventContext> handler)
            {
                netSubscribersByEvent.TryGetValue(gameEvent, out var count);
                netSubscribersByEvent[gameEvent] = count - 1;
            }

            public void Publish(GameEventContext context)
            {
            }

            public int NetSubscribers(GameEvent gameEvent) => netSubscribersByEvent.TryGetValue(gameEvent, out var count) ? count : 0;
        }

        public class AnimatorSubscriptionTests
        {
            private GameObject go;

            [TearDown]
            public void TearDown()
            {
                if (go != null) UnityEngine.Object.DestroyImmediate(go);
            }

            private CharacterPlayableAnimator CreateAnimator()
            {
                go = new GameObject("TestCharacter");
                return go.AddComponent<CharacterPlayableAnimator>();
            }

            [Test]
            public void InitializeSubscribesExactlyOnce()
            {
                var animator = CreateAnimator();
                var bus = new RecordingGameEventBus();

                animator.Initialize(RuntimeEntityId.NewId(), bus);

                Assert.AreEqual(1, bus.NetSubscribers(GameEvent.AnimationCueChanged));
            }

            [Test]
            public void InitializeOnAnInactiveGameObjectDefersSubscribingUntilEnabled()
            {
                go = new GameObject("TestCharacter");
                go.SetActive(false);
                var animator = go.AddComponent<CharacterPlayableAnimator>();
                var bus = new RecordingGameEventBus();

                animator.Initialize(RuntimeEntityId.NewId(), bus);
                Assert.AreEqual(0, bus.NetSubscribers(GameEvent.AnimationCueChanged), "An inactive GameObject must not subscribe yet.");

                go.SetActive(true);
                Assert.AreEqual(1, bus.NetSubscribers(GameEvent.AnimationCueChanged), "Activating must resume the stored binding exactly once.");
            }

            [Test]
            public void InitializeOnADisabledComponentDefersSubscribingUntilEnabled()
            {
                var animator = CreateAnimator();
                animator.enabled = false;
                var bus = new RecordingGameEventBus();

                animator.Initialize(RuntimeEntityId.NewId(), bus);
                Assert.AreEqual(0, bus.NetSubscribers(GameEvent.AnimationCueChanged), "A disabled component must not subscribe yet.");

                animator.enabled = true;
                Assert.AreEqual(1, bus.NetSubscribers(GameEvent.AnimationCueChanged), "Re-enabling must resume the stored binding exactly once.");
            }

            [Test]
            public void RepeatInitializeWithTheIdenticalBindingIsANoOp()
            {
                var animator = CreateAnimator();
                var bus = new RecordingGameEventBus();
                var character = RuntimeEntityId.NewId();

                animator.Initialize(character, bus);
                animator.Initialize(character, bus); // identical (character, eventBus) tuple

                Assert.AreEqual(1, bus.NetSubscribers(GameEvent.AnimationCueChanged));
            }

            [Test]
            public void InitializeWithADifferentBindingWithoutUnbindingFirstThrows()
            {
                var animator = CreateAnimator();
                var busA = new RecordingGameEventBus();
                var busB = new RecordingGameEventBus();
                animator.Initialize(RuntimeEntityId.NewId(), busA);

                Assert.Throws<InvalidOperationException>(() => animator.Initialize(RuntimeEntityId.NewId(), busB));

                Assert.AreEqual(1, busA.NetSubscribers(GameEvent.AnimationCueChanged), "The original binding must be untouched by the failed rebind attempt.");
                Assert.AreEqual(0, busB.NetSubscribers(GameEvent.AnimationCueChanged));
            }

            [Test]
            public void UnbindThenInitializeToADifferentEventBusSucceeds()
            {
                var animator = CreateAnimator();
                var oldBus = new RecordingGameEventBus();
                var newBus = new RecordingGameEventBus();

                animator.Initialize(RuntimeEntityId.NewId(), oldBus);
                animator.Unbind();
                animator.Initialize(RuntimeEntityId.NewId(), newBus);

                Assert.AreEqual(0, oldBus.NetSubscribers(GameEvent.AnimationCueChanged));
                Assert.AreEqual(1, newBus.NetSubscribers(GameEvent.AnimationCueChanged));
            }

            [Test]
            public void DestroyingTheGameObjectUnsubscribes()
            {
                var animator = CreateAnimator();
                var bus = new RecordingGameEventBus();
                animator.Initialize(RuntimeEntityId.NewId(), bus);

                UnityEngine.Object.DestroyImmediate(go);
                go = null;

                Assert.AreEqual(0, bus.NetSubscribers(GameEvent.AnimationCueChanged));
            }

            [Test]
            public void DisablingThenReenablingResubscribesExactlyOnce()
            {
                var animator = CreateAnimator();
                var bus = new RecordingGameEventBus();
                animator.Initialize(RuntimeEntityId.NewId(), bus);

                go.SetActive(false);
                Assert.AreEqual(0, bus.NetSubscribers(GameEvent.AnimationCueChanged), "OnDisable should pause the subscription.");

                go.SetActive(true);
                Assert.AreEqual(1, bus.NetSubscribers(GameEvent.AnimationCueChanged), "OnEnable should resume it exactly once, not stack.");
            }

            [Test]
            public void ExplicitUnbindThenDestroyDoesNotDoubleUnsubscribe()
            {
                var animator = CreateAnimator();
                var bus = new RecordingGameEventBus();
                animator.Initialize(RuntimeEntityId.NewId(), bus);

                animator.Unbind();
                Assert.AreEqual(0, bus.NetSubscribers(GameEvent.AnimationCueChanged));

                Assert.DoesNotThrow(() => UnityEngine.Object.DestroyImmediate(go));
                go = null;
                Assert.AreEqual(0, bus.NetSubscribers(GameEvent.AnimationCueChanged));
            }

            [Test]
            public void UnbindThenDisableThenEnableLeavesTheComponentDetached()
            {
                // The exact scenario from the Task 1.1 code review: Unbind() must clear the binding identity,
                // not just unsubscribe, or a later disable/enable cycle silently resubscribes to what the
                // caller explicitly detached from.
                var animator = CreateAnimator();
                var bus = new RecordingGameEventBus();
                animator.Initialize(RuntimeEntityId.NewId(), bus);

                animator.Unbind();
                go.SetActive(false);
                go.SetActive(true);

                Assert.AreEqual(0, bus.NetSubscribers(GameEvent.AnimationCueChanged), "An explicitly-unbound component must stay detached through a disable/enable cycle.");
            }

            [Test]
            public void UnbindResetsBaselineAndTransientAnimationState()
            {
                var animator = CreateAnimator();
                var bus = new RecordingGameEventBus();
                animator.Initialize(RuntimeEntityId.NewId(), bus);

                // Simulate a live explicit stance + in-flight transient, as a previous character's animation
                // state would leave behind -- without needing a real AnimationProfile/AnimationClip fixture.
                SetPrivateField(animator, "currentBaselineCueId", new DefinitionId("anim-combat-stance"));
                SetPrivateField(animator, "baselineIsExplicit", true);
                SetPrivateField(animator, "transientCueId", (DefinitionId?)new DefinitionId("anim-attack"));

                animator.Unbind();

                Assert.AreEqual(default(DefinitionId), GetPrivateField(animator, "currentBaselineCueId"), "A rebind must not resume the previous character's explicit baseline.");
                Assert.IsFalse((bool)GetPrivateField(animator, "baselineIsExplicit"));
                Assert.IsNull(GetPrivateField(animator, "transientCueId"), "A rebind must not resume the previous character's in-flight transient.");
            }
        }

        public class VisualEquipmentSubscriptionTests
        {
            private GameObject go;
            private GameObject visualPrefab;

            [TearDown]
            public void TearDown()
            {
                if (go != null) UnityEngine.Object.DestroyImmediate(go);
                if (visualPrefab != null) UnityEngine.Object.DestroyImmediate(visualPrefab);
            }

            private CharacterVisualEquipment CreateVisualEquipment()
            {
                go = new GameObject("TestCharacter");
                return go.AddComponent<CharacterVisualEquipment>();
            }

            private static ItemDefinition CreateEquippableItem(string id, DefinitionId slotId, GameObject prefab)
            {
                var item = ScriptableObject.CreateInstance<ItemDefinition>();
                SetPrivateField(item, "id", id);
                SetPrivateField(item, "equipmentSlotId", slotId);
                SetPrivateField(item, "visualPrefab", prefab);
                return item;
            }

            private static GameEventContext EquipContext(RuntimeEntityId character, DefinitionId itemDefinitionId, DefinitionId slotId)
            {
                var context = new GameEventContext(GameEvent.ItemEquipped, character, character);
                context.Data["ItemDefinitionId"] = itemDefinitionId;
                context.Data["SlotId"] = slotId;
                return context;
            }

            [Test]
            public void InitializeSubscribesToItemEquippedAndUnequippedExactlyOnce()
            {
                var equipment = CreateVisualEquipment();
                var bus = new RecordingGameEventBus();

                equipment.Initialize(RuntimeEntityId.NewId(), new RulesDatabase(), bus);

                Assert.AreEqual(1, bus.NetSubscribers(GameEvent.ItemEquipped));
                Assert.AreEqual(1, bus.NetSubscribers(GameEvent.ItemUnequipped));
            }

            [Test]
            public void InitializeOnAnInactiveGameObjectDefersSubscribingUntilEnabled()
            {
                go = new GameObject("TestCharacter");
                go.SetActive(false);
                var equipment = go.AddComponent<CharacterVisualEquipment>();
                var bus = new RecordingGameEventBus();

                equipment.Initialize(RuntimeEntityId.NewId(), new RulesDatabase(), bus);
                Assert.AreEqual(0, bus.NetSubscribers(GameEvent.ItemEquipped), "An inactive GameObject must not subscribe yet.");

                go.SetActive(true);
                Assert.AreEqual(1, bus.NetSubscribers(GameEvent.ItemEquipped), "Activating must resume the stored binding exactly once.");
            }

            [Test]
            public void InitializeOnADisabledComponentDefersSubscribingUntilEnabled()
            {
                var equipment = CreateVisualEquipment();
                equipment.enabled = false;
                var bus = new RecordingGameEventBus();

                equipment.Initialize(RuntimeEntityId.NewId(), new RulesDatabase(), bus);
                Assert.AreEqual(0, bus.NetSubscribers(GameEvent.ItemEquipped), "A disabled component must not subscribe yet.");

                equipment.enabled = true;
                Assert.AreEqual(1, bus.NetSubscribers(GameEvent.ItemEquipped), "Re-enabling must resume the stored binding exactly once.");
            }

            [Test]
            public void RepeatInitializeWithTheIdenticalBindingIsANoOp()
            {
                var equipment = CreateVisualEquipment();
                var bus = new RecordingGameEventBus();
                var rulesDatabase = new RulesDatabase();
                var character = RuntimeEntityId.NewId();

                equipment.Initialize(character, rulesDatabase, bus);
                equipment.Initialize(character, rulesDatabase, bus); // identical tuple

                Assert.AreEqual(1, bus.NetSubscribers(GameEvent.ItemEquipped));
                Assert.AreEqual(1, bus.NetSubscribers(GameEvent.ItemUnequipped));
            }

            [Test]
            public void RepeatInitializeWithTheIdenticalBindingPreservesAlreadyEquippedGear()
            {
                var equipment = CreateVisualEquipment();
                var bus = new GameEventBus(); // the real bus -- this test needs actual Publish delivery
                var rulesDatabase = new RulesDatabase();
                var slotId = new DefinitionId("slot-test");
                visualPrefab = new GameObject("VisualPrefab");
                var item = CreateEquippableItem("item-test-visual-repeat-init", slotId, visualPrefab);
                rulesDatabase.Register(item);
                var character = RuntimeEntityId.NewId();

                equipment.Initialize(character, rulesDatabase, bus);
                bus.Publish(EquipContext(character, item.Id, slotId));
                Assert.AreEqual(1, go.transform.childCount);

                equipment.Initialize(character, rulesDatabase, bus); // identical tuple -- must be a true no-op

                Assert.AreEqual(1, go.transform.childCount, "Repeating Initialize for the same binding must not destroy already-equipped gear.");
            }

            [Test]
            public void InitializeWithADifferentBindingWithoutUnbindingFirstThrows()
            {
                var equipment = CreateVisualEquipment();
                var bus = new GameEventBus(); // the real bus -- this test needs actual Publish delivery
                var rulesDatabase = new RulesDatabase();
                var slotId = new DefinitionId("slot-test");
                visualPrefab = new GameObject("VisualPrefab");
                var item = CreateEquippableItem("item-test-visual-throw", slotId, visualPrefab);
                rulesDatabase.Register(item);

                var characterA = RuntimeEntityId.NewId();
                equipment.Initialize(characterA, rulesDatabase, bus);
                bus.Publish(EquipContext(characterA, item.Id, slotId));
                Assert.AreEqual(1, go.transform.childCount);

                Assert.Throws<InvalidOperationException>(() => equipment.Initialize(RuntimeEntityId.NewId(), rulesDatabase, bus));

                Assert.AreEqual(1, go.transform.childCount, "The original binding's equipped visual must be untouched by the failed rebind attempt.");
            }

            [Test]
            public void DestroyingTheGameObjectUnsubscribes()
            {
                var equipment = CreateVisualEquipment();
                var bus = new RecordingGameEventBus();
                equipment.Initialize(RuntimeEntityId.NewId(), new RulesDatabase(), bus);

                UnityEngine.Object.DestroyImmediate(go);
                go = null;

                Assert.AreEqual(0, bus.NetSubscribers(GameEvent.ItemEquipped));
                Assert.AreEqual(0, bus.NetSubscribers(GameEvent.ItemUnequipped));
            }

            [Test]
            public void DisablingDoesNotUnsubscribeFromEquipmentEvents()
            {
                // Deliberately different from the animator's policy (Task 1.1 code review, issue 4): missing
                // an equip/unequip event while disabled would leave worn gear silently wrong once re-enabled,
                // and full reconciliation-on-enable is Task 2.4 work. Interim policy: keep listening even
                // while inactive/disabled, so equipment state never has a chance to drift.
                var equipment = CreateVisualEquipment();
                var bus = new RecordingGameEventBus();
                equipment.Initialize(RuntimeEntityId.NewId(), new RulesDatabase(), bus);

                go.SetActive(false);

                Assert.AreEqual(1, bus.NetSubscribers(GameEvent.ItemEquipped), "Disabling must not unsubscribe equipment from ItemEquipped/ItemUnequipped.");
                Assert.AreEqual(1, bus.NetSubscribers(GameEvent.ItemUnequipped));
            }

            [Test]
            public void DisablingThenReenablingDoesNotDestroyAlreadyEquippedVisuals()
            {
                var equipment = CreateVisualEquipment();
                var bus = new GameEventBus(); // the real bus -- this test needs actual Publish delivery
                var rulesDatabase = new RulesDatabase();
                var slotId = new DefinitionId("slot-test");
                visualPrefab = new GameObject("VisualPrefab");
                var item = CreateEquippableItem("item-test-visual-disable", slotId, visualPrefab);
                rulesDatabase.Register(item);

                var character = RuntimeEntityId.NewId();
                equipment.Initialize(character, rulesDatabase, bus);
                bus.Publish(EquipContext(character, item.Id, slotId));
                Assert.AreEqual(1, go.transform.childCount);

                go.SetActive(false);
                go.SetActive(true);

                Assert.AreEqual(1, go.transform.childCount, "A mere disable/enable cycle must not strip already-equipped visuals.");
            }

            [Test]
            public void UnbindThenDisableThenEnableLeavesTheComponentDetached()
            {
                var equipment = CreateVisualEquipment();
                var bus = new RecordingGameEventBus();
                equipment.Initialize(RuntimeEntityId.NewId(), new RulesDatabase(), bus);

                equipment.Unbind();
                go.SetActive(false);
                go.SetActive(true);

                Assert.AreEqual(0, bus.NetSubscribers(GameEvent.ItemEquipped), "An explicitly-unbound component must stay detached through a disable/enable cycle.");
                Assert.AreEqual(0, bus.NetSubscribers(GameEvent.ItemUnequipped));
            }

            // [UnityTest]/IEnumerator, not [Test]: CharacterVisualEquipment.Unbind() calls the real
            // UnityEngine.Object.Destroy (deliberately, not DestroyImmediate -- this is runtime gameplay
            // code, and Destroy is what the rest of this file already uses in RemoveVisual), which defers
            // actual removal to the end of the frame. A synchronous [Test] would observe the stale child
            // still present immediately after Unbind() returns.
            [UnityTest]
            public IEnumerator UnbindDestroysTheEquippedVisualThenAFreshInitializeBindsCleanly()
            {
                var equipment = CreateVisualEquipment();
                var bus = new GameEventBus(); // the real bus -- this test needs actual Publish delivery
                var rulesDatabase = new RulesDatabase();
                var slotId = new DefinitionId("slot-test");
                visualPrefab = new GameObject("VisualPrefab");
                var item = CreateEquippableItem("item-test-visual-rebind", slotId, visualPrefab);
                rulesDatabase.Register(item);

                var characterA = RuntimeEntityId.NewId();
                equipment.Initialize(characterA, rulesDatabase, bus);
                bus.Publish(EquipContext(characterA, item.Id, slotId));
                Assert.AreEqual(1, go.transform.childCount, "Equipping should have instantiated one visual.");

                // Production-Foundation.md's "session rebuild can retain..." pattern, applied to the
                // equipped-visual instance: the design's contract requires an explicit Unbind() before
                // rebinding to a different character/session (see InitializeWithADifferentBindingWithout...
                // above) -- this proves Unbind() itself is what releases the stale visual.
                equipment.Unbind();
                yield return null; // let the deferred Destroy() actually take effect

                Assert.AreEqual(0, go.transform.childCount, "Unbind must release the previous character's equipped visual.");

                var characterB = RuntimeEntityId.NewId();
                Assert.DoesNotThrow(() => equipment.Initialize(characterB, rulesDatabase, bus));
            }
        }
    }
}
