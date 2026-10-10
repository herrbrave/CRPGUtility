using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using CRPG.Actors;
using CRPG.Core;
using CRPG.Gameplay;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.TestTools;
using VikingRaid.Appearance;

namespace VikingRaid.Tests
{
    /// <summary>A Sidekick goblin NPC spawned through the toolkit in the REAL village scene: dressed from its definition, animated (the parts follow the skeleton), and re-dressed from its saved STATE.</summary>
    public sealed class VillageGoblinTests : VillageTestBase
    {
        private const string GoblinBasePath = "Assets/Characters/Npcs/GoblinNpcBase.prefab";
        private CharacterAgent goblin;
        private CharacterState state;
        private NpcDefinition definition;

        private static void Set(object target, string field, object value)
        {
            for (var type = target.GetType(); type != null; type = type.BaseType)
            {
                var f = type.GetField(field, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.DeclaredOnly);
                if (f != null) { f.SetValue(target, value); return; }
            }

            throw new System.Exception("no field " + field);
        }

        private static string Region(SkinnedMeshRenderer s) => Regex.Match(s.sharedMesh.name, @"_(\d\d[A-Z]{4})_").Groups[1].Value;
        private static SkinnedMeshRenderer[] Parts(GameObject go) => go.GetComponentsInChildren<SkinnedMeshRenderer>(true).Where(s => s.GetComponentInParent<ModelAppearancePart>(true) != null && s.gameObject.activeInHierarchy).ToArray();

        /// <summary>Spawns a goblin next to the first villager, cloning a villager's definition (its stats and resources) with the goblin base prefab and a look.</summary>
        private void SpawnGoblin(params (string slot, string option)[] look)
        {
            GameObject prefab = null;
            NpcDefinition source = null;
#if UNITY_EDITOR
            prefab = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(GoblinBasePath);
            source = UnityEditor.AssetDatabase.LoadAssetAtPath<NpcDefinition>("Assets/Content/Village/Npcs/npc-village-hild.asset");
#endif
            Assert.IsNotNull(prefab, GoblinBasePath + " is missing: run 'Viking Raid/Appearance/Author Goblin Base Prefab'");
            Assert.IsNotNull(source);
            definition = Object.Instantiate(source);
            Set(definition, "id", "npc-test-goblin");
            Set(definition, "displayName", "Test Goblin");
            Set(definition, "visualPrefab", prefab);
            Set(definition, "appearance", look.Select(l => new AppearanceEntry(new DefinitionId(l.slot), new DefinitionId(l.option))).ToList());

            var anchor = Village.Npcs.Values.First(a => a != null).transform.position;
            NavMesh.SamplePosition(anchor + new Vector3(2f, 0f, 2f), out var hit, 6f, NavMesh.AllAreas);
            goblin = Village.Spawner.SpawnNpc("test-goblin", definition, hit.position, Quaternion.identity);
            Assert.IsTrue(Village.Session.Characters.TryGetCharacter(goblin.EntityId, out state));
        }

        [TearDown]
        public void DestroyDefinition() { if (definition != null) Object.Destroy(definition); }

        private void AssertOneRendererPerRegionAndAllAreFollowingTheSkeleton()
        {
            var parts = Parts(goblin.gameObject);
            var regions = parts.Select(Region).ToList();
            Assert.AreEqual(regions.Count, regions.Distinct().Count(), "one renderer per region: " + string.Join(",", regions));
            foreach (var required in new[] { "01HEAD", "10TORS", "17HIPS", "21FOTR", "07EARL", "35NOSE" }) CollectionAssert.Contains(regions, required);

            // the animated hand: the baked mesh sits at the bone, not where the bind pose would leave it
            var hand = parts.Single(s => Region(s) == "15HNDL");
            var bone = goblin.GetComponentsInChildren<Transform>(true).First(t => t.name == "hand_l" && t.GetComponentInParent<ModelAppearancePart>(true) == null);
            var baked = new Mesh();
            hand.BakeMesh(baked);
            var centre = hand.transform.TransformPoint(baked.bounds.center);
            Object.Destroy(baked);
            var bindCentre = hand.transform.TransformPoint(hand.sharedMesh.bounds.center);
            var toBone = Vector3.Distance(centre, bone.position);
            Assert.Less(toBone, 0.4f, "the hand mesh follows the animated bone");
            Assert.Less(toBone, Vector3.Distance(centre, bindCentre) * 0.5f, $"and is nearer the bone ({toBone:F2} m) than where the bind pose would leave it ({Vector3.Distance(centre, bindCentre):F2} m)");
        }

        [UnityTest]
        public IEnumerator AGoblinSpawnedFromADefinitionIsDressedAndFollowsItsAnimation()
        {
            yield return null;
            SpawnGoblin(("face", "face-goblin-04"), ("hair", "hair-goblin-08"), ("torso", "torso-set-02"), ("legs", "legs-set-07"), ("helm", "helm-set-04"),
                ("shoulders", "shoulders-set-05"), ("back", "back-set-03"), ("belt", "belt-set-06"), ("look", "look-fighter-3"), ("build", "build-buff"));
            yield return null; yield return null;   // Start dresses the character; the animator plays its idle

            Assert.IsTrue(goblin.GetComponent<ModelAppearanceBinder>().IsApplied);
            Assert.IsNotEmpty(state.Appearance, "the spawn copied the definition's appearance onto the character");
            AssertOneRendererPerRegionAndAllAreFollowingTheSkeleton();
            Assert.IsTrue(Parts(goblin.gameObject).Where(s => Region(s) == "10TORS").All(s => s.sharedMesh.name.Contains("FIGT_02")), "the torso is set 2");
            Assert.IsTrue(Parts(goblin.gameObject).All(s => s.sharedMaterial.name == "GoblinFighter_03"), "the whole goblin wears the chosen colour map");
            yield return new WaitForSeconds(0.5f);
            AssertOneRendererPerRegionAndAllAreFollowingTheSkeleton();   // a later frame of the animation
            AssertNoErrorsLogged();
        }

        [UnityTest]
        public IEnumerator ChangingTheStateAndRefreshingRedressesTheLiveGoblinAndItStillFollowsItsAnimation()
        {
            yield return null;
            SpawnGoblin(("torso", "torso-set-02"));
            yield return null; yield return null;

            state.SetAppearance(new DefinitionId("torso"), new DefinitionId("torso-set-09"));
            state.SetAppearance(new DefinitionId("helm"), new DefinitionId("helm-set-01"));
            state.SetAppearance(new DefinitionId("face"), new DefinitionId("face-goblin-07"));
            goblin.GetComponent<ModelAppearanceBinder>().Refresh();
            yield return null; yield return null;   // the replaced parts are destroyed at the end of the frame

            AssertOneRendererPerRegionAndAllAreFollowingTheSkeleton();
            Assert.IsTrue(Parts(goblin.gameObject).Where(s => Region(s) == "10TORS").All(s => s.sharedMesh.name.Contains("FIGT_09")));
            Assert.IsNotNull(Parts(goblin.gameObject).SingleOrDefault(s => Region(s) == "22AHED"), "the helm arrived on an animated character");
            AssertNoErrorsLogged();
        }

        [UnityTest]
        public IEnumerator AViewRebuiltFromSavedStateIsDressedFromTheStateNotTheDefinition()
        {
            yield return null;
            SpawnGoblin(("torso", "torso-set-02"), ("face", "face-goblin-02"));
            yield return null; yield return null;
            var id = goblin.EntityId;
            state.ClearAllAppearance();
            state.SetAppearance(new DefinitionId("face"), new DefinitionId("face-goblin-09"));
            state.SetAppearance(new DefinitionId("torso"), new DefinitionId("torso-set-06"));
            state.SetAppearance(new DefinitionId("look"), new DefinitionId("look-species-2"));
            var position = goblin.transform.position;
            GameObject prefab = null;
#if UNITY_EDITOR
            prefab = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(GoblinBasePath);
#endif
            Object.Destroy(goblin.gameObject);
            yield return null;
            var restored = Village.Spawner.BindRestoredView("test-goblin", id, DefinitionId.None, prefab, position, Quaternion.identity, DefinitionId.None, startInactive: false);
            yield return null; yield return null;

            goblin = restored.GetComponent<CharacterAgent>();
            AssertOneRendererPerRegionAndAllAreFollowingTheSkeleton();
            Assert.IsTrue(Parts(restored).Where(s => Region(s) == "10TORS").All(s => s.sharedMesh.name.Contains("FIGT_06")));
            Assert.IsTrue(Parts(restored).Where(s => Region(s) == "07EARL").All(s => s.sharedMesh.name.Contains("BASE_09")), "face 9 from the state, not face 2 from the definition");
            Assert.IsTrue(Parts(restored).All(s => s.sharedMaterial.name == "GoblinSpecies_02"));
            AssertNoErrorsLogged();
        }
    }
}
