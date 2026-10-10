using System.Collections.Generic;
using System.Linq;
using CRPG.Core;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using VikingRaid.Appearance;
using VikingRaid.Appearance.Editor;

namespace VikingRaid.Tests.EditMode
{
    /// <summary>The Viking appearance catalog (authored from the real Synty packs) and the applier that dresses the real master rig from it.</summary>
    public sealed class ModelAppearanceTests
    {
        private ModelAppearanceCatalog catalog;
        private GameObject root;

        [OneTimeSetUp]
        public void BuildCatalog() => catalog = ModelAppearanceCatalogAuthor.Build();

        [SetUp]
        public void Instantiate() => root = Object.Instantiate(catalog.MasterPrefab);

        [TearDown]
        public void Cleanup() => Object.DestroyImmediate(root);

        private static Dictionary<DefinitionId, DefinitionId> Choices(params (string slot, string option)[] pairs) =>
            pairs.ToDictionary(p => new DefinitionId(p.slot), p => new DefinitionId(p.option));

        private void Apply(params (string slot, string option)[] pairs) => ModelAppearanceApplier.Apply(root, catalog, Choices(pairs), immediate: true);

        private IEnumerable<SkinnedMeshRenderer> ActiveMasterBodies() =>
            root.GetComponentsInChildren<SkinnedMeshRenderer>(false).Where(s => s.GetComponentInParent<ModelAppearancePart>() == null && s.name.StartsWith("SM_Chr_") && !s.name.Contains("Attach"));

        private ModelAppearancePart[] Parts(string slot) => root.GetComponentsInChildren<ModelAppearancePart>(true).Where(p => p.SlotId == slot).ToArray();

        // ------------------------------------------------------------------ catalog
        [Test]
        public void TheCatalogHasEightRowsOnThreeTabsInOrder()
        {
            Assert.AreEqual(new[] { "body", "look", "hair", "headwear", "face", "cloak", "shoulders", "gear" }, catalog.Slots.Select(s => s.id).ToArray());
            Assert.AreEqual(new[] { "Body", "Head", "Wear" }, catalog.Tabs.ToArray());
            Assert.AreEqual(new[] { "body", "look" }, catalog.Slots.Where(s => s.required).Select(s => s.id).ToArray());
        }

        [Test]
        public void RequiredRowsHaveTheirContentAndAValidDefault()
        {
            Assert.AreEqual(10, catalog.FindSlot("body").options.Count);
            Assert.AreEqual(15, catalog.FindSlot("look").options.Count);
            foreach (var slot in catalog.Slots.Where(s => s.required)) Assert.IsNotNull(slot.Find(slot.defaultOptionId), $"{slot.id} default");
        }

        [Test]
        public void EveryOptionIsCompleteAndIdsAreUniqueWithinARow()
        {
            foreach (var slot in catalog.Slots)
            {
                Assert.IsNotEmpty(slot.options, slot.id);
                Assert.AreEqual(slot.options.Count, slot.options.Select(o => o.id).Distinct().Count(), slot.id + " ids");
                foreach (var o in slot.options)
                {
                    Assert.IsNotEmpty(o.title, o.id);
                    switch (o.kind)
                    {
                        case ModelAppearanceKind.ToggleChild: Assert.IsNotEmpty(o.childName, o.id); break;
                        case ModelAppearanceKind.Palette: Assert.IsNotNull(o.material, o.id); break;
                        case ModelAppearanceKind.AttachStatic:
                        case ModelAppearanceKind.AttachOwnRig: Assert.IsNotNull(o.prefab, o.id); Assert.IsNotEmpty(o.bone, o.id); break;
                        case ModelAppearanceKind.AttachRigged: Assert.IsNotNull(o.prefab, o.id); Assert.IsNotEmpty(o.childName, o.id); break;
                    }
                }
            }
        }

        [Test]
        public void EveryMasterMeshBelongsToExactlyOneRowSoNothingStaysOnFromTheStockLook()
        {
            var toggled = catalog.Slots.SelectMany(s => s.options).Where(o => o.kind == ModelAppearanceKind.ToggleChild).Select(o => o.childName).ToList();
            var meshes = catalog.MasterPrefab.GetComponentsInChildren<SkinnedMeshRenderer>(true).Select(s => s.name).ToList();
            CollectionAssert.AreEquivalent(meshes, toggled);
        }

        [Test]
        public void ThePaletteFamilyCoversTheMastersOwnMaterialsAndEveryPalette()
        {
            foreach (var m in catalog.MasterPrefab.GetComponentsInChildren<Renderer>(true).SelectMany(r => r.sharedMaterials)) CollectionAssert.Contains(catalog.PaletteFamily, m);
            foreach (var o in catalog.FindSlot("look").options) CollectionAssert.Contains(catalog.PaletteFamily, o.material);
        }

        [Test]
        public void HeadItemsSitOnTheHeadBoneUprightAndCapesAndPacksOnTheSpine()
        {
            foreach (var o in catalog.FindSlot("headwear").options.Where(x => x.kind == ModelAppearanceKind.AttachStatic)) Assert.AreEqual("Head", o.bone, o.id);
            foreach (var o in catalog.FindSlot("cloak").options) Assert.AreEqual("Spine_03", o.bone, o.id);
            foreach (var o in catalog.FindSlot("gear").options.Where(x => x.id.Contains("pouch") || x.id.Contains("bags") || x.id.Contains("bottle"))) Assert.AreEqual("Hips", o.bone, o.id);
            Assert.IsTrue(catalog.FindSlot("cloak").options.Any(o => o.kind == ModelAppearanceKind.AttachOwnRig), "capes carry their own rig");
            Assert.IsTrue(catalog.FindSlot("headwear").options.Any(o => o.kind == ModelAppearanceKind.AttachRigged), "hoods are re-skinned");
        }

        // ------------------------------------------------------------------ applier
        [Test]
        public void ABodyChoiceShowsExactlyThatBody()
        {
            Apply(("body", "body-peasant-female-02"));
            CollectionAssert.AreEqual(new[] { "SM_Chr_Peasant_Female_02" }, ActiveMasterBodies().Select(s => s.name).ToArray());
        }

        [Test]
        public void NothingChosenStillShowsTheDefaultBodyAndNoOptionalPieces()
        {
            Apply();
            CollectionAssert.AreEqual(new[] { "SM_Chr_Warrior_Male_01" }, ActiveMasterBodies().Select(s => s.name).ToArray());
            Assert.IsEmpty(root.GetComponentsInChildren<ModelAppearancePart>(true));
            Assert.IsEmpty(root.GetComponentsInChildren<SkinnedMeshRenderer>(false).Where(s => s.name.Contains("Hair") || s.name.Contains("Fur") || s.name.Contains("Wolf") || s.name.Contains("Bear")), "the stock fur and wolf are switched off");
        }

        [Test]
        public void ASkinnedHairAndAFurAreToggledNotAttached()
        {
            Apply(("hair", "hair-viking-hair-02"), ("shoulders", "shoulders-viking-fur-03"));
            var active = root.GetComponentsInChildren<SkinnedMeshRenderer>(false).Select(s => s.name).ToList();
            CollectionAssert.Contains(active, "SM_Chr_Attach_Hair_02");
            CollectionAssert.Contains(active, "SM_Chr_Attach_Fur_03");
            Assert.AreEqual(1, active.Count(n => n.Contains("Hair")));
            Assert.AreEqual(1, active.Count(n => n.Contains("Fur")));
            Assert.IsEmpty(root.GetComponentsInChildren<ModelAppearancePart>(true));
        }

        [Test]
        public void AStaticHelmetIsMountedOnTheHeadAtTheCatalogsLocalPose()
        {
            var option = catalog.FindSlot("headwear").options.First(o => o.kind == ModelAppearanceKind.AttachStatic && o.id.Contains("helmet-01"));
            Apply(("headwear", option.id));
            var part = Parts("headwear").Single();
            Assert.AreEqual("Head", part.transform.parent.name);
            Assert.AreEqual(option.localPosition, part.transform.localPosition);
            Assert.AreEqual(0f, Quaternion.Angle(Quaternion.Euler(option.localEuler), part.transform.localRotation), 0.01f);
            Assert.AreEqual(option.id, part.OptionId);
        }

        [Test]
        public void AMountedItemStandsUprightInTheWorldAtTheRestPose()
        {
            // the catalog's rule: local rotation = inverse of the bone's rest world rotation, so every attachment starts upright whatever bone it hangs on
            foreach (var slotId in new[] { "headwear", "cloak", "gear" })
            {
                var option = catalog.FindSlot(slotId).options.First(o => o.kind == ModelAppearanceKind.AttachStatic || o.kind == ModelAppearanceKind.AttachOwnRig);
                Apply((slotId, option.id));
                var part = Parts(slotId).Single();
                Assert.AreEqual(0f, Quaternion.Angle(Quaternion.identity, part.transform.rotation), 1.5f, option.id);
                Assert.AreEqual(part.transform.parent.position.x, part.transform.position.x, 0.001f, option.id + " pivot is at the bone");
            }
        }

        [Test]
        public void ChangingARowReplacesItsPieceAndNoneRemovesIt()
        {
            var options = catalog.FindSlot("headwear").options.Where(o => o.kind == ModelAppearanceKind.AttachStatic).Take(2).ToList();
            Apply(("headwear", options[0].id));
            Apply(("headwear", options[1].id));
            Assert.AreEqual(options[1].id, Parts("headwear").Single().OptionId);
            Apply();
            Assert.IsEmpty(Parts("headwear"));
        }

        [Test]
        public void ApplyingTheSameChoicesTwiceIsIdempotent()
        {
            var choices = new[] { ("body", "body-leader-male-01"), ("hair", "hair-viking-hair-06"), ("headwear", "headwear-war-camp-hat-03"), ("cloak", "cloak-viking-cape-01") };
            Apply(choices);
            var first = root.GetComponentsInChildren<ModelAppearancePart>(true).Select(p => p.SlotId + ":" + p.OptionId).OrderBy(x => x).ToArray();
            Apply(choices);
            CollectionAssert.AreEqual(first, root.GetComponentsInChildren<ModelAppearancePart>(true).Select(p => p.SlotId + ":" + p.OptionId).OrderBy(x => x).ToArray());
            Assert.AreEqual(3, first.Length);
        }

        [Test]
        public void ARiggedHoodIsReskinnedOntoTheMastersOwnSkeleton()
        {
            var option = catalog.FindSlot("headwear").options.First(o => o.kind == ModelAppearanceKind.AttachRigged);
            Apply(("headwear", option.id));
            var smr = Parts("headwear").Single().GetComponent<SkinnedMeshRenderer>();
            Assert.IsNotNull(smr);
            var skeleton = new HashSet<Transform>(root.GetComponentsInChildren<Transform>(true).Where(t => t.GetComponentInParent<ModelAppearancePart>(true) == null));
            Assert.IsTrue(smr.bones.All(b => b == null || skeleton.Contains(b)), "every bone is the master's");
            Assert.IsTrue(smr.gameObject.activeInHierarchy);
        }

        [Test]
        public void ARiggedMeshNeedingABoneTheMasterLacksIsNotAttachedAtAll()
        {
            var source = new GameObject("Source");
            var smrGo = new GameObject("Odd_Hood"); smrGo.transform.SetParent(source.transform);
            var bone = new GameObject("NoSuchBone").transform; bone.SetParent(source.transform);
            var smr = smrGo.AddComponent<SkinnedMeshRenderer>();
            smr.bones = new[] { bone };
            var temp = ScriptableObject.CreateInstance<ModelAppearanceCatalog>();
            var slot = new ModelAppearanceSlot { id = "headwear", title = "Headwear", tab = "Head" };
            slot.options.Add(new ModelAppearanceOption { id = "odd", title = "Odd", kind = ModelAppearanceKind.AttachRigged, prefab = source, childName = "Odd_Hood" });
            temp.SetContent(new List<ModelAppearanceSlot> { slot }, new List<Material>(), catalog.MasterPrefab);
            try
            {
                LogAssert.Expect(LogType.Warning, new System.Text.RegularExpressions.Regex("lacks a bone"));
                ModelAppearanceApplier.Apply(root, temp, Choices(("headwear", "odd")), immediate: true);
                Assert.IsEmpty(Parts("headwear"), "a half-bound mesh is worse than none");
            }
            finally { Object.DestroyImmediate(temp); Object.DestroyImmediate(source); }
        }

        [Test]
        public void ClonesLoseAnimatorsAndCollidersAndTakeTheRootsLayer()
        {
            root.layer = 7;
            foreach (var option in catalog.FindSlot("cloak").options.Take(2))
            {
                Apply(("cloak", option.id));
                var part = Parts("cloak").Single();
                Assert.IsEmpty(part.GetComponentsInChildren<Animator>(true), option.id);
                Assert.IsEmpty(part.GetComponentsInChildren<Collider>(true), option.id);
                Assert.IsTrue(part.GetComponentsInChildren<Transform>(true).All(t => t.gameObject.layer == 7), option.id);
            }
        }

        [Test]
        public void ALookReplacesTheVikingMaterialsButNotAWarCampPartsOwn()
        {
            var look = catalog.FindSlot("look").options.First(o => o.id == "look-03-b");
            var hat = catalog.FindSlot("headwear").options.First(o => o.id.StartsWith("headwear-war-camp-hat"));
            Apply(("look", look.id), ("headwear", hat.id));
            var body = root.GetComponentsInChildren<SkinnedMeshRenderer>(true).Where(s => s.GetComponentInParent<ModelAppearancePart>(true) == null).ToList();
            Assert.IsTrue(body.All(s => s.sharedMaterials.All(m => m == look.material)), "every master mesh, active or not, wears the look");
            var hatMaterial = Parts("headwear").Single().GetComponentInChildren<Renderer>().sharedMaterial;
            Assert.AreNotEqual(look.material, hatMaterial);
            Assert.IsFalse(catalog.PaletteFamily.Contains(hatMaterial), "War Camp materials are outside the family");
        }

        [Test]
        public void AnUnknownOptionFallsBackToTheDefaultOnARequiredRowAndToNothingOnAnOptionalOne()
        {
            LogAssert.Expect(LogType.Warning, new System.Text.RegularExpressions.Regex("slot 'body' has no option 'nope'"));
            LogAssert.Expect(LogType.Warning, new System.Text.RegularExpressions.Regex("slot 'hair' has no option 'nope'"));
            Apply(("body", "nope"), ("hair", "nope"), ("unknown-slot", "x"));
            CollectionAssert.AreEqual(new[] { "SM_Chr_Warrior_Male_01" }, ActiveMasterBodies().Select(s => s.name).ToArray());
            Assert.IsFalse(root.GetComponentsInChildren<SkinnedMeshRenderer>(false).Any(s => s.name.Contains("Hair")));
        }

        [Test]
        public void EveryOptionOfEveryRowAppliesWithoutWarningsOrErrors()
        {
            foreach (var slot in catalog.Slots)
                foreach (var option in slot.options)
                {
                    Apply((slot.id, option.id));   // an unexpected log line fails the test
                    if (option.kind == ModelAppearanceKind.Palette || option.kind == ModelAppearanceKind.ToggleChild) continue;
                    Assert.AreEqual(1, Parts(slot.id).Length, option.id);
                }
        }

        // ------------------------------------------------------------------ randomizer
        [Test]
        public void TheSameSeedRollsTheSameNpcAndDifferentSeedsDiffer()
        {
            var a = ModelAppearanceRandomizer.Roll(catalog, 11);
            var b = ModelAppearanceRandomizer.Roll(catalog, 11);
            CollectionAssert.AreEquivalent(a, b);
            Assert.IsTrue(Enumerable.Range(0, 20).Any(seed => !ModelAppearanceRandomizer.Roll(catalog, seed).OrderBy(p => p.Key.Value).SequenceEqual(a.OrderBy(p => p.Key.Value))));
        }

        [Test]
        public void ARollAlwaysFillsRequiredRowsWithRealOptionsAndSometimesLeavesOptionalOnesEmpty()
        {
            var empties = 0;
            for (var seed = 0; seed < 50; seed++)
            {
                var roll = ModelAppearanceRandomizer.Roll(catalog, seed);
                foreach (var slot in catalog.Slots)
                {
                    var has = roll.TryGetValue(new DefinitionId(slot.id), out var id);
                    if (slot.required) { Assert.IsTrue(has, slot.id); Assert.IsNotNull(slot.Find(id.Value), slot.id); }
                    else if (!has) empties++;
                    else Assert.IsNotNull(slot.Find(id.Value), slot.id);
                }
            }

            Assert.Greater(empties, 20);
        }

        [Test]
        public void RollingOneTabLeavesTheOtherTabsRowsUntouched()
        {
            var basis = Choices(("body", "body-leader-female-01"), ("look", "look-05-c"), ("hair", "hair-viking-hair-02"));
            var roll = ModelAppearanceRandomizer.Roll(catalog, 5, "Wear", basis);
            Assert.AreEqual("body-leader-female-01", roll[new DefinitionId("body")].Value);
            Assert.AreEqual("look-05-c", roll[new DefinitionId("look")].Value);
            Assert.AreEqual("hair-viking-hair-02", roll[new DefinitionId("hair")].Value);
        }
    }
}
