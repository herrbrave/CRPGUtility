using System.Collections;
using System.Collections.Generic;
using System.Linq;
using CRPG.Actors;
using CRPG.Core;
using CRPG.Gameplay;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using VikingRaid.Appearance;

namespace VikingRaid.Tests
{
    /// <summary>The villagers in the REAL village scene wear what their NpcDefinition (and then their saved CharacterState) says: spawn through the toolkit's WorldSpawnService, look from the binder.</summary>
    public sealed class VillageAppearanceTests : VillageTestBase
    {
        private ModelAppearanceCatalog catalog;
        private ModelAppearanceCatalog Catalog => catalog != null ? catalog : catalog = Village.Npcs.Values.First(a => a != null && a.GetComponent<ModelAppearanceBinder>() != null).GetComponent<ModelAppearanceBinder>().Catalog;

        private IEnumerable<(string SpawnId, CharacterAgent Agent, CharacterState State)> Villagers()
        {
            foreach (var pair in Village.Npcs)
                if (pair.Value != null && pair.Value.GetComponent<ModelAppearanceBinder>() != null && Village.Session.Characters.TryGetCharacter(pair.Value.EntityId, out var state))
                    yield return (pair.Key, pair.Value, state);
        }

        private static ModelAppearancePart[] Parts(GameObject go) => go.GetComponentsInChildren<ModelAppearancePart>(true).Where(p => p.gameObject.activeInHierarchy).ToArray();

        private static string ActiveBody(GameObject go) => go.GetComponentsInChildren<SkinnedMeshRenderer>(false)
            .Where(s => s.GetComponentInParent<ModelAppearancePart>() == null && s.name.StartsWith("SM_Chr_") && !s.name.Contains("Attach")).Select(s => s.name).Single();

        /// <summary>What the character should be wearing, by option id, per catalog row: attachments only (toggles are checked by mesh name).</summary>
        private void AssertDressedAs(CharacterAgent agent, IReadOnlyDictionary<DefinitionId, DefinitionId> appearance)
        {
            foreach (var slot in Catalog.Slots)
            {
                var wanted = ModelAppearanceApplier.Resolve(slot, appearance);
                var parts = Parts(agent.gameObject).Where(p => p.SlotId == slot.id).ToArray();
                if (wanted == null) { Assert.IsEmpty(parts, $"{agent.name}: {slot.id} should be empty"); continue; }
                switch (wanted.kind)
                {
                    case ModelAppearanceKind.AttachStatic:
                    case ModelAppearanceKind.AttachOwnRig:
                    case ModelAppearanceKind.AttachRigged: Assert.AreEqual(wanted.id, parts.Single().OptionId, $"{agent.name}: {slot.id}"); break;
                    case ModelAppearanceKind.ToggleChild:
                        Assert.IsTrue(agent.GetComponentsInChildren<SkinnedMeshRenderer>(false).Any(s => s.name == wanted.childName && s.GetComponentInParent<ModelAppearancePart>() == null), $"{agent.name}: {wanted.childName} is on");
                        break;
                }
            }
        }

        [UnityTest]
        public IEnumerator EveryVillagerIsDressedFromItsDefinitionsAppearance()
        {
            yield return null;
            var villagers = Villagers().ToList();
            Assert.GreaterOrEqual(villagers.Count, 3, "the elder, Hild and Orm");
            foreach (var (_, agent, state) in villagers)
            {
                Assert.IsTrue(agent.GetComponent<ModelAppearanceBinder>().IsApplied, agent.name);
                Assert.IsNotEmpty(state.Appearance, $"{agent.name}: the spawn copied the definition's appearance onto the character");
                AssertDressedAs(agent, state.Appearance);
            }

            AssertNoErrorsLogged();
        }

        [UnityTest]
        public IEnumerator VillagersLookDifferentFromEachOther()
        {
            yield return null;
            var looks = Villagers().Select(v => string.Join("|", v.State.Appearance.OrderBy(p => p.Key.Value).Select(p => p.Key.Value + "=" + p.Value.Value))).ToList();
            Assert.AreEqual(looks.Count, looks.Distinct().Count());
            Assert.AreEqual(looks.Count, Villagers().Select(v => ActiveBody(v.Agent.gameObject)).Count());
        }

        [UnityTest]
        public IEnumerator ChangingTheStateAndRefreshingRedressesTheLiveCharacter()
        {
            yield return null;
            var (_, agent, state) = Villagers().First();
            var binder = agent.GetComponent<ModelAppearanceBinder>();

            state.SetAppearance(new DefinitionId("headwear"), new DefinitionId("headwear-viking-helmet-horns-01"));
            state.SetAppearance(new DefinitionId("body"), new DefinitionId("body-warrior-female-02"));
            binder.Refresh();
            yield return null;   // the replaced parts are destroyed at the end of the frame

            AssertDressedAs(agent, state.Appearance);
            Assert.AreEqual("SM_Chr_Warrior_Female_02", ActiveBody(agent.gameObject));

            state.SetAppearance(new DefinitionId("headwear"), new DefinitionId("headwear-war-camp-hood-01"));
            binder.Refresh();
            yield return null;
            Assert.AreEqual(1, agent.GetComponentsInChildren<ModelAppearancePart>(true).Count(p => p.SlotId == "headwear"), "the horned helmet is gone");
            AssertNoErrorsLogged();
        }

        [UnityTest]
        public IEnumerator AViewRebuiltFromSavedStateIsDressedFromTheStateNotTheDefinition()
        {
            yield return null;
            var (spawnId, agent, state) = Villagers().First();
            var id = agent.EntityId;
            // the character's state is what a save holds: change it away from the definition, then destroy the view and bind a restored one (the load path)
            state.ClearAllAppearance();
            state.SetAppearance(new DefinitionId("body"), new DefinitionId("body-leader-female-01"));
            state.SetAppearance(new DefinitionId("look"), new DefinitionId("look-05-c"));
            state.SetAppearance(new DefinitionId("cloak"), new DefinitionId("cloak-war-camp-cape-04"));
            var position = agent.transform.position; var rotation = agent.transform.rotation;
            GameObject prefab = null;
#if UNITY_EDITOR
            prefab = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Characters/Villagers/VikingNpcBase.prefab");
#endif
            Assert.IsNotNull(prefab);

            Object.Destroy(agent.gameObject);
            yield return null;
            // the spawn record for this spawnId and character already exists, so the definition id only matters for a brand-new record
            var restored = Village.Spawner.BindRestoredView(spawnId, id, DefinitionId.None, prefab, position, rotation, new DefinitionId("faction-villagers"), startInactive: false);
            yield return null; yield return null;

            var rebuilt = restored.GetComponent<CharacterAgent>();
            Assert.AreEqual("SM_Chr_Leader_Female_01", ActiveBody(restored));
            AssertDressedAs(rebuilt, state.Appearance);
            Assert.AreEqual("cloak-war-camp-cape-04", Parts(restored).Single(p => p.SlotId == "cloak").OptionId);
            AssertNoErrorsLogged();
        }
    }
}
