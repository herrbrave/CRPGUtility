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
    /// <summary>The Sidekick human family: the catalog authored from the Starter outfits, our Blender tunic and the Polygon pieces, and the applier dressing the real skeleton-only master from it.</summary>
    public sealed class HumanAppearanceTests
    {
        private static readonly string[] BodyRegions = { "10TORS", "11AUPL", "12AUPR", "13ALWL", "14ALWR", "15HNDL", "16HNDR", "17HIPS", "18LEGL", "19LEGR", "20FOTL", "21FOTR" };
        private ModelAppearanceCatalog catalog;
        private GameObject root;

        [OneTimeSetUp]
        public void BuildCatalog() => catalog = HumanCatalogAuthor.Build();

        [SetUp]
        public void Instantiate() => root = UnityEngine.Object.Instantiate(catalog.MasterPrefab);

        [TearDown]
        public void Cleanup() => UnityEngine.Object.DestroyImmediate(root);

        private static Dictionary<DefinitionId, DefinitionId> Choices(params (string slot, string option)[] pairs) =>
            pairs.ToDictionary(p => new DefinitionId(p.slot), p => new DefinitionId(p.option));

        private void Apply(params (string slot, string option)[] pairs) => ModelAppearanceApplier.Apply(root, catalog, Choices(pairs), immediate: true);

        private ModelAppearancePart[] Marks() => root.GetComponentsInChildren<ModelAppearancePart>(true).Where(p => p.GetComponent<SkinnedMeshRenderer>() != null || p.GetComponentInChildren<Renderer>(true) != null).ToArray();

        /// <summary>The renderers that come from PART SETS (Sidekick meshes), not the rigid Polygon pieces.</summary>
        private SkinnedMeshRenderer[] SidekickParts() => root.GetComponentsInChildren<SkinnedMeshRenderer>(true)
            .Where(s => s.GetComponentInParent<ModelAppearancePart>(true) is { } part && catalog.FindSlot(part.SlotId)?.Find(part.OptionId)?.kind == ModelAppearanceKind.AttachParts).ToArray();

        private static string Region(SkinnedMeshRenderer s) => Regex.Match(s.sharedMesh.name, @"_(\d\d[A-Z]{4})_").Groups[1].Value;
        private Transform Bone(string name) => root.GetComponentsInChildren<Transform>(true).First(t => t.name == name && t.GetComponentInParent<ModelAppearancePart>(true) == null);

        // ------------------------------------------------------------------ catalog and master
        [Test]
        public void TheCatalogHasThirteenRowsOnFourTabs()
        {
            Assert.AreEqual(new[] { "face", "hair", "facialhair", "outfit", "shoulders", "headwear", "faceware", "cloak", "gear", "look", "build", "skin", "haircolour" }, catalog.Slots.Select(s => s.id).ToArray());
            Assert.AreEqual(new[] { "Face", "Outfit", "Gear", "Colour" }, catalog.Tabs.ToArray());
            Assert.AreEqual(new[] { "face", "outfit", "look", "build" }, catalog.Slots.Where(s => s.required).Select(s => s.id).ToArray());
            Assert.AreEqual("Human", catalog.Family); Assert.AreEqual(ModelPaletteScope.AppearanceParts, catalog.PaletteScope);
            Assert.AreEqual("head", catalog.HeadBone); Assert.AreEqual("Face", catalog.HeadTab);
            Assert.IsNotNull(catalog.PreviewIdleClip, "the hero idle");
        }

        [Test]
        public void EveryRowHasItsOptionsAndAValidDefaultAndNoOptionIsRigged()
        {
            int Count(string id) => catalog.FindSlot(id).options.Count;
            Assert.AreEqual(10, Count("face")); Assert.AreEqual(4, Count("outfit")); Assert.AreEqual(4, Count("look")); Assert.AreEqual(4, Count("build"));
            Assert.AreEqual(2 + 4 + 1, Count("shoulders"), "knight and civilian pads, four Viking furs, the War Camp collar");
            Assert.AreEqual(10 + 8 + 4 + 7 + 9 + 2, Count("hair"));
            Assert.AreEqual(10 + 5 + 2 + 2 + 2, Count("facialhair"));
            foreach (var slot in catalog.Slots.Where(s => s.required)) Assert.IsNotNull(slot.Find(slot.defaultOptionId), slot.id + " default");
            Assert.IsEmpty(catalog.Slots.SelectMany(s => s.options).Where(o => o.kind == ModelAppearanceKind.AttachRigged), "a rigged piece cannot be bound on a skeleton-only master");
        }

        [Test]
        public void EveryOptionIsCompleteAndIdsAreUniqueWithinARow()
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
                            foreach (var part in o.parts) Assert.IsNotNull(part.GetComponentInChildren<SkinnedMeshRenderer>(true), o.id + " part is skinned");
                            break;
                        case ModelAppearanceKind.AttachStatic: Assert.IsNotNull(o.prefab, o.id); Assert.IsNull(o.prefab.GetComponentInChildren<SkinnedMeshRenderer>(true), o.id + " is rigid"); Assert.IsNotEmpty(o.bone, o.id); break;
                        case ModelAppearanceKind.AttachOwnRig: Assert.IsNotNull(o.prefab, o.id); Assert.IsTrue(o.id.Contains("cape"), o.id + ": only capes carry their own rig"); break;
                        case ModelAppearanceKind.AttachPartsOwnMaterial: Assert.AreEqual(1, o.parts.Count, o.id); Assert.IsNotNull(o.material, o.id); break;
                        case ModelAppearanceKind.Palette: Assert.IsNotNull(o.material, o.id); break;
                        case ModelAppearanceKind.BlendShapes: break;
                        case ModelAppearanceKind.ColourCells: Assert.IsNotEmpty(o.cells, o.id); Assert.Greater(o.swatch.a, 0, o.id + " has a swatch"); break;
                        default: Assert.Fail($"{o.id}: unexpected kind {o.kind}"); break;
                    }
                }
            }
        }

        [Test]
        public void TheMasterIsASkeletonWithTheAvatarAndNoMeshesAndNoDynamicChains()
        {
            Assert.IsEmpty(catalog.MasterPrefab.GetComponentsInChildren<SkinnedMeshRenderer>(true));
            Assert.IsEmpty(catalog.MasterPrefab.GetComponentsInChildren<Transform>(true).Where(t => ModelAppearanceApplier.IsDynamic(t.name)));
            var animator = catalog.MasterPrefab.GetComponentInChildren<Animator>(true);
            Assert.IsTrue(animator != null && animator.avatar != null && animator.avatar.isHuman);
            Assert.AreEqual("Assets/Appearance/Human/HumanMaster.prefab", UnityEditor.AssetDatabase.GetAssetPath(catalog.MasterPrefab), "the master is in the UNIGNORED folder (derived geometry is not)");
        }

        [Test]
        public void PolygonPiecesReuseTheVikingIdsSoSwitchingFamiliesKeepsThem()
        {
            var viking = ModelAppearanceCatalogAuthor.LoadOrBuild();
            foreach (var slotId in new[] { "headwear", "cloak", "gear" })
            {
                var shared = catalog.FindSlot(slotId).options.Select(o => o.id).Intersect(viking.FindSlot(slotId).options.Select(o => o.id)).ToList();
                Assert.IsNotEmpty(shared, slotId + " shares ids with the Viking catalog");
            }

            CollectionAssert.Contains(catalog.FindSlot("headwear").options.Select(o => o.id), "headwear-viking-helmet-horns-02");
            CollectionAssert.Contains(catalog.FindSlot("hair").options.Select(o => o.id), "hair-viking-hair-06");
        }

        [Test]
        public void SidekickFacialHairAndThePolygonBeardsShareOneRow()
        {
            var ids = catalog.FindSlot("facialhair").options.Select(o => o.id).ToList();
            Assert.IsTrue(ids.Any(i => i.StartsWith("facialhair-human-")) && ids.Any(i => i.StartsWith("facialhair-viking-beard-")), "both families in one row");
            Assert.IsNull(catalog.FindSlot("beard"), "no separate beard row");
        }

        // ------------------------------------------------------------------ outfits
        [Test]
        public void EveryOutfitGivesExactlyOneRendererPerBodyRegionAndSwitchingReplacesNeverDoubles()
        {
            foreach (var outfit in catalog.FindSlot("outfit").options)
            {
                Apply(("outfit", outfit.id));
                var body = SidekickParts().Where(s => s.GetComponentInParent<ModelAppearancePart>().SlotId == "outfit").Select(Region).ToList();
                CollectionAssert.AreEquivalent(BodyRegions, body, outfit.id);
            }

            Apply(("outfit", "outfit-knight")); Apply(("outfit", "outfit-bare"));
            var after = SidekickParts().Where(s => s.GetComponentInParent<ModelAppearancePart>().SlotId == "outfit").Select(s => s.sharedMesh.name).ToList();
            Assert.IsTrue(after.All(n => n.Contains("BASE_01")), "back to the bare body, nothing of the knight left");
        }

        [Test]
        public void NothingChosenDressesTheBareDefaults()
        {
            Apply();
            var regions = SidekickParts().Select(Region).ToList();
            CollectionAssert.IsSupersetOf(regions, BodyRegions.Concat(new[] { "01HEAD", "05EYEL", "06EYER", "37TONG", "03EBRL", "04EBRR", "07EARL", "08EARR", "35NOSE", "36TETH" }));
            Assert.AreEqual(regions.Count, regions.Distinct().Count(), "one renderer per region");
            Assert.IsEmpty(Marks().Where(p => p.SlotId == "hair" || p.SlotId == "facialhair"), "clean-shaven and bald by default: no baked beard, no female-beard bug");
        }

        [Test]
        public void EveryBuildResolvesTheShapeNamesOnEveryOutfitAndLeavesNoResidue()
        {
            foreach (var outfit in catalog.FindSlot("outfit").options)
                foreach (var build in catalog.FindSlot("build").options.Where(b => b.blendShapes.Count > 0))
                {
                    Apply(("outfit", outfit.id), ("build", build.id));
                    var shape = build.blendShapes[0].shape;
                    var bodyParts = SidekickParts().Where(s => s.GetComponentInParent<ModelAppearancePart>().SlotId == "outfit").ToArray();
                    foreach (var smr in bodyParts)
                    {
                        var index = ModelAppearanceApplier.BlendShapeIndex(smr.sharedMesh, shape);
                        Assert.GreaterOrEqual(index, 0, $"{outfit.id} {smr.sharedMesh.name} has {shape}");
                        Assert.AreEqual(100f, smr.GetBlendShapeWeight(index), $"{outfit.id} {build.id} {smr.sharedMesh.name}");
                        foreach (var other in catalog.FindSlot("build").options.SelectMany(b => b.blendShapes).Select(b => b.shape).Distinct().Where(n => n != shape))
                        {
                            var j = ModelAppearanceApplier.BlendShapeIndex(smr.sharedMesh, other);
                            if (j >= 0) Assert.AreEqual(0f, smr.GetBlendShapeWeight(j), $"{outfit.id} {build.id}: no residue of {other}");
                        }
                    }
                }
        }

        [Test]
        public void TheTunicsSleeveEndMeetsTheBareHandWithoutAGap()
        {
            Apply(("outfit", "outfit-tunic"));
            Vector3[] Vertices(string region) => SidekickParts().Single(s => Region(s) == region).sharedMesh.vertices;
            foreach (var (sleeve, hand) in new[] { ("13ALWL", "15HNDL"), ("14ALWR", "16HNDR") })
            {
                var sleeveVertices = Vertices(sleeve); var handVertices = Vertices(hand);
                var outermost = sleeveVertices.Max(v => Mathf.Abs(v.x));
                var ring = sleeveVertices.Where(v => Mathf.Abs(v.x) > outermost - 0.002f).ToArray();
                var worst = ring.Max(v => handVertices.Min(h => Vector3.Distance(h, v)));
                Assert.Less(worst, 0.025f, $"{sleeve}: the sleeve's last ring sits within 2.5 cm of the hand ({worst * 100f:F1} cm)");
            }
        }

        // ------------------------------------------------------------------ skinned Viking pieces fitted to the human (Phase B)
        private static readonly string[] FittedHairs = { "hair-viking-hair-01", "hair-viking-hair-01-alt", "hair-viking-hair-02", "hair-viking-hair-03", "hair-viking-hair-03-alt", "hair-viking-hair-04", "hair-viking-hair-04-alt", "hair-viking-hair-05" };
        private static readonly string[] FittedPelts = { "headwear-viking-wolf-01", "headwear-viking-bear-01" };
        private static readonly string[] FittedFurs = { "shoulders-viking-fur-01", "shoulders-viking-fur-02", "shoulders-viking-fur-03", "shoulders-viking-fur-04" };

        private IEnumerable<ModelAppearanceOption> FittedOptions() =>
            catalog.Slots.SelectMany(s => s.options).Where(o => o.kind == ModelAppearanceKind.AttachPartsOwnMaterial);

        [Test]
        public void TheSkinnedVikingHairsPeltsAndFursAreInTheCatalogWithTheVikingIds()
        {
            CollectionAssert.AreEquivalent(FittedHairs.Concat(FittedPelts).Concat(FittedFurs), FittedOptions().Select(o => o.id).ToArray());
            var viking = ModelAppearanceCatalogAuthor.LoadOrBuild();
            foreach (var (slot, ids) in new[] { ("hair", FittedHairs), ("headwear", FittedPelts), ("shoulders", FittedFurs) })
                foreach (var id in ids)
                {
                    Assert.IsNotNull(catalog.FindSlot(slot).Find(id), id + " is in the human row");
                    Assert.IsNotNull(viking.FindSlot(slot).Find(id), id + " is also the Viking catalog's id, so a family switch keeps it");
                }

            Assert.IsNull(catalog.FindSlot("gear").Find("gear-war-camp-collar-01"), "the collar is a shoulders piece, as in the Viking catalog");
            Assert.IsNotNull(catalog.FindSlot("shoulders").Find("shoulders-war-camp-collar-01"));
        }

        [Test]
        public void EveryFittedPieceResolvesToRealAssetsAndOnlyReferencesMasterBonesWithValidWeights()
        {
            var master = catalog.MasterPrefab.GetComponentsInChildren<Transform>(true).Select(t => t.name).ToHashSet();
            foreach (var option in FittedOptions())
            {
                Assert.AreEqual(1, option.parts.Count, option.id); Assert.IsNotNull(option.parts[0], option.id + " part prefab resolves"); Assert.IsNotNull(option.material, option.id + " material");
                var smr = option.parts[0].GetComponentInChildren<SkinnedMeshRenderer>(true);
                Assert.IsNotNull(smr, option.id); Assert.IsNotNull(smr.sharedMesh, option.id + " mesh resolves");
                var mesh = smr.sharedMesh; var weights = mesh.boneWeights;
                Assert.Greater(mesh.vertexCount, 150, option.id + " is a real mesh"); Assert.Greater(mesh.triangles.Length, 300, option.id + " has triangles");
                Assert.AreEqual(mesh.vertexCount, weights.Length, option.id);
                Assert.AreEqual(smr.bones.Length, mesh.bindposes.Length, option.id);
                Assert.IsTrue(smr.bones.All(b => b != null && master.Contains(b.name)), option.id + ": every bone is a master bone");
                foreach (var w in weights)
                {
                    Assert.AreEqual(1f, w.weight0 + w.weight1 + w.weight2 + w.weight3, 0.01f, option.id + " weights sum to 1");
                    Assert.Less(Mathf.Max(w.boneIndex0, w.boneIndex1, w.boneIndex2, w.boneIndex3), smr.bones.Length, option.id);
                }
            }
        }

        [Test]
        public void TheBoneMapSendsPolygonBonesToTheirSidekickNamesAndFingersToTheHand()
        {
            var polygon = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(ModelAppearanceCatalogAuthor.MasterPath).GetComponentsInChildren<Transform>(true);
            Transform Bone(string name) => polygon.First(t => t.name == name);
            var master = catalog.MasterPrefab.GetComponentsInChildren<Transform>(true).Select(t => t.name).ToHashSet();
            foreach (var (from, to) in new[] { ("Hips", "pelvis"), ("Spine_03", "spine_03"), ("Neck", "neck_01"), ("Head", "head"), ("Shoulder_L", "upperarm_l"), ("Elbow_R", "lowerarm_r"), ("Hand_L", "hand_l"), ("Clavicle_R", "clavicle_r") })
                Assert.AreEqual(to, PolygonPieceFitter.TargetBone(Bone(from)), from);
            Assert.AreEqual("hand_l", PolygonPieceFitter.TargetBone(Bone("IndexFinger_02_L")), "a finger resolves to its nearest mapped ancestor");
            foreach (var bone in polygon.Where(t => t.GetComponent<SkinnedMeshRenderer>() == null && t.GetComponentInParent<Animator>() != null))
            {
                var target = PolygonPieceFitter.TargetBone(bone);
                Assert.IsTrue(target == null || master.Contains(target), bone.name + " -> " + target);
            }
        }

        [Test]
        public void PlacementIsTheRigidMountFormulaAboutOneAnchor()
        {
            var placed = PolygonPieceFitter.Place(new Vector3(0.1f, 1.8f, 0.2f), new Vector3(0f, 1.6f, 0f), new Vector3(0.01f, 1.5f, -0.02f), 0.935f);
            Assert.AreEqual(0.01f + 0.935f * 0.1f, placed.x, 1e-5f); Assert.AreEqual(1.5f + 0.935f * 0.2f, placed.y, 1e-5f); Assert.AreEqual(-0.02f + 0.935f * 0.2f, placed.z, 1e-5f);
        }

        [Test]
        public void EveryFittedPieceAppliesCleanlyWithItsOwnMaterialAndNoBindBones()
        {
            using var log = new LogCapture();
            foreach (var option in FittedOptions())
            {
                var slot = catalog.Slots.First(s => s.options.Contains(option));
                Apply((slot.id, option.id));
                var marks = Marks().Where(p => p.SlotId == slot.id).ToArray();
                Assert.IsNotEmpty(marks, option.id);
                foreach (var smr in root.GetComponentsInChildren<SkinnedMeshRenderer>(true).Where(s => s.GetComponentInParent<ModelAppearancePart>(true) is { } p && p.SlotId == slot.id))
                    Assert.IsTrue(smr.sharedMaterials.All(m => m == option.material), option.id + " keeps its Polygon material");
                Assert.IsEmpty(root.GetComponentsInChildren<Transform>(true).Where(t => t.name.EndsWith("_bind")), option.id + ": the bind poses are the master's rest, so no bind bones");
            }

            Assert.IsEmpty(log.Messages, string.Join("\n", log.Messages.Take(8)));
        }

        [Test]
        public void AFittedPieceIsNotRepaintedByALookOrASkinOrHairColourChoice()
        {
            Apply(("look", "look-starter-2"), ("skin", "skin-dark"), ("haircolour", "haircolour-white"), ("hair", "hair-viking-hair-01"), ("headwear", "headwear-viking-wolf-01"), ("shoulders", "shoulders-viking-fur-02"));
            var tinted = PartMaterial();
            foreach (var slot in new[] { "hair", "headwear", "shoulders" })
            {
                var renderers = root.GetComponentsInChildren<SkinnedMeshRenderer>(true).Where(s => s.GetComponentInParent<ModelAppearancePart>(true) is { } p && p.SlotId == slot).ToArray();
                Assert.IsNotEmpty(renderers, slot);
                Assert.IsTrue(renderers.All(s => s.sharedMaterials.All(m => m != tinted && m == catalog.FindSlot(slot).Find(s.GetComponentInParent<ModelAppearancePart>(true).OptionId).material)), slot + " keeps the Polygon atlas");
            }
        }

        [Test]
        public void SwitchingAFittedHairReplacesItAndNoneRemovesIt()
        {
            Apply(("hair", "hair-viking-hair-01"));
            Apply(("hair", "hair-viking-hair-02"));
            Assert.IsTrue(Marks().Where(p => p.SlotId == "hair").All(p => p.OptionId == "hair-viking-hair-02"));
            Assert.AreEqual(1, root.GetComponentsInChildren<SkinnedMeshRenderer>(true).Count(s => s.GetComponentInParent<ModelAppearancePart>(true) is { SlotId: "hair" }));
            Apply();
            Assert.IsEmpty(Marks().Where(p => p.SlotId == "hair"));
        }

        [Test]
        public void FittedHairAndPeltsSitOnTheHeadAndFursOnTheShouldersAtRest()
        {
            var head = Bone("head").position; var upperL = Bone("upperarm_l").position; var upperR = Bone("upperarm_r").position;
            foreach (var id in FittedHairs.Concat(FittedPelts))
            {
                var slot = id.StartsWith("hair") ? "hair" : "headwear";
                Apply((slot, id));
                var bounds = root.GetComponentsInChildren<SkinnedMeshRenderer>(true).Single(s => s.GetComponentInParent<ModelAppearancePart>(true) is { } p && p.SlotId == slot).sharedMesh.bounds;
                Assert.Less(Vector2.Distance(new Vector2(bounds.center.x, bounds.center.z), new Vector2(head.x, head.z)), 0.12f, id + " is centred over the head");
                Assert.Greater(bounds.max.y, head.y + 0.04f, id + " reaches above the head bone"); Assert.Less(bounds.min.y, head.y + 0.05f, id);
            }

            foreach (var id in FittedFurs)
            {
                Apply(("shoulders", id));
                var bounds = root.GetComponentsInChildren<SkinnedMeshRenderer>(true).Single(s => s.GetComponentInParent<ModelAppearancePart>(true) is { SlotId: "shoulders" }).sharedMesh.bounds;
                var middle = (upperL + upperR) / 2f;
                Assert.Less(Mathf.Abs(bounds.center.x - middle.x), 0.05f, id + " is centred between the shoulders");
                Assert.Less(Mathf.Abs(bounds.center.y - middle.y), 0.18f, id + " sits at shoulder height");
            }
        }

        [Test]
        public void AFittedHairFollowsTheHeadWhenTheIdleIsSampled()
        {
            Apply(("hair", "hair-viking-hair-05"));
            var smr = root.GetComponentsInChildren<SkinnedMeshRenderer>(true).Single(s => s.GetComponentInParent<ModelAppearancePart>(true) is { SlotId: "hair" });
            var head = Bone("head");
            Vector3 HeadLocalCentre()
            {
                // linear blend skinning by hand (BakeMesh in the editor does not pose a part reliably): world = sum of weight * bone-to-world * bind pose * vertex
                var mesh = smr.sharedMesh; var vertices = mesh.vertices; var weights = mesh.boneWeights; var bindposes = mesh.bindposes; var bones = smr.bones;
                var min = new Vector3(float.MaxValue, float.MaxValue, float.MaxValue); var max = -min;
                for (var i = 0; i < vertices.Length; i++)
                {
                    var w = weights[i];
                    Vector3 Part(int bone, float weight) => weight <= 0f ? Vector3.zero : weight * (bones[bone].localToWorldMatrix * bindposes[bone]).MultiplyPoint3x4(vertices[i]);
                    var p = Part(w.boneIndex0, w.weight0) + Part(w.boneIndex1, w.weight1) + Part(w.boneIndex2, w.weight2) + Part(w.boneIndex3, w.weight3);
                    min = Vector3.Min(min, p); max = Vector3.Max(max, p);
                }

                return head.InverseTransformPoint((min + max) / 2f);
            }

            var atRest = HeadLocalCentre();
            catalog.PreviewIdleClip.SampleAnimation(root, 0.25f);
            var posed = HeadLocalCentre();
            Assert.Less(Vector3.Distance(atRest, posed), 0.08f, $"the hair keeps its place on the head through the idle pose (head-local centre {atRest:F3} at rest, {posed:F3} posed)");
        }

        [Test]
        public void TheBuiltCatalogHasNoNullReferencesAnywhere()
        {
            foreach (var slot in catalog.Slots)
                foreach (var o in slot.options)
                    switch (o.kind)
                    {
                        case ModelAppearanceKind.AttachParts: case ModelAppearanceKind.AttachPartsOwnMaterial: Assert.IsTrue(o.parts.All(p => p != null), o.id + " parts"); break;
                        case ModelAppearanceKind.AttachStatic: case ModelAppearanceKind.AttachOwnRig: Assert.IsNotNull(o.prefab, o.id); break;
                    }
        }

        // ------------------------------------------------------------------ fitting to the body builds (Phase C1)
        private static BodySurface bare;
        private static BodySurface Bare() => bare ??= BodySurface.Load(out var problem) ?? throw new System.Exception(problem);
        private const float MaxExtraSink = 0.02f;   // metres a build may push a piece deeper into the body than it sits at the default build

        private IEnumerable<ModelAppearanceOption> RigidWithOffsets() =>
            catalog.Slots.SelectMany(s => s.options).Where(o => (o.kind == ModelAppearanceKind.AttachStatic || o.kind == ModelAppearanceKind.AttachOwnRig) && o.buildOffsets.Count > 0);

        [Test]
        public void EveryFittedPieceCarriesTheThreeBuildShapesAndTheirNamesResolve()
        {
            foreach (var option in FittedOptions())
            {
                var mesh = option.parts[0].GetComponentInChildren<SkinnedMeshRenderer>(true).sharedMesh;
                Assert.AreEqual(3, mesh.blendShapeCount, option.id);
                foreach (var shape in BodySurface.BuildShapes) Assert.GreaterOrEqual(ModelAppearanceApplier.BlendShapeIndex(mesh, shape), 0, $"{option.id} has {shape}");
            }
        }

        [Test]
        public void NoBuildMakesAFittedPieceSinkIntoTheBodyFurtherThanItDoesAtTheDefaultBuild()
        {
            var worstUnfitted = 0f; var unfittedFails = 0;
            foreach (var option in FittedOptions())
            {
                var mesh = option.parts[0].GetComponentInChildren<SkinnedMeshRenderer>(true).sharedMesh;
                var atDefault = PieceClearance.Penetration(Bare(), PieceClearance.FittedVertices(mesh, new Dictionary<string, float>()));
                var unfittedWorst = 0f;
                foreach (var (name, weights) in PieceClearance.Builds.Skip(1))
                {
                    var deformed = Bare().Deformed(weights);
                    var fitted = PieceClearance.Penetration(deformed, PieceClearance.FittedVertices(mesh, weights));
                    Assert.LessOrEqual(fitted, atDefault + MaxExtraSink, $"{option.id} at {name}: {fitted * 100f:F1} cm in, {atDefault * 100f:F1} cm at the default build");
                    unfittedWorst = Mathf.Max(unfittedWorst, PieceClearance.Penetration(deformed, PieceClearance.FittedVertices(mesh, new Dictionary<string, float>())) - atDefault);
                }

                worstUnfitted = Mathf.Max(worstUnfitted, unfittedWorst);
                if (unfittedWorst > MaxExtraSink) unfittedFails++;
            }

            Assert.GreaterOrEqual(unfittedFails, 3, "the measure is meaningful: without the fit several pieces would sink in further than the limit (worst " + worstUnfitted * 100f + " cm)");
        }

        [Test]
        public void RigidGearAndCapesCarryBuildOffsetsForEveryBuildAndFollowTheBodyWithoutSinkingIn()
        {
            var options = RigidWithOffsets().ToList();
            CollectionAssert.IsSupersetOf(options.Select(o => o.id), new[] { "gear-viking-pouch-01", "gear-war-camp-bags-01", "gear-war-camp-backpack-01", "shoulders-war-camp-collar-01" }, "gear and the collar carry offsets");
            var master = catalog.MasterPrefab.GetComponentsInChildren<Transform>(true);
            var worldRotation = new Dictionary<string, Quaternion>();
            var unfittedFails = 0;
            foreach (var option in options.Where(o => o.kind == ModelAppearanceKind.AttachStatic))
            {
                CollectionAssert.AreEquivalent(BodySurface.BuildShapes, option.buildOffsets.Select(b => b.shape).ToArray(), option.id);
                var bone = master.First(t => t.name == option.bone);
                var atDefault = PieceClearance.Penetration(Bare(), PieceClearance.StaticVertices(option.prefab, bone.position, option.localScale.x, Vector3.zero));
                foreach (var (name, weights) in PieceClearance.Builds.Skip(1))
                {
                    var offset = option.buildOffsets.Single(b => b.shape == weights.Keys.Single()).offset;
                    var worldOffset = bone.rotation * offset;
                    var deformed = Bare().Deformed(weights);
                    var with = PieceClearance.Penetration(deformed, PieceClearance.StaticVertices(option.prefab, bone.position, option.localScale.x, worldOffset));
                    var without = PieceClearance.Penetration(deformed, PieceClearance.StaticVertices(option.prefab, bone.position, option.localScale.x, Vector3.zero));
                    Assert.LessOrEqual(with, atDefault + MaxExtraSink * 1.5f, $"{option.id} at {name}: {with * 100f:F1} cm in with the offset, {atDefault * 100f:F1} at the default build");
                    if (without > atDefault + MaxExtraSink * 1.5f) unfittedFails++;
                }
            }

            Assert.GreaterOrEqual(unfittedFails, 5, "without the offsets several pieces would sink in further than the limit at Heavy or Buff");
        }

        [Test]
        public void TheFitFadeIsOneNearTheBodyZeroFarAwayAndLinearBetween()
        {
            Assert.AreEqual(1f, PolygonPieceFitter.Fade(0f)); Assert.AreEqual(1f, PolygonPieceFitter.Fade(PolygonPieceFitter.FitNear), 1e-5f);
            Assert.AreEqual(0f, PolygonPieceFitter.Fade(PolygonPieceFitter.FitFar), 1e-5f); Assert.AreEqual(0f, PolygonPieceFitter.Fade(1f));
            Assert.AreEqual(0.5f, PolygonPieceFitter.Fade((PolygonPieceFitter.FitNear + PolygonPieceFitter.FitFar) / 2f), 1e-5f);
        }

        [Test]
        public void TheGridClosestPointAgreesWithTheBruteForceReferenceEverywhereAroundTheBody()
        {
            var random = new System.Random(12);
            var bounds = new Bounds(new Vector3(0f, 0.9f, 0f), new Vector3(1.4f, 2.1f, 0.9f));
            for (var i = 0; i < 250; i++)
            {
                var p = bounds.center + new Vector3((float)(random.NextDouble() - 0.5) * bounds.size.x, (float)(random.NextDouble() - 0.5) * bounds.size.y, (float)(random.NextDouble() - 0.5) * bounds.size.z);
                Assert.AreEqual(Bare().ClosestBruteForce(p).Distance, Bare().Closest(p).Distance, 1e-5f, $"point {p}");
            }
        }

        [Test]
        public void AFittedPieceIsKeptNotRecreatedWhenTheSameChoiceIsAppliedAgain()
        {
            Apply(("hair", "hair-viking-hair-02"));
            var first = Marks().Single(p => p.SlotId == "hair").gameObject;
            Apply(("hair", "hair-viking-hair-02"), ("build", "build-buff"));
            Assert.AreSame(first, Marks().Single(p => p.SlotId == "hair").gameObject, "the slot already shows it: left alone (no re-clone)");
        }

        [Test]
        public void ABuildOffsetScalesLinearlyWithTheBuildWeight()
        {
            var option = Option("gear", "gear-war-camp-bags-01");
            var buildSlot = catalog.FindSlot("build");
            var half = new ModelAppearanceOption { id = "build-half-test", title = "Half", kind = ModelAppearanceKind.BlendShapes, blendShapes = new List<ModelAppearanceBlendShape> { new() { shape = "defaultHeavy", weight = 50f } } };
            buildSlot.options.Add(half);
            try
            {
                Apply(("gear", option.id), ("build", "build-heavy"));
                var full = Marks().Single(p => p.SlotId == "gear").transform.localPosition - option.localPosition;
                Apply(("gear", option.id), ("build", half.id));
                var halfMove = Marks().Single(p => p.SlotId == "gear").transform.localPosition - option.localPosition;
                Assert.AreEqual(full.x / 2f, halfMove.x, 1e-5f); Assert.AreEqual(full.y / 2f, halfMove.y, 1e-5f); Assert.AreEqual(full.z / 2f, halfMove.z, 1e-5f);
            }
            finally { buildSlot.options.Remove(half); }
        }

        [Test]
        public void ABuildMovesAKeptRigidPieceAndTheDefaultBuildPutsItBack()
        {
            var option = Option("gear", "gear-war-camp-bags-01");
            Apply(("gear", option.id), ("build", "build-default"));
            var piece = Marks().Single(p => p.SlotId == "gear").transform;
            var home = piece.localPosition;
            Assert.AreEqual(option.localPosition, home);
            Apply(("gear", option.id), ("build", "build-heavy"));
            Assert.AreSame(piece, Marks().Single(p => p.SlotId == "gear").transform, "the same object: the slot was kept, only repositioned");
            var moved = piece.localPosition - home;
            Assert.That(moved.magnitude, Is.InRange(0.02f, 0.25f), "Heavy moves the bag out by a few centimetres, not metres");
            Assert.AreEqual(option.buildOffsets.Single(b => b.shape == "defaultHeavy").offset.x, moved.x, 1e-5f);
            Apply(("gear", option.id));
            Assert.AreEqual(home.x, piece.localPosition.x, 1e-6f); Assert.AreEqual(home.y, piece.localPosition.y, 1e-6f); Assert.AreEqual(home.z, piece.localPosition.z, 1e-6f);
        }

        [Test]
        public void ACapeFollowsTheBuildToo()
        {
            var cape = catalog.FindSlot("cloak").options.First(o => o.kind == ModelAppearanceKind.AttachOwnRig);
            Assert.IsNotEmpty(cape.buildOffsets, cape.id);
            Apply(("cloak", cape.id), ("build", "build-heavy"));
            Assert.Greater((Marks().Single(p => p.SlotId == "cloak").transform.localPosition - cape.localPosition).magnitude, 0.01f);
        }

        // ------------------------------------------------------------------ tunic relief (Phase C2)
        private static Vector2 CellUv(int x, int y) => new((x + 0.5f) / 32f, (y + 0.5f) / 32f);

        /// <summary>(vertex, distance to the nearest vertex of the bare body) for every vertex of the tunic's regions whose UV sits on the colour cell.</summary>
        private List<(Vector3 vertex, float offset)> TunicVerticesOnCell(int cellX, int cellY, params string[] regions)
        {
            Apply(("outfit", "outfit-bare"));
            var bare = SidekickParts().Where(s => s.GetComponentInParent<ModelAppearancePart>().SlotId == "outfit").SelectMany(s => s.sharedMesh.vertices).ToArray();
            Apply(("outfit", "outfit-tunic"));
            var uv = CellUv(cellX, cellY);
            var result = new List<(Vector3, float)>();
            foreach (var smr in SidekickParts().Where(s => s.GetComponentInParent<ModelAppearancePart>().SlotId == "outfit" && regions.Contains(Region(s))))
            {
                var vertices = smr.sharedMesh.vertices; var uvs = smr.sharedMesh.uv;
                for (var i = 0; i < vertices.Length; i++)
                    if (Vector2.Distance(uvs[i], uv) < 0.001f) result.Add((vertices[i], bare.Min(b => Vector3.Distance(b, vertices[i]))));
            }

            return result;
        }

        private static readonly string[] TunicRegions = { "10TORS", "11AUPL", "12AUPR", "13ALWL", "14ALWR", "17HIPS", "18LEGL", "19LEGR", "20FOTL", "21FOTR" };

        [Test]
        public void TheTunicsBeltBuckleIsASymmetricMetalBlockStandingProudOfTheBelt()
        {
            var buckle = TunicVerticesOnCell(6, 4, "10TORS", "17HIPS");
            Assert.GreaterOrEqual(buckle.Count, 8, "the buckle exists");
            var minX = buckle.Min(v => v.vertex.x); var maxX = buckle.Max(v => v.vertex.x);
            Assert.AreEqual(0f, minX + maxX, 0.01f, $"symmetric about the centre line ({minX * 100f:F1} .. {maxX * 100f:F1} cm)");
            Assert.That(maxX - minX, Is.InRange(0.04f, 0.13f), "a buckle, not a plate");
            Assert.Greater(buckle.Average(v => v.vertex.z), 0.04f, "on the front of the belly");
            Assert.Greater(buckle.Max(v => v.offset), 0.03f, "stands at least 3 cm proud of the body (1.6 cm cloth + the relief)");
        }

        [Test]
        public void TheLeatherBandsStandProudOfTheClothAndThePlainClothDoesNot()
        {
            var leather = TunicVerticesOnCell(8, 16, TunicRegions);
            var cloth = TunicVerticesOnCell(4, 28, "10TORS", "11AUPL", "12AUPR");
            Assert.IsNotEmpty(leather); Assert.IsNotEmpty(cloth);
            Assert.Greater(leather.Average(v => v.offset), cloth.Average(v => v.offset) + 0.004f, "the belt, collar, cuffs and hem are raised 0.4 cm or more above the cloth next to them on average");
            var chest = cloth.Where(v => v.vertex.y > 1.15f && v.vertex.y < 1.30f && Mathf.Abs(v.vertex.x) < 0.15f).ToList();
            Assert.IsNotEmpty(chest);
            Assert.Less(chest.Max(v => v.offset), 0.026f, "the plain chest stays a snug 1.5 cm layer (the vertices next to a band slope up to it, so only the open chest is measured)");
        }

        [Test]
        public void TheTunicNoLongerUsesTheIrisCellForItsLeather()
        {
            // (8,4) is an eye cell: a future eye colour row would recolour the trim
            Assert.IsEmpty(TunicVerticesOnCell(8, 4, TunicRegions));
        }

        // ------------------------------------------------------------------ skin tone and hair colour (Phase C3)
        private Material PartMaterial() => SidekickParts().SelectMany(s => s.sharedMaterials).Distinct().Single();
        private static Color32[] Pixels(Material m) => ((Texture2D)m.GetTexture("_ColorMap")).GetPixels32();
        private static HashSet<int> CellIndexes(params ModelAppearanceOption[] options) => options.SelectMany(o => o.cells).Select(c => c.y * 32 + c.x).ToHashSet();
        private static bool Same(Color32 a, Color32 b) => a.r == b.r && a.g == b.g && a.b == b.b;
        private ModelAppearanceOption Option(string slot, string id) => catalog.FindSlot(slot).options.Single(o => o.id == id);
        private Material Look(int n) => catalog.FindSlot("look").options.Single(o => o.id == $"look-starter-{n}").material;

        [Test]
        public void SkinAndHairColourAreOptionalRowsFromTheLookAppendedAfterBuild()
        {
            foreach (var id in new[] { "skin", "haircolour" })
            {
                var slot = catalog.FindSlot(id);
                Assert.AreEqual("Colour", slot.tab); Assert.IsFalse(slot.required, id); Assert.AreEqual("From look", slot.noneTitle);
                Assert.AreEqual(8, slot.options.Count, id);
                Assert.IsTrue(slot.options.All(o => o.kind == ModelAppearanceKind.ColourCells && o.cells.Count > 0 && o.swatch.a == 255), id);
            }

            Assert.AreEqual(new[] { "skin-pale", "skin-fair", "skin-light", "skin-tan", "skin-bronze", "skin-brown", "skin-dark", "skin-deep" }, catalog.FindSlot("skin").options.Select(o => o.id).ToArray());
            Assert.AreEqual(new[] { "haircolour-black", "haircolour-dark-brown", "haircolour-brown", "haircolour-auburn", "haircolour-ginger", "haircolour-blond", "haircolour-grey", "haircolour-white" }, catalog.FindSlot("haircolour").options.Select(o => o.id).ToArray());
        }

        [Test]
        public void TheSkinCellsAreTheMeasuredSkinCellsIncludingTheNoseAndNeverTheUnderwear()
        {
            var cells = CellIndexes(Option("skin", "skin-tan"));
            foreach (var (x, y) in new[] { (2, 4), (4, 4), (2, 6), (4, 6), (2, 8), (4, 8), (0, 10), (0, 11), (2, 10), (4, 10), (2, 2) }) CollectionAssert.Contains(cells, y * 32 + x, $"skin cell ({x},{y})");
            foreach (var (x, y) in new[] { (0, 18), (0, 19), (1, 19), (8, 2), (8, 4), (6, 2), (4, 2) }) CollectionAssert.DoesNotContain(cells, y * 32 + x, $"({x},{y}) is not skin");
            CollectionAssert.AreEquivalent(new[] { 2 * 32 + 12, 8 * 32 + 12, 8 * 32 + 14, 10 * 32 + 12, 10 * 32 + 14 }, CellIndexes(Option("haircolour", "haircolour-brown")), "highlight, hair, facial hair, both brows");
        }

        [Test]
        public void TheTanSkinAndBrownHairAreStarter01sOwnColours()
        {
            var starter = Pixels(Look(1));
            foreach (var option in new[] { Option("skin", "skin-tan"), Option("haircolour", "haircolour-brown") })
                foreach (var cell in option.cells) Assert.IsTrue(Same(starter[cell.y * 32 + cell.x], cell.colour), $"{option.id} ({cell.x},{cell.y}) equals Starter_01");
        }

        [Test]
        public void TheColourMapsAreReadableAndUncompressedSoCellsCanBeComposedAndRenderExactly()
        {
            foreach (var n in new[] { 1, 2, 3, 4 })
            {
                var texture = (Texture2D)Look(n).GetTexture("_ColorMap");
                Assert.IsTrue(texture.isReadable, $"Starter_0{n} is readable");
                Assert.IsFalse(UnityEngine.Experimental.Rendering.GraphicsFormatUtility.IsCompressedFormat(texture.graphicsFormat), $"Starter_0{n} is uncompressed ({texture.graphicsFormat})");
                Assert.AreEqual(32, texture.width); Assert.AreEqual(32, texture.height);
            }
        }

        [Test]
        public void NoSkinOrHairChoiceKeepsTheLooksOwnMaterialOnEverySidekickPart()
        {
            foreach (var n in new[] { 1, 2, 3, 4 })
            {
                Apply(("look", $"look-starter-{n}"), ("outfit", "outfit-civilian"), ("hair", "hair-human-01"));
                Assert.AreSame(Look(n), PartMaterial(), $"look {n}");
            }
        }

        [Test]
        public void ASkinChoiceOverridesExactlyTheSkinCellsAndNothingElse()
        {
            var skin = Option("skin", "skin-dark");
            Apply(("look", "look-starter-2"), ("skin", skin.id));
            var material = PartMaterial();
            Assert.AreNotSame(Look(2), material); Assert.AreEqual("Starter_02", material.name);
            Assert.AreEqual(Look(2).shader, material.shader);
            var composed = Pixels(material); var basis = Pixels(Look(2)); var cells = CellIndexes(skin);
            for (var i = 0; i < 1024; i++)
                Assert.IsTrue(Same(composed[i], cells.Contains(i) ? skin.cells.Single(c => c.y * 32 + c.x == i).colour : basis[i]), $"pixel ({i % 32},{i / 32})");
            Assert.IsFalse(Same(composed[6 * 32 + 2], basis[6 * 32 + 2]), "the torso really changed");
        }

        [Test]
        public void AHairColourOverridesTheHairAndBrowCellsAndSidekickHairWearsIt()
        {
            var hair = Option("haircolour", "haircolour-ginger");
            Apply(("look", "look-starter-3"), ("hair", "hair-human-01"), ("facialhair", "facialhair-human-01"), ("haircolour", hair.id));
            var material = PartMaterial();
            var composed = Pixels(material); var basis = Pixels(Look(3)); var cells = CellIndexes(hair);
            for (var i = 0; i < 1024; i++) Assert.IsTrue(Same(composed[i], cells.Contains(i) ? hair.cells.Single(c => c.y * 32 + c.x == i).colour : basis[i]), $"pixel ({i % 32},{i / 32})");
            var hairParts = SidekickParts().Where(s => s.GetComponentInParent<ModelAppearancePart>().SlotId == "hair").ToArray();
            Assert.IsNotEmpty(hairParts); Assert.IsTrue(hairParts.All(s => s.sharedMaterials.All(m => m == material)), "Sidekick hair takes the colour");
        }

        [Test]
        public void SkinAndHairCombineAndEachRowAloneLeavesTheOthersCellsToTheLook()
        {
            var skin = Option("skin", "skin-pale"); var hair = Option("haircolour", "haircolour-white");
            Apply(("look", "look-starter-4"), ("skin", skin.id), ("haircolour", hair.id));
            var both = Pixels(PartMaterial()); var basis = Pixels(Look(4));
            foreach (var cell in skin.cells.Concat(hair.cells)) Assert.IsTrue(Same(both[cell.y * 32 + cell.x], cell.colour));
            Assert.IsTrue(Same(both[18 * 32 + 0], basis[18 * 32 + 0]), "the underwear cell is the look's");
            Apply(("look", "look-starter-4"), ("haircolour", hair.id));
            Assert.IsTrue(Same(Pixels(PartMaterial())[6 * 32 + 2], basis[6 * 32 + 2]), "no skin choice: the look's skin");
        }

        [Test]
        public void ChangingOnlyTheSkinRowRepaintsAndClearingItGoesBackToTheLook()
        {
            Apply(("look", "look-starter-1"), ("skin", "skin-pale"));
            var pale = PartMaterial();
            Apply(("look", "look-starter-1"), ("skin", "skin-deep"));
            var deep = PartMaterial();
            Assert.AreNotSame(pale, deep);
            Assert.IsTrue(Same(Pixels(deep)[6 * 32 + 2], Option("skin", "skin-deep").cells.Single(c => c.x == 2 && c.y == 6).colour));
            Apply(("look", "look-starter-1"));
            Assert.AreSame(Look(1), PartMaterial());
        }

        [Test]
        public void TwoCharactersWithTheSameChoicesShareOneMaterialAndDifferentChoicesDoNot()
        {
            var other = UnityEngine.Object.Instantiate(catalog.MasterPrefab);
            try
            {
                Apply(("look", "look-starter-2"), ("skin", "skin-brown"), ("haircolour", "haircolour-auburn"));
                var first = PartMaterial();
                ModelAppearanceApplier.Apply(other, catalog, Choices(("look", "look-starter-2"), ("skin", "skin-brown"), ("haircolour", "haircolour-auburn")), immediate: true);
                Assert.AreSame(first, other.GetComponentsInChildren<SkinnedMeshRenderer>(true).Where(s => s.GetComponentInParent<ModelAppearancePart>(true) != null).SelectMany(s => s.sharedMaterials).Distinct().Single());
                Apply(("look", "look-starter-2"), ("skin", "skin-brown"), ("haircolour", "haircolour-blond"));
                Assert.AreNotSame(first, PartMaterial());
            }
            finally { UnityEngine.Object.DestroyImmediate(other); }
        }

        [Test]
        public void ARebuiltOptionWithNewColoursUnderTheSameIdsNeverServesTheStaleTexture()
        {
            var option = Option("skin", "skin-fair");
            var cell = option.cells[0];
            var before = cell.colour;
            try
            {
                Apply(("look", "look-starter-1"), ("skin", option.id));
                var first = PartMaterial();
                cell.colour = new Color32((byte)(before.r ^ 0x40), before.g, before.b, 255);
                Apply(("look", "look-starter-1"), ("skin", option.id));
                var second = PartMaterial();
                Assert.AreNotSame(first, second);
                Assert.IsTrue(Same(Pixels(second)[cell.y * 32 + cell.x], cell.colour));
            }
            finally { cell.colour = before; }
        }

        [Test]
        public void PolygonHairBeardsAndHelmetsKeepTheirOwnMaterialsUnderAColourChoice()
        {
            Apply(("look", "look-starter-1"), ("skin", "skin-dark"), ("haircolour", "haircolour-white"), ("hair", "hair-viking-hair-06"), ("facialhair", "facialhair-viking-beard-01"), ("headwear", "headwear-viking-helmet-01"));
            var tinted = PartMaterial();
            foreach (var slot in new[] { "hair", "facialhair", "headwear" })
            {
                var piece = Marks().Single(p => p.SlotId == slot);
                Assert.IsTrue(piece.GetComponentsInChildren<Renderer>(true).All(r => r.sharedMaterials.All(m => m != null && m != tinted && m.name.StartsWith("PolygonVikingRealm"))), slot + " keeps its Polygon material");
            }
        }

        [Test]
        public void AnUnreadableColourMapDegradesToTheLooksOwnMaterialWithOneWarning()
        {
            var texture = new Texture2D(32, 32, TextureFormat.RGBA32, false); texture.Apply(false, true);   // makeNoLongerReadable
            var look = new Material(Look(1)) { name = "Unreadable" }; look.SetTexture("_ColorMap", texture);
            try
            {
                using var log = new LogCapture();
                var tints = new List<ModelAppearanceOption> { Option("skin", "skin-dark") };
                Assert.AreSame(look, ModelAppearanceColours.Resolve(look, tints));
                Assert.AreSame(look, ModelAppearanceColours.Resolve(look, tints));
                Assert.AreEqual(1, log.Messages.Count, string.Join("\n", log.Messages));
                StringAssert.Contains("not readable", log.Messages[0]);
            }
            finally { UnityEngine.Object.DestroyImmediate(look); UnityEngine.Object.DestroyImmediate(texture); }
        }

        [Test]
        public void TheBakedHumanBasePrefabHoldsOnlyAssetMaterialsNeverARuntimeOne()
        {
            var prefab = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(ModelAppearanceFamilies.BasePrefabPath("Human"));
            Assert.IsNotNull(prefab);
            foreach (var renderer in prefab.GetComponentsInChildren<Renderer>(true))
                foreach (var material in renderer.sharedMaterials) Assert.IsTrue(material != null && UnityEditor.EditorUtility.IsPersistent(material), $"{renderer.name} has a saved material");
        }

        // ------------------------------------------------------------------ Polygon pieces
        [Test]
        public void APolygonHelmetKeepsItsOwnMaterialUnderALookAndSitsUprightOnTheHead()
        {
            var look = catalog.FindSlot("look").options.First(o => o.id == "look-starter-2");
            Apply(("headwear", "headwear-viking-helmet-horns-02"), ("look", look.id), ("cloak", "cloak-viking-cape-01"));
            var helmet = Marks().Single(p => p.SlotId == "headwear");
            Assert.AreEqual("head", helmet.transform.parent.name);
            Assert.IsTrue(helmet.GetComponentsInChildren<Renderer>(true).All(r => r.sharedMaterials.All(m => m != look.material)), "the helmet keeps its Polygon material");
            Assert.IsTrue(SidekickParts().All(s => s.sharedMaterials.All(m => m == look.material)), "the Sidekick parts wear the look's own colour map (no skin or hair choice)");
            Assert.AreEqual(0f, Vector3.Angle(helmet.transform.up, Vector3.up), 2f, "upright in the world at rest");
            Assert.AreEqual(0f, Vector3.Angle(Marks().Single(p => p.SlotId == "cloak").transform.up, Vector3.up), 2f, "the cape too, on spine_03");
            Assert.AreEqual(CharacterScale, helmet.transform.localScale.x, 0.001f);
        }

        private static float CharacterScale => HumanCatalogAuthor.PolygonScale;

        [Test]
        public void APolygonPieceStaysAlignedWithTheCharacterAfterTheIdleIsSampled()
        {
            Apply(("headwear", "headwear-viking-helmet-horns-02"));
            catalog.PreviewIdleClip.SampleAnimation(root, 0.25f);
            root.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            var helmet = Marks().Single(p => p.SlotId == "headwear");
            var head = Bone("head");
            // the piece is a child of the head: whatever the idle does to the head (it turns the whole body ~165 degrees on this rig), the piece moves with it
            Assert.AreEqual(0f, Quaternion.Angle(head.rotation * helmet.transform.localRotation, helmet.transform.rotation), 0.01f);
            Assert.Greater(Vector3.Dot(head.rotation * Quaternion.Inverse(catalog.MasterPrefab.GetComponentsInChildren<Transform>(true).First(t => t.name == "head").rotation) * Vector3.up, helmet.transform.up), 0.5f, "the helmet's up follows the head's turn, it is not left pointing the old way");
        }

        [Test]
        public void ThePreviewFacesTheCameraWhateverTheIdleClipDidToTheBody()
        {
            // the UAL idle sampled in the editor turns a Sidekick body ~165 degrees (in the running game it leaves a human at about -3): the preview must undo that itself
            using var preview = new ModelAppearancePreview(catalog);
            preview.Apply(Choices());
            var animator = preview.Model.GetComponentsInChildren<Animator>(true).First(a => a.avatar != null);
            var side = animator.GetBoneTransform(HumanBodyBones.RightUpperLeg).position - animator.GetBoneTransform(HumanBodyBones.LeftUpperLeg).position;
            side.y = 0f;
            var facing = Vector3.Cross(side.normalized, Vector3.up);
            Assert.Less(Mathf.Abs(Vector3.SignedAngle(Vector3.forward, facing, Vector3.up)), 12f, "the hip line faces +Z, towards the preview camera");
            Assert.AreEqual(0f, preview.Model.transform.position.magnitude, 1e-4f);
        }

        [Test]
        public void ClonesLoseAnimatorsAndCollidersAndTakeTheRootsLayer()
        {
            root.layer = 7;
            Apply(("headwear", "headwear-viking-helmet-01"), ("cloak", "cloak-viking-cape-02"), ("gear", "gear-war-camp-bags-01"));
            Assert.AreEqual(1, root.GetComponentsInChildren<Animator>(true).Length, "only the master's own Animator");
            Assert.IsEmpty(root.GetComponentsInChildren<Collider>(true));
            Assert.IsTrue(Marks().All(p => p.GetComponentsInChildren<Transform>(true).All(t => t.gameObject.layer == 7)));
        }

        [Test]
        public void EveryOptionOfEveryRowAppliesWithoutAWarning()
        {
            using var log = new LogCapture();
            foreach (var slot in catalog.Slots)
                foreach (var option in slot.options)
                {
                    Apply((slot.id, option.id));
                    if (option.kind == ModelAppearanceKind.AttachParts || option.kind == ModelAppearanceKind.AttachStatic || option.kind == ModelAppearanceKind.AttachOwnRig)
                        Assert.IsNotEmpty(Marks().Where(p => p.OptionId == option.id), option.id);
                }

            Assert.IsEmpty(log.Messages, string.Join("\n", log.Messages.Take(10)));
        }

        [Test]
        public void TheRandomizerRollsEveryRowAndTheResultApplies()
        {
            var roll = ModelAppearanceRandomizer.Roll(catalog, 33);
            foreach (var slot in catalog.Slots.Where(s => s.required)) Assert.IsNotNull(slot.Find(roll[new DefinitionId(slot.id)].Value), slot.id);
            using var log = new LogCapture();
            ModelAppearanceApplier.Apply(root, catalog, roll, immediate: true);
            Assert.IsEmpty(log.Messages, string.Join("\n", log.Messages.Take(5)));
        }
    }
}
