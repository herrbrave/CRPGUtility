using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using CRPG.Core;
using CRPG.Gameplay;
using CRPG.Inventory;
using CRPG.Presentation;
using CRPG.Presentation.Animation;
using CRPG.Presentation.Equipment;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.Playables;
using UnityEngine.TestTools;

namespace CRPG.Tests.PlayMode
{
    /// <summary>
    /// Task 1.3 (Production Foundation): the art-independent wrapper (Docs/Design/Production-Foundation.md
    /// sec.4.1) -- CharacterRoot (gameplay: CharacterAgent/NavMeshAgent/CharacterPlayableAnimator/
    /// EquipmentVisualBinder) versus VisualRoot/Model (replaceable art, managed by CharacterView).
    /// Acceptance from the implementation plan: replacing only Model must leave movement/selection identity
    /// valid and never duplicate a motor/Animator/physics writer.
    /// </summary>
    public class CharacterWrapperTests
    {
        private GameObject go;

        [TearDown]
        public void TearDown()
        {
            if (go != null) UnityEngine.Object.DestroyImmediate(go);
        }

        private static object GetPrivateField(object target, string fieldName)
        {
            var type = target.GetType();
            while (type != null)
            {
                var field = type.GetField(fieldName, BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.DeclaredOnly);
                if (field != null) return field.GetValue(target);
                type = type.BaseType;
            }

            throw new Exception($"Field '{fieldName}' not found on {target.GetType().Name} or its base types.");
        }

        private static GameObject CreateModel(string name)
        {
            var model = new GameObject(name);
            model.AddComponent<Animator>();
            return model;
        }

        /// <summary>Builds CharacterRoot/VisualRoot/Model with an already-authored Model child, matching how
        /// BaseHumanMaleCharacter.prefab is migrated -- CharacterView's Awake() auto-binds to it.</summary>
        private (CharacterAgent agent, CharacterPlayableAnimator animator, EquipmentVisualBinder equipment, CharacterView view, GameObject model)
            CreateCharacterRootWithModel()
        {
            go = new GameObject("CharacterRoot");
            go.AddComponent<NavMeshAgent>();
            var agent = go.AddComponent<CharacterAgent>();
            var animator = go.AddComponent<CharacterPlayableAnimator>();
            var equipment = go.AddComponent<EquipmentVisualBinder>();

            var visualRoot = new GameObject("VisualRoot");
            visualRoot.transform.SetParent(go.transform, false);

            var model = CreateModel("Model");
            model.transform.SetParent(visualRoot.transform, false);

            var view = visualRoot.AddComponent<CharacterView>(); // Awake runs now, auto-binds Model

            return (agent, animator, equipment, view, model);
        }

        [Test]
        public void CharacterViewAutomaticallyBindsAnAlreadyPresentModelAtAwake()
        {
            var (_, _, _, view, model) = CreateCharacterRootWithModel();

            Assert.AreSame(model, view.Model);
            Assert.IsNotNull(view.Animator);
            Assert.AreSame(model.GetComponent<Animator>(), view.Animator);
        }

        [Test]
        public void BindPresentationIsCalledDuringAutomaticBind()
        {
            var (_, animator, _, _, _) = CreateCharacterRootWithModel();

            Assert.IsTrue(animator.IsPresentationBound);
        }

        [Test]
        public void CharacterAgentResolvesViewOnBind()
        {
            var (agent, _, _, view, _) = CreateCharacterRootWithModel();
            var session = new GameSession(new RulesDatabase(), new DiceService(new SeededRandomSource(1)));

            agent.Bind(RuntimeEntityId.NewId(), session);

            Assert.AreSame(view, agent.View);
        }

        [UnityTest]
        public IEnumerator SetModelReplacesTheModelAndDestroysTheOldOne()
        {
            var (_, _, _, view, oldModel) = CreateCharacterRootWithModel();
            var newModelPrefab = CreateModel("ReplacementModelPrefab");

            view.SetModel(newModelPrefab);
            yield return null; // let the deferred Destroy(oldModel) actually take effect

            Assert.AreNotSame(oldModel, view.Model);
            Assert.IsTrue(oldModel == null, "The old Model instance must be destroyed, not just detached.");
            UnityEngine.Object.DestroyImmediate(newModelPrefab);
        }

        [UnityTest]
        public IEnumerator SetModelLeavesExactlyOneNavMeshAgentAndOneAnimatorInTheHierarchy()
        {
            var (_, _, _, view, _) = CreateCharacterRootWithModel();
            var newModelPrefab = CreateModel("ReplacementModelPrefab");

            view.SetModel(newModelPrefab);
            yield return null;

            Assert.AreEqual(1, go.GetComponentsInChildren<NavMeshAgent>(true).Length, "SetModel must never duplicate CharacterRoot's NavMeshAgent.");
            Assert.AreEqual(1, go.GetComponentsInChildren<Animator>(true).Length, "SetModel must leave exactly one live Animator, not one per swap.");
            UnityEngine.Object.DestroyImmediate(newModelPrefab);
        }

        [UnityTest]
        public IEnumerator MotorAndIdentityAreUnaffectedBySettingModel()
        {
            var (agent, _, _, view, _) = CreateCharacterRootWithModel();
            var session = new GameSession(new RulesDatabase(), new DiceService(new SeededRandomSource(1)));
            var entityId = RuntimeEntityId.NewId();
            agent.Bind(entityId, session);
            var motorBefore = agent.Motor;

            var newModelPrefab = CreateModel("ReplacementModelPrefab");
            view.SetModel(newModelPrefab);
            yield return null;

            Assert.AreEqual(entityId, agent.EntityId, "Replacing Model must not disturb the character's entity identity.");
            Assert.AreSame(session, agent.Session, "Replacing Model must not disturb the character's session binding.");
            Assert.AreSame(motorBefore, agent.Motor, "Replacing Model must not disturb (or duplicate) the movement motor -- it lives on CharacterRoot.");
            UnityEngine.Object.DestroyImmediate(newModelPrefab);
        }

        [Test]
        public void SetModelThrowsWhenPrefabIsNull()
        {
            var (_, _, _, view, _) = CreateCharacterRootWithModel();
            Assert.Throws<ArgumentNullException>(() => view.SetModel(null));
        }

        // Regression guard for a real, empirically-confirmed Unity gotcha (not covered by the synthetic
        // fixture above, which builds its hierarchy via AddComponent and never hits this): when Model is a
        // nested PrefabInstance -- exactly how BaseHumanMaleCharacter.prefab is authored -- it is NOT
        // guaranteed attached yet during Awake() of sibling/ancestor components in the same outer
        // instantiate, even though it is unambiguously present the instant Instantiate() returns to the
        // caller. CharacterView.Start() is the fallback that catches this a frame later; a synchronous [Test]
        // against a real such prefab would incorrectly see Model still null.
        [UnityTest]
        public IEnumerator TheRealMigratedHumanMalePrefabBindsItsNestedModelByTheFollowingFrame()
        {
            var prefab = Resources.Load<GameObject>("NPCs/BaseHumanMaleCharacter");
            Assert.IsNotNull(prefab, "Expected TestProject's Assets/Resources/NPCs/BaseHumanMaleCharacter.prefab to exist.");

            go = UnityEngine.Object.Instantiate(prefab);
            var view = go.GetComponentInChildren<CharacterView>();
            Assert.IsNotNull(view, "The migrated prefab must have a CharacterView on its VisualRoot child.");

            yield return null; // let CharacterView.Start()'s fallback bind actually run

            Assert.IsNotNull(view.Model, "CharacterView must bind its nested-PrefabInstance Model by the frame after instantiation.");
            Assert.IsNotNull(view.Animator, "The bound Model's Animator must be resolved too.");

            var agent = go.GetComponent<CharacterAgent>();
            Assert.IsNotNull(agent, "CharacterRoot must carry exactly the gameplay components the migration authored.");
            Assert.AreEqual(1, go.GetComponentsInChildren<NavMeshAgent>(true).Length);
            Assert.AreEqual(1, go.GetComponentsInChildren<Animator>(true).Length);
        }

        public class CharacterPlayableAnimatorPresentationTests
        {
            private GameObject go;
            private GameObject modelGo;

            [TearDown]
            public void TearDown()
            {
                if (go != null) UnityEngine.Object.DestroyImmediate(go);
                if (modelGo != null) UnityEngine.Object.DestroyImmediate(modelGo);
            }

            private CharacterPlayableAnimator CreateAnimator()
            {
                go = new GameObject("TestCharacterRoot");
                return go.AddComponent<CharacterPlayableAnimator>();
            }

            [Test]
            public void UpdateDoesNotThrowBeforeAnyPresentationIsBound()
            {
                var animator = CreateAnimator();
                Assert.DoesNotThrow(() => typeof(CharacterPlayableAnimator)
                    .GetMethod("Update", BindingFlags.NonPublic | BindingFlags.Instance)!
                    .Invoke(animator, null));
            }

            [Test]
            public void BindPresentationThrowsWhenAnimatorIsNull()
            {
                var animator = CreateAnimator();
                Assert.Throws<ArgumentNullException>(() => animator.BindPresentation(null, null));
            }

            [Test]
            public void BindPresentationCreatesAValidGraph()
            {
                var animator = CreateAnimator();
                modelGo = CreateModel("Model");

                animator.BindPresentation(modelGo.GetComponent<Animator>(), null);

                Assert.IsTrue(animator.IsPresentationBound);
            }

            [Test]
            public void RebindingToADifferentAnimatorStillLeavesExactlyOneLiveGraph()
            {
                // The actual graph-object lifecycle (is the PREVIOUS graph specifically destroyed, not just
                // superseded) is covered directly against PlayableCharacterAnimationDriver in
                // PlayableCharacterAnimationDriverTests.RebindingToADifferentAnimatorDestroysThePreviousGraph
                // -- CharacterPlayableAnimator no longer owns a graph field to reflect into (Task 2.2), so
                // this level only re-confirms the public, black-box contract: still bound, no exception, and
                // (via BindPresentationDoesNotThrowWhenCalledTwiceInARow-style coverage) no duplicate Animator
                // output left driving the old Model.
                var animator = CreateAnimator();
                modelGo = CreateModel("ModelA");
                var modelB = CreateModel("ModelB");

                animator.BindPresentation(modelGo.GetComponent<Animator>(), null);
                Assert.DoesNotThrow(() => animator.BindPresentation(modelB.GetComponent<Animator>(), null));

                Assert.IsTrue(animator.IsPresentationBound);
                UnityEngine.Object.DestroyImmediate(modelB);
            }

            [Test]
            public void UnbindPresentationIsIdempotent()
            {
                var animator = CreateAnimator();
                modelGo = CreateModel("Model");
                animator.BindPresentation(modelGo.GetComponent<Animator>(), null);

                animator.UnbindPresentation();
                Assert.DoesNotThrow(() => animator.UnbindPresentation());
                Assert.IsFalse(animator.IsPresentationBound);
            }
        }

        /// <summary>Task 2.2: the actual PlayableGraph/mixer/port lifecycle now lives entirely in
        /// PlayableCharacterAnimationDriver, a plain C# class -- testable directly (still needs a real
        /// Animator component to bind against, so PlayMode, but no GameObject/MonoBehaviour host of its own
        /// required) rather than only indirectly through CharacterPlayableAnimator's black-box IsPresentationBound.</summary>
        public class PlayableCharacterAnimationDriverTests
        {
            private GameObject modelGo;
            private GameObject modelGoB;

            [TearDown]
            public void TearDown()
            {
                if (modelGo != null) UnityEngine.Object.DestroyImmediate(modelGo);
                if (modelGoB != null) UnityEngine.Object.DestroyImmediate(modelGoB);
            }

            [Test]
            public void BindCreatesAValidGraph()
            {
                modelGo = CreateModel("Model");
                var driver = new PlayableCharacterAnimationDriver();

                driver.Bind(modelGo.GetComponent<Animator>());

                Assert.IsTrue(driver.IsBound);
            }

            [Test]
            public void BindThrowsWhenAnimatorIsNull()
            {
                var driver = new PlayableCharacterAnimationDriver();
                Assert.Throws<ArgumentNullException>(() => driver.Bind(null));
            }

            [Test]
            public void RebindingToADifferentAnimatorDestroysThePreviousGraph()
            {
                modelGo = CreateModel("ModelA");
                modelGoB = CreateModel("ModelB");
                var driver = new PlayableCharacterAnimationDriver();

                driver.Bind(modelGo.GetComponent<Animator>());
                var firstGraph = (PlayableGraph)GetPrivateField(driver, "graph");

                driver.Bind(modelGoB.GetComponent<Animator>());

                Assert.IsFalse(firstGraph.IsValid(), "The previous graph must be destroyed, not leaked, when rebinding to a new Animator.");
                Assert.IsTrue(driver.IsBound);
            }

            [Test]
            public void UnbindIsIdempotent()
            {
                modelGo = CreateModel("Model");
                var driver = new PlayableCharacterAnimationDriver();
                driver.Bind(modelGo.GetComponent<Animator>());

                driver.Unbind();
                Assert.DoesNotThrow(() => driver.Unbind());
                Assert.IsFalse(driver.IsBound);
            }

            [Test]
            public void TickDoesNotThrowBeforeAnyBind()
            {
                var driver = new PlayableCharacterAnimationDriver();
                Assert.DoesNotThrow(() => driver.Tick(0.016f));
            }

            [Test]
            public void PlayOnceWithAMissingClipCompletesImmediately()
            {
                modelGo = CreateModel("Model");
                var driver = new PlayableCharacterAnimationDriver();
                driver.Bind(modelGo.GetComponent<Animator>());

                var completedCount = 0;
                driver.PlayOnce(null, () => completedCount++);

                Assert.AreEqual(1, completedCount, "Missing art must not soft-lock the caller -- design sec.4.4.");
            }
        }

        /// <summary>Task 2.4: renamed from CharacterVisualEquipmentRefreshBoneMapTests -- the migrated
        /// EquipmentVisualBinder's RefreshBoneMap(CharacterRigDefinition) now also performs the "initial full
        /// reconciliation from inventory on bind/load" design sec.4.5 calls for; the full
        /// Rigid/Skinned/hiding-region test matrix lives in the new Tests/Runtime/EquipmentVisualBindingTests.cs
        /// instead of here -- this class keeps just the two RefreshBoneMap-specific scenarios that were
        /// already here.</summary>
        public class EquipmentVisualBinderRefreshBoneMapTests
        {
            private GameObject characterGo;
            private GameObject visualPrefab;

            [TearDown]
            public void TearDown()
            {
                if (characterGo != null) UnityEngine.Object.DestroyImmediate(characterGo);
                if (visualPrefab != null) UnityEngine.Object.DestroyImmediate(visualPrefab);
            }

            private static void SetPrivateField(object target, string fieldName, object value)
            {
                var type = target.GetType();
                while (type != null)
                {
                    var field = type.GetField(fieldName, BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.DeclaredOnly);
                    if (field != null) { field.SetValue(target, value); return; }
                    type = type.BaseType;
                }

                throw new Exception($"Field '{fieldName}' not found on {target.GetType().Name} or its base types.");
            }

            private static ItemDefinition RegisterEquippableVisualItem(RulesDatabase rulesDatabase, string itemId, DefinitionId slotId, GameObject prefab)
            {
                var map = ScriptableObject.CreateInstance<RigBindingMap>();
                SetPrivateField(map, "id", $"{itemId}-map");
                SetPrivateField(map, "bonePaths", new List<RigBindingMap.BonePathEntry>());

                var profile = ScriptableObject.CreateInstance<EquipmentVisualProfile>();
                SetPrivateField(profile, "id", $"{itemId}-profile");
                SetPrivateField(profile, "kind", EquipmentVisualKind.Skinned);
                SetPrivateField(profile, "visualPrefab", prefab);
                SetPrivateField(profile, "boneMap", map);
                SetPrivateField(profile, "hiddenBodyRegions", new List<DefinitionId>());

                var item = ScriptableObject.CreateInstance<ItemDefinition>();
                SetPrivateField(item, "id", itemId);
                SetPrivateField(item, "equipmentSlotId", slotId);
                SetPrivateField(item, "equipmentVisualProfileId", profile.Id);

                rulesDatabase.Register(map);
                rulesDatabase.Register(profile);
                rulesDatabase.Register(item);
                return item;
            }

            private static GameEventContext EquipContext(RuntimeEntityId character, DefinitionId itemDefinitionId, DefinitionId slotId)
            {
                var context = new GameEventContext(GameEvent.ItemEquipped, character, character);
                context.Data["ItemDefinitionId"] = itemDefinitionId;
                context.Data["SlotId"] = slotId;
                return context;
            }

            [UnityTest]
            public IEnumerator RefreshBoneMapDestroysStaleEquippedVisuals()
            {
                characterGo = new GameObject("CharacterRoot");
                var equipment = characterGo.AddComponent<EquipmentVisualBinder>();
                equipment.RefreshBoneMap(null); // simulates CharacterView already having bound a (trivial) skeleton
                var bus = new GameEventBus();
                var rulesDatabase = new RulesDatabase();
                var slotId = new DefinitionId("slot-test");
                visualPrefab = new GameObject("VisualPrefab");
                var item = RegisterEquippableVisualItem(rulesDatabase, "item-refresh-test", slotId, visualPrefab);
                var character = RuntimeEntityId.NewId();

                equipment.Initialize(character, rulesDatabase, new PartyInventoryState(), bus);
                bus.Publish(EquipContext(character, item.Id, slotId));
                Assert.AreEqual(1, characterGo.transform.childCount, "Equipping should have instantiated one visual.");

                equipment.RefreshBoneMap(null);
                yield return null; // let the deferred Destroy() actually take effect

                Assert.AreEqual(0, characterGo.transform.childCount, "RefreshBoneMap must release visuals bound to the previous skeleton.");
            }

            [Test]
            public void RefreshBoneMapPicksUpTheNewSkeletonsBones()
            {
                characterGo = new GameObject("CharacterRoot");
                var equipment = characterGo.AddComponent<EquipmentVisualBinder>();

                var boneA = new GameObject("Hips");
                boneA.transform.SetParent(characterGo.transform);
                var boneATransform = boneA.transform; // captured before destruction, below
                equipment.RefreshBoneMap(null);
                var mapAfterA = (Dictionary<string, Transform>)GetPrivateField(equipment, "transformsByName");
                Assert.AreSame(boneATransform, mapAfterA["Hips"]);

                UnityEngine.Object.DestroyImmediate(boneA);
                var boneB = new GameObject("Hips"); // a different Transform instance, same bone name
                boneB.transform.SetParent(characterGo.transform);
                equipment.RefreshBoneMap(null);
                var mapAfterB = (Dictionary<string, Transform>)GetPrivateField(equipment, "transformsByName");

                Assert.AreSame(boneB.transform, mapAfterB["Hips"]);
                Assert.AreNotSame(boneATransform, mapAfterB["Hips"], "The bone map must not still point at the previous (now-destroyed) skeleton's Transform.");
            }
        }
    }
}
