using System;
using System.Collections;
using System.Reflection;
using CRPG.Actors;
using CRPG.Core;
using CRPG.Gameplay;
using CRPG.Inventory;
using CRPG.Presentation;
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
    /// CharacterVisualEquipment) versus VisualRoot/Model (replaceable art, managed by CharacterView).
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
        private (CharacterAgent agent, CharacterPlayableAnimator animator, CharacterVisualEquipment equipment, CharacterView view, GameObject model)
            CreateCharacterRootWithModel()
        {
            go = new GameObject("CharacterRoot");
            go.AddComponent<NavMeshAgent>();
            var agent = go.AddComponent<CharacterAgent>();
            var animator = go.AddComponent<CharacterPlayableAnimator>();
            var equipment = go.AddComponent<CharacterVisualEquipment>();

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

            // Indirect proof: BindPresentation creates a live PlayableGraph; nothing else does.
            Assert.IsTrue(((PlayableGraph)GetPrivateField(animator, "graph")).IsValid());
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

                Assert.IsTrue(((PlayableGraph)GetPrivateField(animator, "graph")).IsValid());
            }

            [Test]
            public void RebindingToADifferentAnimatorDestroysThePreviousGraph()
            {
                var animator = CreateAnimator();
                modelGo = CreateModel("ModelA");
                var modelB = CreateModel("ModelB");

                animator.BindPresentation(modelGo.GetComponent<Animator>(), null);
                var firstGraph = (PlayableGraph)GetPrivateField(animator, "graph");

                animator.BindPresentation(modelB.GetComponent<Animator>(), null);

                Assert.IsFalse(firstGraph.IsValid(), "The previous graph must be destroyed, not leaked, when rebinding to a new Animator.");
                Assert.IsTrue(((PlayableGraph)GetPrivateField(animator, "graph")).IsValid());
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
                Assert.IsFalse(((PlayableGraph)GetPrivateField(animator, "graph")).IsValid());
            }
        }

        public class CharacterVisualEquipmentRefreshBoneMapTests
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

            [UnityTest]
            public IEnumerator RefreshBoneMapDestroysStaleEquippedVisuals()
            {
                characterGo = new GameObject("CharacterRoot");
                var equipment = characterGo.AddComponent<CharacterVisualEquipment>();
                var bus = new GameEventBus();
                var rulesDatabase = new RulesDatabase();
                var slotId = new DefinitionId("slot-test");
                visualPrefab = new GameObject("VisualPrefab");
                var item = CreateEquippableItem("item-refresh-test", slotId, visualPrefab);
                rulesDatabase.Register(item);
                var character = RuntimeEntityId.NewId();

                equipment.Initialize(character, rulesDatabase, bus);
                bus.Publish(EquipContext(character, item.Id, slotId));
                Assert.AreEqual(1, characterGo.transform.childCount, "Equipping should have instantiated one visual.");

                equipment.RefreshBoneMap();
                yield return null; // let the deferred Destroy() actually take effect

                Assert.AreEqual(0, characterGo.transform.childCount, "RefreshBoneMap must release visuals bound to the previous skeleton.");
            }

            [Test]
            public void RefreshBoneMapPicksUpTheNewSkeletonsBones()
            {
                characterGo = new GameObject("CharacterRoot");
                var equipment = characterGo.AddComponent<CharacterVisualEquipment>();

                var boneA = new GameObject("Hips");
                boneA.transform.SetParent(characterGo.transform);
                var boneATransform = boneA.transform; // captured before destruction, below
                equipment.RefreshBoneMap();
                var mapAfterA = (System.Collections.Generic.Dictionary<string, Transform>)GetPrivateField(equipment, "bonesByName");
                Assert.AreSame(boneATransform, mapAfterA["Hips"]);

                UnityEngine.Object.DestroyImmediate(boneA);
                var boneB = new GameObject("Hips"); // a different Transform instance, same bone name
                boneB.transform.SetParent(characterGo.transform);
                equipment.RefreshBoneMap();
                var mapAfterB = (System.Collections.Generic.Dictionary<string, Transform>)GetPrivateField(equipment, "bonesByName");

                Assert.AreSame(boneB.transform, mapAfterB["Hips"]);
                Assert.AreNotSame(boneATransform, mapAfterB["Hips"], "The bone map must not still point at the previous (now-destroyed) skeleton's Transform.");
            }
        }
    }
}
