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
    /// <summary>A Sidekick HUMAN NPC (kitbashed from Starter outfits and Polygon Viking pieces) spawned through the toolkit in the REAL village scene: dressed from its definition, animated, facing the way it moves, and re-dressed from its saved STATE.</summary>
    public sealed class VillageHumanTests : VillageTestBase
    {
        private const string HumanBasePath = "Assets/Characters/Npcs/HumanNpcBase.prefab";
        private CharacterAgent human;
        private CharacterState state;
        private NpcDefinition definition;
        private ModelAppearanceCatalog catalog;

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

        private SkinnedMeshRenderer[] SidekickParts(GameObject go) => go.GetComponentsInChildren<SkinnedMeshRenderer>(true)
            .Where(s => s.gameObject.activeInHierarchy && s.GetComponentInParent<ModelAppearancePart>(true) is { } part && catalog.FindSlot(part.SlotId)?.Find(part.OptionId)?.kind == ModelAppearanceKind.AttachParts).ToArray();

        private void SpawnHuman(params (string slot, string option)[] look)
        {
            GameObject prefab = null;
            NpcDefinition source = null;
#if UNITY_EDITOR
            prefab = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(HumanBasePath);
            source = UnityEditor.AssetDatabase.LoadAssetAtPath<NpcDefinition>("Assets/Content/Village/Npcs/npc-village-hild.asset");
            catalog = UnityEditor.AssetDatabase.LoadAssetAtPath<ModelAppearanceCatalog>("Assets/Appearance/Human/HumanAppearanceCatalog.asset");
#endif
            Assert.IsNotNull(prefab, HumanBasePath + " is missing: run 'Viking Raid/Appearance/Author Human Base Prefab'");
            definition = Object.Instantiate(source);
            Set(definition, "id", "npc-test-human");
            Set(definition, "displayName", "Test Human");
            Set(definition, "visualPrefab", prefab);
            Set(definition, "appearance", look.Select(l => new AppearanceEntry(new DefinitionId(l.slot), new DefinitionId(l.option))).ToList());

            var anchor = Village.Npcs.Values.First(a => a != null).transform.position;
            NavMesh.SamplePosition(anchor + new Vector3(2f, 0f, 2f), out var hit, 6f, NavMesh.AllAreas);
            human = Village.Spawner.SpawnNpc("test-human", definition, hit.position, Quaternion.identity);
            Assert.IsTrue(Village.Session.Characters.TryGetCharacter(human.EntityId, out state));
        }

        [TearDown]
        public void DestroyDefinition() { if (definition != null) Object.Destroy(definition); }

        /// <summary>The direction the body faces, from the hip line, as a signed yaw against the root's forward (the definition GameFacingTests uses).</summary>
        private static float HipYaw(GameObject go)
        {
            var animator = go.GetComponentsInChildren<Animator>(true).First(a => a.avatar != null);
            var side = animator.GetBoneTransform(HumanBodyBones.RightUpperLeg).position - animator.GetBoneTransform(HumanBodyBones.LeftUpperLeg).position;
            side.y = 0f;
            var forward = go.transform.forward; forward.y = 0f;
            return Vector3.SignedAngle(forward.normalized, Vector3.Cross(side.normalized, Vector3.up).normalized, Vector3.up);
        }

        private static readonly (string, string)[] FullLook =
        {
            ("face", "face-human-04"), ("hair", "hair-viking-hair-07"), ("facialhair", "facialhair-viking-beard-03"), ("outfit", "outfit-knight"), ("shoulders", "shoulders-knight"),
            ("headwear", "headwear-viking-helmet-horns-02"), ("cloak", "cloak-viking-cape-01"), ("gear", "gear-war-camp-bags-02"), ("look", "look-starter-1"), ("build", "build-buff"),
        };

        private void AssertDressedAndFollowingTheSkeleton()
        {
            var parts = SidekickParts(human.gameObject);
            var regions = parts.Select(Region).ToList();
            Assert.AreEqual(regions.Count, regions.Distinct().Count(), "one Sidekick renderer per region: " + string.Join(",", regions));
            foreach (var required in new[] { "01HEAD", "10TORS", "17HIPS", "21FOTR", "07EARL", "35NOSE", "15HNDL" }) CollectionAssert.Contains(regions, required);

            var hand = parts.Single(s => Region(s) == "15HNDL");
            var bone = human.GetComponentsInChildren<Transform>(true).First(t => t.name == "hand_l" && t.GetComponentInParent<ModelAppearancePart>(true) == null);
            var baked = new Mesh();
            hand.BakeMesh(baked);
            var centre = hand.transform.TransformPoint(baked.bounds.center);
            var bindCentre = hand.transform.TransformPoint(hand.sharedMesh.bounds.center);
            Object.Destroy(baked);
            var toBone = Vector3.Distance(centre, bone.position);
            Assert.Less(toBone, 0.4f, "the hand follows the animated bone");
            Assert.Less(toBone, Vector3.Distance(centre, bindCentre) * 0.5f, "and is nearer the bone than where the bind pose would leave it");
        }

        [UnityTest]
        public IEnumerator AHumanSpawnedFromADefinitionIsDressedKitbashedAndFollowsItsAnimation()
        {
            yield return null;
            SpawnHuman(FullLook);
            yield return null; yield return null;

            Assert.IsTrue(human.GetComponent<ModelAppearanceBinder>().IsApplied);
            Assert.IsNotEmpty(state.Appearance, "the spawn copied the definition's appearance onto the character");
            AssertDressedAndFollowingTheSkeleton();

            var marks = human.GetComponentsInChildren<ModelAppearancePart>(true);
            var helmet = marks.Single(p => p.SlotId == "headwear");
            var cape = marks.Single(p => p.SlotId == "cloak");
            Assert.AreEqual("head", helmet.transform.parent.name);
            Assert.AreEqual("spine_03", cape.transform.parent.name);
            Assert.IsTrue(helmet.GetComponentsInChildren<Renderer>(true).All(r => r.sharedMaterial.name.StartsWith("PolygonVikingRealm")), "the helmet keeps its Polygon material, not the human colour map");
            Assert.IsTrue(SidekickParts(human.gameObject).All(s => s.sharedMaterial.name == "Starter_01"), "every Sidekick part wears the chosen colour map");
            yield return new WaitForSeconds(0.5f);
            AssertDressedAndFollowingTheSkeleton();
            AssertNoErrorsLogged();
        }

        [UnityTest]
        public IEnumerator ChangingTheStateAndRefreshingRedressesTheLiveHuman()
        {
            yield return null;
            SpawnHuman(("outfit", "outfit-civilian"), ("headwear", "headwear-viking-helmet-01"));
            yield return null; yield return null;

            state.SetAppearance(new DefinitionId("outfit"), new DefinitionId("outfit-tunic"));
            state.SetAppearance(new DefinitionId("headwear"), new DefinitionId("headwear-war-camp-hat-03"));
            state.SetAppearance(new DefinitionId("facialhair"), new DefinitionId("facialhair-human-05"));
            human.GetComponent<ModelAppearanceBinder>().Refresh();
            yield return null; yield return null;

            AssertDressedAndFollowingTheSkeleton();
            Assert.IsTrue(SidekickParts(human.gameObject).Where(s => Region(s) == "10TORS").All(s => s.sharedMesh.name.Contains("TUNC") || s.sharedMesh.name.Contains("BASE_01")), "the tunic torso");
            var marks = human.GetComponentsInChildren<ModelAppearancePart>(true);
            Assert.AreEqual("headwear-war-camp-hat-03", marks.Single(p => p.SlotId == "headwear").OptionId, "the old helmet is gone");
            Assert.IsNotEmpty(marks.Where(p => p.SlotId == "facialhair"));
            AssertNoErrorsLogged();
        }

        [UnityTest]
        public IEnumerator AViewRebuiltFromSavedStateIsDressedFromTheStateNotTheDefinition()
        {
            yield return null;
            SpawnHuman(("outfit", "outfit-knight"), ("face", "face-human-02"));
            yield return null; yield return null;
            var id = human.EntityId;
            state.ClearAllAppearance();
            state.SetAppearance(new DefinitionId("face"), new DefinitionId("face-human-09"));
            state.SetAppearance(new DefinitionId("outfit"), new DefinitionId("outfit-civilian"));
            state.SetAppearance(new DefinitionId("look"), new DefinitionId("look-starter-3"));
            var position = human.transform.position;
            GameObject prefab = null;
#if UNITY_EDITOR
            prefab = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(HumanBasePath);
#endif
            Object.Destroy(human.gameObject);
            yield return null;
            var restored = Village.Spawner.BindRestoredView("test-human", id, DefinitionId.None, prefab, position, Quaternion.identity, DefinitionId.None, startInactive: false);
            yield return null; yield return null;

            human = restored.GetComponent<CharacterAgent>();
            AssertDressedAndFollowingTheSkeleton();
            Assert.IsTrue(SidekickParts(restored).Where(s => Region(s) == "07EARL").All(s => s.sharedMesh.name.Contains("BASE_09")), "face 9 from the state, not face 2 from the definition");
            Assert.IsTrue(SidekickParts(restored).Where(s => Region(s) == "10TORS").All(s => s.sharedMesh.name.Contains("CIVL")), "the civilian outfit from the state");
            Assert.IsTrue(SidekickParts(restored).All(s => s.sharedMaterial.name == "Starter_03"));
            AssertNoErrorsLogged();
        }

        [UnityTest]
        public IEnumerator ASavedSkinAndHairColourChoiceIsRebuiltFromTheStateInTheRealScene()
        {
            yield return null;
            SpawnHuman(("outfit", "outfit-civilian"), ("hair", "hair-human-03"), ("skin", "skin-deep"), ("haircolour", "haircolour-ginger"));
            yield return null; yield return null;
            var parts = SidekickParts(human.gameObject);
            var material = parts.SelectMany(s => s.sharedMaterials).Distinct().Single();
            var map = (Texture2D)material.GetTexture("_ColorMap");
            var skin = catalog.FindSlot("skin").options.Single(o => o.id == "skin-deep").cells.First();
            var pixel = map.GetPixels32()[skin.y * 32 + skin.x];
            Assert.AreEqual(skin.colour.r, pixel.r); Assert.AreEqual(skin.colour.g, pixel.g); Assert.AreEqual(skin.colour.b, pixel.b);
            Assert.AreEqual("Starter_01", material.name);

            var id = human.EntityId;
            state.SetAppearance(new DefinitionId("skin"), new DefinitionId("skin-pale"));
            GameObject prefab = null;
#if UNITY_EDITOR
            prefab = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(HumanBasePath);
#endif
            var position = human.transform.position;
            Object.Destroy(human.gameObject);
            yield return null;
            var restored = Village.Spawner.BindRestoredView("test-human", id, DefinitionId.None, prefab, position, Quaternion.identity, DefinitionId.None, startInactive: false);
            yield return null; yield return null;
            var paleMaterial = SidekickParts(restored).SelectMany(s => s.sharedMaterials).Distinct().Single();
            Assert.AreNotSame(material, paleMaterial, "the changed skin choice from the state, not the definition's");
            var pale = catalog.FindSlot("skin").options.Single(o => o.id == "skin-pale").cells.First();
            Assert.AreEqual(pale.colour.r, ((Texture2D)paleMaterial.GetTexture("_ColorMap")).GetPixels32()[pale.y * 32 + pale.x].r);
            AssertNoErrorsLogged();
        }

        [UnityTest]
        public IEnumerator FittedVikingPiecesAndBuildOffsetsSurviveTheRealSceneAndARestoreFromSavedState()
        {
            yield return null;
            SpawnHuman(("outfit", "outfit-tunic"), ("hair", "hair-viking-hair-02"), ("headwear", "headwear-viking-wolf-01"), ("shoulders", "shoulders-viking-fur-02"), ("gear", "gear-war-camp-bags-01"), ("build", "build-heavy"));
            yield return null; yield return null;
            AssertDressedAndFollowingTheSkeleton();

            void AssertPieces(GameObject character)
            {
                foreach (var (slot, id) in new[] { ("hair", "hair-viking-hair-02"), ("headwear", "headwear-viking-wolf-01"), ("shoulders", "shoulders-viking-fur-02") })
                {
                    var option = catalog.FindSlot(slot).Find(id);
                    var piece = character.GetComponentsInChildren<SkinnedMeshRenderer>(true).Single(s => s.GetComponentInParent<ModelAppearancePart>(true) is { } p && p.SlotId == slot);
                    Assert.IsTrue(piece.gameObject.activeInHierarchy, id);
                    Assert.IsTrue(piece.sharedMaterials.All(m => m == option.material), id + " keeps its Polygon material");
                    Assert.AreEqual(100f, piece.GetBlendShapeWeight(ModelAppearanceApplier.BlendShapeIndex(piece.sharedMesh, "defaultHeavy")), id + " follows the Heavy build");
                }

                var bag = catalog.FindSlot("gear").Find("gear-war-camp-bags-01");
                var bagPart = character.GetComponentsInChildren<ModelAppearancePart>(true).Single(p => p.SlotId == "gear");
                Assert.Greater((bagPart.transform.localPosition - bag.localPosition).magnitude, 0.02f, "the bag moved out with the Heavy build");
            }

            AssertPieces(human.gameObject);

            var id = human.EntityId;
            var position = human.transform.position;
            GameObject prefab = null;
#if UNITY_EDITOR
            prefab = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(HumanBasePath);
#endif
            Object.Destroy(human.gameObject);
            yield return null;
            var restored = Village.Spawner.BindRestoredView("test-human", id, DefinitionId.None, prefab, position, Quaternion.identity, DefinitionId.None, startInactive: false);
            yield return null; yield return null;
            human = restored.GetComponent<CharacterAgent>();
            AssertPieces(restored);
            AssertNoErrorsLogged();
        }

        [UnityTest]
        public IEnumerator AHumansBodyFacesItsDirectionOfTravelWhileWalkingAndForwardWhenIdle()
        {
            yield return null;
            SpawnHuman(("outfit", "outfit-civilian"));
            yield return null;
            var agent = human.GetComponent<NavMeshAgent>();
            if (!agent.isOnNavMesh) agent.Warp(human.transform.position);
            var forward = human.transform.forward;
            for (var i = 0; i < 40; i++) yield return null;
            var idle = Enumerable.Range(0, 20).Select(_ => HipYaw(human.gameObject)).ToList();
            Debug.Log($"[HumanFacing] idle hip yaw {idle.Average():F1} (spread {idle.Max() - idle.Min():F1})");

            // walk toward a reachable point a few metres away
            var target = Vector3.zero;
            for (var angle = 0; angle < 360 && target == Vector3.zero; angle += 45)
            {
                var wanted = human.transform.position + Quaternion.AngleAxis(angle, Vector3.up) * Vector3.forward * 8f;
                if (NavMesh.SamplePosition(wanted, out var hit, 1.5f, NavMesh.AllAreas)) target = hit.position;
            }

            Assert.AreNotEqual(Vector3.zero, target, "a reachable point to walk to");
            agent.SetDestination(target);
            var moving = new List<float>();
            var until = Time.realtimeSinceStartup + 6f;
            for (var i = 0; Time.realtimeSinceStartup < until && i < 240; i++)
            {
                if (i > 45 && agent.velocity.magnitude > 1.0f) moving.Add(HipYaw(human.gameObject));
                yield return null;
            }

            Debug.Log($"[HumanFacing] moving hip yaw {(moving.Count > 0 ? moving.Average().ToString("F1") : "n/a")} over {moving.Count} samples");
            Assert.GreaterOrEqual(moving.Count, 10, "it got up to speed");
            Assert.Less(Mathf.Abs(idle.Average()), 7f, $"stands with its body {idle.Average():F0} degrees off forward");
            Assert.Less(Mathf.Abs(moving.Average()), 7f, $"walks with its body {moving.Average():F0} degrees off its direction of travel");
        }
    }
}
