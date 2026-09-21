using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using BarrowsEnd.Editor;
using CRPG.Core;
using CRPG.Editor.Validation;
using CRPG.Presentation.Sprites;
using CRPG.Rules;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace BarrowsEnd.Tests
{
    /// <summary>
    /// Task 2.4 stage B: the generated campaign sprite catalogs. Proves the mechanical content is consistent (every option resolves
    /// through the runtime source, the validator is clean, ids follow the permanent scheme) and that stored Sprite references
    /// SURVIVE a re-slice of the source sheets (the slicer must reuse sprite ids by name).
    /// </summary>
    public class SpriteCatalogContentTests
    {
        private static readonly Dictionary<string, int> ExpectedCounts = new()
        {
            ["body"] = 10, ["feet"] = 20, ["clothes"] = 125, ["hair"] = 80, ["helmet"] = 36, ["shield"] = 60, ["weapon"] = 110,
        };

        private static List<(BarrowsEndCatalogAuthor.CatalogSpec spec, SpriteLayerCatalog catalog)> LoadCatalogs() =>
            BarrowsEndCatalogAuthor.Specs
                .Select(spec => (spec, AssetDatabase.LoadAssetAtPath<SpriteLayerCatalog>(BarrowsEndCatalogAuthor.CatalogPath(spec))))
                .ToList();

        [OneTimeSetUp]
        public void AuthorCatalogs() => BarrowsEndCatalogAuthor.Author();

        [Test]
        public void EveryLayerSlotHasACatalogWithTheExpectedNumberOfOptions()
        {
            foreach (var (spec, catalog) in LoadCatalogs())
            {
                Assert.IsNotNull(catalog, $"catalog for {spec.ShortName}");
                Assert.AreEqual(spec.LayerSlot, catalog.LayerSlot.Value);
                Assert.AreEqual(ExpectedCounts[spec.ShortName], catalog.Options.Count, $"{spec.ShortName} option count (the verified column-block sizes)");
            }
        }

        [Test]
        public void EveryOptionOfEveryCatalogResolvesToItsSpriteThroughTheRuntimeSource()
        {
            var rules = new RulesDatabase();
            var catalogs = LoadCatalogs();
            foreach (var (_, catalog) in catalogs) rules.Register(catalog);
            var source = new SpriteCatalogSource(rules);

            foreach (var (_, catalog) in catalogs)
            foreach (var option in catalog.Options)
            {
                Assert.IsNotNull(option.sprite, $"{option.optionId.Value} has a sprite");
                Assert.IsTrue(source.TryGetOption(catalog.LayerSlot, option.optionId, out var resolved), $"{option.optionId.Value} resolves");
                Assert.AreSame(option.sprite, resolved.Sprite);
            }
        }

        [Test]
        public void TheValidatorIsCleanOnTheGeneratedCatalogs()
        {
            var catalogs = LoadCatalogs();
            var results = new List<ValidationResult>();
            foreach (var (spec, catalog) in catalogs) results.AddRange(SpriteLayerCatalogValidator.Validate(catalog, BarrowsEndCatalogAuthor.CatalogPath(spec)));
            results.AddRange(SpriteLayerCatalogValidator.ValidateSet(catalogs.Select(c => (c.catalog, BarrowsEndCatalogAuthor.CatalogPath(c.spec)))));

            Assert.IsEmpty(results, string.Join("\n", results));
        }

        [Test]
        public void OptionIdsFollowThePermanentSchemeAndAreUniquePerCatalog()
        {
            var shape = new Regex(@"^(body|feet|clothes|hair|helmet|shield|weapon)-(kenney|be)-\d+-\d+$");
            foreach (var (spec, catalog) in LoadCatalogs())
            {
                var ids = catalog.Options.Select(o => o.optionId.Value).ToList();
                Assert.AreEqual(ids.Count, ids.Distinct().Count(), $"{spec.ShortName}: ids are unique");
                foreach (var id in ids)
                {
                    Assert.IsTrue(shape.IsMatch(id), $"'{id}' matches <slot>-<sheet>-<col>-<row>");
                    StringAssert.StartsWith(spec.ShortName + "-", id);
                }
            }
        }

        [Test]
        public void TheBodyCatalogCarriesBothTheKenneyBodiesAndTheCustomSkeletonBodiesWithoutCollidingIds()
        {
            var body = LoadCatalogs().First(c => c.spec.ShortName == "body").catalog;
            var ids = body.Options.Select(o => o.optionId.Value).ToList();

            Assert.Contains("body-kenney-0-0", ids);
            Assert.Contains("body-be-0-0", ids);
            Assert.Contains("body-be-1-0", ids);
            Assert.AreEqual(8, ids.Count(i => i.StartsWith("body-kenney-")));
            Assert.AreEqual(2, ids.Count(i => i.StartsWith("body-be-")));
        }

        [Test]
        public void ReauthoringIsIdempotentAndKeepsTheCatalogAssetGuids()
        {
            var before = LoadCatalogs().Select(c => AssetDatabase.AssetPathToGUID(BarrowsEndCatalogAuthor.CatalogPath(c.spec))).ToList();
            var idsBefore = LoadCatalogs().SelectMany(c => c.catalog.Options.Select(o => o.optionId.Value)).ToList();

            BarrowsEndCatalogAuthor.Author();

            var after = LoadCatalogs().Select(c => AssetDatabase.AssetPathToGUID(BarrowsEndCatalogAuthor.CatalogPath(c.spec))).ToList();
            CollectionAssert.AreEqual(before, after, "asset GUIDs are kept");
            CollectionAssert.AreEqual(idsBefore, LoadCatalogs().SelectMany(c => c.catalog.Options.Select(o => o.optionId.Value)).ToList());
        }

        [Test]
        public void ReSlicingLeavesTheSourceSheetMetaFilesByteIdentical()
        {
            // Unity keeps each sprite's internalID (the fileID stored in references) stable by NAME even if the slicer hands out fresh
            // spriteIDs -- verified empirically -- but regenerating them would still rewrite every sheet's .meta on every re-slice
            // (thousands of changed lines). The slicer reuses ids by name so a re-slice is a no-op on disk.
            var metas = Directory.GetFiles("Assets/Art/Source", "*.png.meta");
            Assert.IsNotEmpty(metas);
            var before = metas.ToDictionary(m => m, File.ReadAllText);

            BarrowsEndSetup.SliceAll();

            foreach (var meta in metas) Assert.AreEqual(before[meta], File.ReadAllText(meta), $"{meta} was rewritten by a re-slice");
        }

        [Test]
        public void StoredSpriteReferencesSurviveAReSliceOfTheSourceSheets()
        {
            // The property that matters: a stored Sprite reference (catalog, profile, prefab, scene) must still resolve to the same sprite
            // after the sheets are re-sliced. (Unity keeps the fileID stable by sprite NAME; this pins it against any slicer change.)
            var recorded = new Dictionary<string, (string guid, long localId)>();
            foreach (var (_, catalog) in LoadCatalogs())
            foreach (var option in catalog.Options)
            {
                Assert.IsTrue(AssetDatabase.TryGetGUIDAndLocalFileIdentifier(option.sprite, out var guid, out long localId));
                recorded[option.optionId.Value] = (guid, localId);
            }

            BarrowsEndSetup.SliceAll();

            foreach (var (spec, catalog) in LoadCatalogs())
            {
                AssetDatabase.ImportAsset(BarrowsEndCatalogAuthor.CatalogPath(spec), ImportAssetOptions.ForceUpdate);
                var reloaded = AssetDatabase.LoadAssetAtPath<SpriteLayerCatalog>(BarrowsEndCatalogAuthor.CatalogPath(spec));
                foreach (var option in reloaded.Options)
                {
                    Assert.IsNotNull(option.sprite, $"{option.optionId.Value}: the stored reference still resolves after the re-slice");
                    Assert.IsTrue(AssetDatabase.TryGetGUIDAndLocalFileIdentifier(option.sprite, out var guid, out long localId));
                    Assert.AreEqual(recorded[option.optionId.Value], (guid, localId), $"{option.optionId.Value}: same sprite identity");
                }
            }
        }
    }
}
