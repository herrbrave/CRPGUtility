using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using CRPG.Core;
using NUnit.Framework;
using UnityEngine;
using VikingRaid.Appearance;
using VikingRaid.Appearance.Editor;

namespace VikingRaid.Tests.EditMode
{
    /// <summary>Captures warnings and errors logged while it is alive (LogAssert only fails a test on errors; the appearance code reports content problems as warnings).</summary>
    public sealed class LogCapture : IDisposable
    {
        public readonly List<string> Messages = new();
        public LogCapture() => Application.logMessageReceived += OnLog;
        public void Dispose() => Application.logMessageReceived -= OnLog;
        private void OnLog(string condition, string stack, LogType type) { if (type != LogType.Log) Messages.Add($"{type}: {condition}"); }
    }

    /// <summary>The Sidekick goblin family: the catalog authored from the real part folders, and the applier dressing the real skeleton-only master from it.</summary>
    public sealed class GoblinAppearanceTests
    {
        private ModelAppearanceCatalog catalog;
        private GameObject root;

        [OneTimeSetUp]
        public void BuildCatalog() => catalog = GoblinCatalogAuthor.Build();

        [SetUp]
        public void Instantiate() => root = UnityEngine.Object.Instantiate(catalog.MasterPrefab);

        [TearDown]
        public void Cleanup() => UnityEngine.Object.DestroyImmediate(root);

        private static Dictionary<DefinitionId, DefinitionId> Choices(params (string slot, string option)[] pairs) =>
            pairs.ToDictionary(p => new DefinitionId(p.slot), p => new DefinitionId(p.option));

        private void Apply(params (string slot, string option)[] pairs) => ModelAppearanceApplier.Apply(root, catalog, Choices(pairs), immediate: true);

        private SkinnedMeshRenderer[] PartRenderers() => root.GetComponentsInChildren<SkinnedMeshRenderer>(true).Where(s => s.GetComponentInParent<ModelAppearancePart>(true) != null).ToArray();
        private static string Region(SkinnedMeshRenderer s) => Regex.Match(s.sharedMesh.name, @"_(\d\d[A-Z]{4})_").Groups[1].Value;
        private static string MeshSet(SkinnedMeshRenderer s) => Regex.Match(s.sharedMesh.name, @"SK_GOBL_(BASE|FIGT)_(\d\d)_").Value;

        // ------------------------------------------------------------------ catalog and master
        [Test]
        public void TheCatalogHasTwelveRowsOnFourTabs()
        {
            Assert.AreEqual(new[] { "face", "hair", "facialhair", "torso", "legs", "helm", "shoulders", "back", "belt", "joints", "look", "build" }, catalog.Slots.Select(s => s.id).ToArray());
            Assert.AreEqual(new[] { "Face", "Outfit", "Gear", "Colour" }, catalog.Tabs.ToArray());
            Assert.AreEqual(new[] { "face", "torso", "legs", "look", "build" }, catalog.Slots.Where(s => s.required).Select(s => s.id).ToArray());
            Assert.AreEqual("Goblin", catalog.Family);
            Assert.AreEqual(ModelPaletteScope.AppearanceParts, catalog.PaletteScope);
            Assert.AreEqual("head", catalog.HeadBone);
            Assert.AreEqual("Face", catalog.HeadTab);
            Assert.IsNotNull(catalog.PreviewIdleClip, "the goblin idle (run 'Viking Raid/Task 1/Author Characters' if the goblin animation profile is missing)");
        }

        [Test]
        public void EveryRowHasTheOptionsTheDesignListsAndAValidDefault()
        {
            int Count(string id) => catalog.FindSlot(id).options.Count;
            Assert.AreEqual(10, Count("face"));
            Assert.AreEqual(20, Count("hair")); Assert.AreEqual(20, Count("facialhair"));
            Assert.AreEqual(11, Count("torso")); Assert.AreEqual(11, Count("legs"));
            foreach (var id in new[] { "helm", "shoulders", "back", "belt", "joints" }) Assert.AreEqual(10, Count(id), id);
            Assert.AreEqual(10, Count("look"));
            foreach (var slot in catalog.Slots.Where(s => s.required)) Assert.IsNotNull(slot.Find(slot.defaultOptionId), slot.id + " default");
        }

        [Test]
        public void EveryOptionIsCompleteAndIdsAreUnique()
        {
            foreach (var slot in catalog.Slots)
            {
                Assert.AreEqual(slot.options.Count, slot.options.Select(o => o.id).Distinct().Count(), slot.id + " ids");
                foreach (var o in slot.options)
                {
                    Assert.IsNotEmpty(o.title, o.id);
                    switch (o.kind)
                    {
                        case ModelAppearanceKind.AttachParts:
                            Assert.IsNotEmpty(o.parts, o.id);
                            foreach (var part in o.parts) { Assert.IsNotNull(part, o.id); Assert.IsNotNull(part.GetComponentInChildren<SkinnedMeshRenderer>(true), o.id + " part is skinned"); }
                            break;
                        case ModelAppearanceKind.Palette: Assert.IsNotNull(o.material, o.id); break;
                        case ModelAppearanceKind.BlendShapes: break;
                        default: Assert.Fail($"{o.id}: unexpected kind {o.kind}"); break;
                    }
                }
            }
        }

        [Test]
        public void TheMasterIsASkeletonWithTheAvatarAndNoMeshesAndNoDynamicChains()
        {
            Assert.IsEmpty(catalog.MasterPrefab.GetComponentsInChildren<SkinnedMeshRenderer>(true), "no baked meshes");
            Assert.IsEmpty(catalog.MasterPrefab.GetComponentsInChildren<MeshRenderer>(true));
            Assert.IsEmpty(catalog.MasterPrefab.GetComponentsInChildren<Transform>(true).Where(t => ModelAppearanceApplier.IsDynamic(t.name)), "the master carries none of the generic dynamic-chain names");
            var animator = catalog.MasterPrefab.GetComponentInChildren<Animator>(true);
            Assert.IsNotNull(animator);
            Assert.IsTrue(animator.avatar != null && animator.avatar.isHuman, "a humanoid avatar");
            Assert.IsNotNull(catalog.MasterPrefab.GetComponentsInChildren<Transform>(true).FirstOrDefault(t => t.name == "head"), "the skeleton has its lower-case head bone");
        }

        // ------------------------------------------------------------------ applier
        [Test]
        public void NothingChosenDressesTheDefaultsAndLeavesNoRegionDoubled()
        {
            Apply();
            var regions = PartRenderers().Select(Region).ToList();
            CollectionAssert.AreEquivalent(new[] { "01HEAD", "05EYEL", "06EYER", "37TONG", "03EBRL", "04EBRR", "07EARL", "08EARR", "35NOSE", "36TETH",
                "10TORS", "11AUPL", "12AUPR", "13ALWL", "14ALWR", "15HNDL", "16HNDR", "17HIPS", "18LEGL", "19LEGR", "20FOTL", "21FOTR" }, regions);
            Assert.AreEqual(regions.Count, regions.Distinct().Count(), "one renderer per region");
        }

        [Test]
        public void AFaceChoiceTakesItsFaceRegionsFromThatSpecies()
        {
            Apply(("face", "face-goblin-04"));
            var face = PartRenderers().Where(s => s.GetComponentInParent<ModelAppearancePart>().SlotId == "face").ToArray();
            Assert.AreEqual(10, face.Length);
            foreach (var region in new[] { "03EBRL", "04EBRR", "07EARL", "08EARR", "35NOSE", "36TETH" })
                StringAssert.StartsWith("SK_GOBL_BASE_04_", face.Single(s => Region(s) == region).sharedMesh.name, region);
            foreach (var region in new[] { "01HEAD", "05EYEL", "06EYER", "37TONG" })
                StringAssert.StartsWith("SK_GOBL_BASE_01_", face.Single(s => Region(s) == region).sharedMesh.name, region);
        }

        [Test]
        public void OutfitPiecesFromDifferentSetsMixAndSwitchingReplacesNeverDoubles()
        {
            Apply(("torso", "torso-set-02"), ("legs", "legs-set-07"), ("helm", "helm-set-04"), ("shoulders", "shoulders-set-05"));
            string SetOf(string region) => MeshSet(PartRenderers().Single(s => Region(s) == region));
            Assert.AreEqual("SK_GOBL_FIGT_02_", SetOf("10TORS")); Assert.AreEqual("SK_GOBL_FIGT_02_", SetOf("16HNDR"));
            Assert.AreEqual("SK_GOBL_FIGT_07_", SetOf("18LEGL")); Assert.AreEqual("SK_GOBL_FIGT_07_", SetOf("21FOTR"));
            Assert.AreEqual("SK_GOBL_FIGT_04_", SetOf("22AHED")); Assert.AreEqual("SK_GOBL_FIGT_05_", SetOf("29ASHL"));

            Apply(("torso", "torso-set-09"), ("legs", "legs-set-07"), ("helm", "helm-set-04"));   // torso changes, shoulders go away
            var regions = PartRenderers().Select(Region).ToList();
            Assert.AreEqual(regions.Count, regions.Distinct().Count(), "no region doubled after a switch");
            Assert.AreEqual("SK_GOBL_FIGT_09_", SetOf("10TORS"));
            Assert.IsFalse(regions.Contains("29ASHL"), "None removes a set");
            Apply(("torso", "torso-bare"));
            Assert.AreEqual("SK_GOBL_BASE_01_", SetOf("10TORS"));
            Assert.IsFalse(regions.Contains("22AHED") && PartRenderers().Any(s => Region(s) == "22AHED"), "the helm went with the choices");
        }

        [Test]
        public void EveryBoundBoneMatchesItsBindPoseAndBelongsToTheMastersHierarchy()
        {
            Apply(("face", "face-goblin-03"), ("hair", "hair-goblin-04"), ("facialhair", "facialhair-set-02"), ("torso", "torso-set-02"), ("legs", "legs-set-07"),
                ("helm", "helm-set-04"), ("shoulders", "shoulders-set-05"), ("back", "back-set-03"), ("belt", "belt-set-06"), ("joints", "joints-set-08"));
            var rig = root.GetComponentInChildren<Animator>(true).transform;
            var checkedBones = 0;
            foreach (var smr in PartRenderers())
            {
                var bindposes = smr.sharedMesh.bindposes;
                Assert.AreEqual(bindposes.Length, smr.bones.Length, smr.name);
                var weighted = new HashSet<int>(smr.sharedMesh.GetAllBoneWeights().Where(w => w.weight > 0.001f).Select(w => w.boneIndex));   // socket bones like backAttach carry no vertices
                for (var i = 0; i < smr.bones.Length; i++)
                {
                    var bone = smr.bones[i];
                    if (bone == null || !weighted.Contains(i)) continue;
                    Assert.IsTrue(bone.IsChildOf(rig), $"{smr.sharedMesh.name}: bone {bone.name} is outside the master's rig");
                    // at rest a vertex stays where the renderer puts it: boneToWorld * bindpose == rendererToWorld
                    var skin = bone.localToWorldMatrix * bindposes[i];
                    var renderer = smr.transform.localToWorldMatrix;
                    for (var e = 0; e < 16; e++)
                        if (Mathf.Abs(skin[e] - renderer[e]) > 0.02f) Assert.Fail($"{smr.sharedMesh.name}: bone {bone.name} is not at its bind pose (element {e}: {skin[e]:F3} vs {renderer[e]:F3})");
                    checkedBones++;
                }
            }

            Assert.Greater(checkedBones, 100, "a dressed goblin has many weighted bone bindings");
            Assert.IsNotEmpty(root.GetComponentsInChildren<Transform>(true).Where(t => t.name.EndsWith("_bind")), "the outfit sets rest their attachment sockets differently from the master: those parts brought bind bones");
            Assert.IsNotEmpty(root.GetComponentsInChildren<Transform>(true).Where(t => ModelAppearanceApplier.IsDynamic(t.name)), "the parts brought their dynamic chains");
        }

        [Test]
        public void PartsAttachedToAnAlreadyPosedSkeletonStillFollowTheBones()
        {
            // the preview and a live character are dressed while animated: the rest poses must come from the master asset, not from the posed transforms
            void Pose(float time) { catalog.PreviewIdleClip.SampleAnimation(root, time); root.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity); }
            Apply();
            Pose(0.25f);
            var handBone = root.GetComponentsInChildren<Transform>(true).First(t => t.name == "hand_l");
            var bindHand = handBone.position;
            Apply(("torso", "torso-set-02"), ("shoulders", "shoulders-set-05"), ("helm", "helm-set-04"));

            Vector3 BakedCentre(string region)
            {
                var smr = PartRenderers().Single(s => Region(s) == region);
                var baked = new Mesh();
                smr.BakeMesh(baked);
                var centre = smr.transform.TransformPoint(baked.bounds.center);
                UnityEngine.Object.DestroyImmediate(baked);
                return centre;
            }

            var hand = PartRenderers().Single(s => Region(s) == "15HNDL");
            var restCentre = hand.transform.TransformPoint(hand.sharedMesh.bounds.center);
            Assert.Less(Vector3.Distance(BakedCentre("15HNDL"), handBone.position), 0.2f, "the hand follows its posed bone");
            Assert.Greater(Vector3.Distance(BakedCentre("15HNDL"), restCentre), 0.5f, "and is not left in the bind pose");

            var shoulderBefore = BakedCentre("29ASHL");
            Pose(1.2f);
            Assert.Greater(Vector3.Distance(BakedCentre("29ASHL"), shoulderBefore), 0.01f, "a pad on a bind bone moves with the skeleton");
            Assert.Less(Vector3.Distance(BakedCentre("15HNDL"), handBone.position), 0.2f, "still following after the pose changes");
        }

        [Test]
        public void ADynamicChainBelongsToItsPartAndGoesWhenThePartDoes()
        {
            string WithChain(int skip) => catalog.FindSlot("hair").options.Where(o => o.parts[0].GetComponentInChildren<SkinnedMeshRenderer>(true).bones.Any(b => b != null && ModelAppearanceApplier.IsDynamic(b.name))).Skip(skip).First().id;
            var hairA = WithChain(0); var hairB = WithChain(1);
            Apply(("face", "face-goblin-03"), ("hair", hairA));
            var chains = root.GetComponentsInChildren<ModelAppearancePart>(true).Where(p => ModelAppearanceApplier.IsDynamic(p.name)).ToList();
            Assert.IsNotEmpty(chains, "the hair's dynamic chain is marked as part of the hair slot");
            Assert.IsTrue(chains.All(p => p.SlotId == "hair" || p.SlotId == "face"));

            Apply(("face", "face-goblin-03"), ("hair", hairB));
            Assert.IsEmpty(root.GetComponentsInChildren<ModelAppearancePart>(true).Where(p => p.OptionId == hairA), "nothing of the replaced hair is left, chains included");
            Apply(("face", "face-goblin-03"));
            Assert.IsEmpty(root.GetComponentsInChildren<ModelAppearancePart>(true).Where(p => p.SlotId == "hair"), "None removes the hair and its chains");
            Assert.IsEmpty(root.GetComponentsInChildren<Transform>(true).Where(t => ModelAppearanceApplier.IsDynamic(t.name) && t.GetComponentInParent<ModelAppearancePart>(true) == null), "no orphan dynamic bone");
        }

        [Test]
        public void AMasterThatStillCarriesDynamicBonesIsNeverMatchedToThemByName()
        {
            // Synty's own prefab keeps hair_dyn_01_0N_k under its skeleton. Dressed on it, a part that also has a bone of that name must bring its OWN chain, not bind to the master's
            var raw = (GameObject)UnityEditor.PrefabUtility.InstantiatePrefab(UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Synty/SidekickCharacters/Characters/GoblinSpecies/GoblinSpecies_01/GoblinSpecies_01.prefab"));
            try
            {
                UnityEditor.PrefabUtility.UnpackPrefabInstance(raw, UnityEditor.PrefabUnpackMode.Completely, UnityEditor.InteractionMode.AutomatedAction);
                foreach (var combined in raw.GetComponentsInChildren<SkinnedMeshRenderer>(true).ToList()) UnityEngine.Object.DestroyImmediate(combined.gameObject);
                var masterDynamics = raw.GetComponentsInChildren<Transform>(true).Where(t => ModelAppearanceApplier.IsDynamic(t.name)).ToList();
                Assert.IsNotEmpty(masterDynamics, "this master has dynamic bones of its own (the premise)");

                var withChain = catalog.FindSlot("hair").options.First(o => o.parts[0].GetComponentInChildren<SkinnedMeshRenderer>(true).bones.Any(b => b != null && masterDynamics.Any(m => m.name == b.name)));
                ModelAppearanceApplier.Apply(raw, catalog, Choices(("hair", withChain.id)), immediate: true);
                var hair = raw.GetComponentsInChildren<SkinnedMeshRenderer>(true).Single(s => s.GetComponentInParent<ModelAppearancePart>(true) != null && s.GetComponentInParent<ModelAppearancePart>(true).SlotId == "hair");
                foreach (var bone in hair.bones.Where(b => b != null && masterDynamics.Contains(b))) Assert.Fail($"the hair is bound to the MASTER's {bone.name} instead of its own");
                foreach (var bone in hair.bones.Where(b => b != null && ModelAppearanceApplier.IsDynamic(b.name))) Assert.IsNotNull(bone.GetComponentInParent<ModelAppearancePart>(true), bone.name + " is the part's own, marked chain");
            }
            finally { UnityEngine.Object.DestroyImmediate(raw); }
        }

        [Test]
        public void ALookRepaintsEveryPartButNotAnObjectTheApplierDidNotCreate()
        {
            var weapon = GameObject.CreatePrimitive(PrimitiveType.Cube);
            weapon.transform.SetParent(root.transform, false);
            var weaponMaterial = weapon.GetComponent<Renderer>().sharedMaterial;
            var look = catalog.FindSlot("look").options.First(o => o.id == "look-species-3");
            Apply(("look", look.id), ("helm", "helm-set-02"), ("belt", "belt-set-02"));
            Assert.IsTrue(PartRenderers().All(s => s.sharedMaterials.All(m => m == look.material)), "every part, late-attached ones included");
            Assert.AreSame(weaponMaterial, weapon.GetComponent<Renderer>().sharedMaterial, "equipment is left alone");
            var other = catalog.FindSlot("look").options.First(o => o.id == "look-fighter-4");
            Apply(("look", other.id), ("helm", "helm-set-02"), ("belt", "belt-set-02"));
            Assert.IsTrue(PartRenderers().All(s => s.sharedMaterials.All(m => m == other.material)));
        }

        [Test]
        public void ABuildSetsItsBlendShapesByNameOnEveryPartThatHasThemAndSwitchingLeavesNothingBehind()
        {
            Apply(("build", "build-buff"), ("torso", "torso-set-02"), ("helm", "helm-set-02"));
            float Weight(string region, string shape)
            {
                var smr = PartRenderers().Single(s => Region(s) == region);
                var index = ModelAppearanceApplier.BlendShapeIndex(smr.sharedMesh, shape);
                Assert.GreaterOrEqual(index, 0, $"{region} has {shape}");
                return smr.GetBlendShapeWeight(index);
            }

            Assert.AreEqual(100f, Weight("10TORS", "defaultBuff")); Assert.AreEqual(100f, Weight("01HEAD", "defaultBuff"));
            Assert.AreEqual(0f, Weight("10TORS", "defaultSkinny"));
            Apply(("build", "build-skinny"), ("torso", "torso-set-02"), ("helm", "helm-set-02"));
            Assert.AreEqual(0f, Weight("10TORS", "defaultBuff"), "the previous build left nothing behind");
            Assert.AreEqual(100f, Weight("10TORS", "defaultSkinny"));
            Apply(("build", "build-default"), ("torso", "torso-set-02"), ("helm", "helm-set-02"));
            Assert.AreEqual(0f, Weight("10TORS", "defaultSkinny"));
            // a part added after the build was chosen gets it too
            Apply(("build", "build-heavy"));
            Apply(("build", "build-heavy"), ("belt", "belt-set-03"));
            foreach (var smr in PartRenderers())
            {
                var i = ModelAppearanceApplier.BlendShapeIndex(smr.sharedMesh, "defaultHeavy");
                if (i >= 0) Assert.AreEqual(100f, smr.GetBlendShapeWeight(i), smr.sharedMesh.name);
            }
        }

        [Test]
        public void ApplyingTheSameChoicesTwiceKeepsTheSameObjectsInsteadOfCloningAgain()
        {
            var choices = new[] { ("face", "face-goblin-02"), ("torso", "torso-set-03"), ("hair", "hair-set-06") };
            Apply(choices);
            var first = PartRenderers();
            Apply(choices);
            CollectionAssert.AreEquivalent(first, PartRenderers());   // the very same objects
        }

        [Test]
        public void ClonesLoseAnimatorsAndCollidersAndTakeTheRootsLayer()
        {
            root.layer = 7;
            Apply(("helm", "helm-set-02"), ("back", "back-set-02"));
            Assert.IsNotEmpty(PartRenderers());
            Assert.AreEqual(1, root.GetComponentsInChildren<Animator>(true).Length, "only the master's own Animator");
            Assert.IsEmpty(root.GetComponentsInChildren<Collider>(true));
            Assert.IsTrue(PartRenderers().All(s => s.gameObject.layer == 7));
        }

        [Test]
        public void EveryOptionOfEveryRowAppliesWithoutAWarning()
        {
            using var log = new LogCapture();
            foreach (var slot in catalog.Slots)
                foreach (var option in slot.options)
                {
                    Apply((slot.id, option.id));
                    if (option.kind == ModelAppearanceKind.AttachParts) Assert.IsNotEmpty(PartRenderers().Where(s => s.GetComponentInParent<ModelAppearancePart>().OptionId == option.id), option.id);
                }

            Assert.IsEmpty(log.Messages, string.Join("\n", log.Messages.Take(10)));
        }

        [Test]
        public void TheRandomizerRollsEveryRowOfTheGoblinCatalog()
        {
            var roll = ModelAppearanceRandomizer.Roll(catalog, 21);
            foreach (var slot in catalog.Slots.Where(s => s.required)) Assert.IsNotNull(slot.Find(roll[new DefinitionId(slot.id)].Value), slot.id);
            ModelAppearanceApplier.Apply(root, catalog, roll, immediate: true);
            Assert.IsNotEmpty(PartRenderers());
        }

        [Test]
        public void TheVikingCatalogKeepsItsOwnFamilySettings()
        {
            var viking = ModelAppearanceCatalogAuthor.LoadOrBuild();
            Assert.AreEqual("Viking", viking.Family);
            Assert.AreEqual(ModelPaletteScope.FamilyMaterials, viking.PaletteScope);
            Assert.AreEqual("Head", viking.HeadBone);
            Assert.AreEqual("Head", viking.HeadTab);
        }
    }
}
