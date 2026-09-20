using System.IO;
using System.Linq;
using BarrowsEnd.Editor;
using CRPG.Editor.Validation;
using CRPG.Navigation.Grid.Tilemaps;
using CRPG.Presentation.Sprites;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.Tilemaps;

namespace BarrowsEnd.Tests
{
    /// <summary>Task 3.2: the curated tile semantics, the generated semantic tiles, the shared sorted-object prefab and the palettes.</summary>
    public class TileContentTests
    {
        private TileSemanticsTable table;
        private BarrowsEndTileAuthor.Result firstRun;

        [OneTimeSetUp]
        public void Author()
        {
            table = TileSemanticsTable.Load();
            firstRun = BarrowsEndTileAuthor.Author();
        }

        private static string T(string key, int col, int row) => BarrowsEndTileAuthor.TilePath(key, col, row);
        private static SemanticTile Load(string path) => AssetDatabase.LoadAssetAtPath<SemanticTile>(path);

        // ---------------------------------------------------------------------------------------------
        // The table
        // ---------------------------------------------------------------------------------------------

        [Test]
        public void TheTableLoadsBothSheetsWithValidRules()
        {
            CollectionAssert.AreEqual(new[] { "town", "cave" }, table.Sheets.Select(s => s.Key).ToArray());
            foreach (var sheet in table.Sheets)
            {
                Assert.IsTrue(File.Exists(sheet.Path), sheet.Path);
                Assert.Greater(sheet.Rules.Count, 10, sheet.Key);
                foreach (var rule in sheet.Rules)
                {
                    Assert.AreNotEqual(TileSemantic.Unclassified, rule.Semantic);
                    Assert.LessOrEqual(rule.ColMin, rule.ColMax);
                    Assert.LessOrEqual(rule.RowMin, rule.RowMax);
                    Assert.IsFalse(string.IsNullOrWhiteSpace(rule.Note), $"{sheet.Key}: every rule says what it covers");
                }
            }
        }

        [TestCase("town", 5, 0, TileSemantic.Ground)]      // grass
        [TestCase("town", 0, 0, TileSemantic.Blocked)]     // water
        [TestCase("town", 13, 9, TileSemantic.Prop)]       // tree
        [TestCase("town", 32, 1, TileSemantic.Door)]       // wooden door
        [TestCase("town", 23, 17, TileSemantic.Wall)]      // building wall
        [TestCase("town", 16, 7, TileSemantic.Decor)]      // torch (a later rule overrides the broad Prop rule around it)
        [TestCase("town", 48, 3, TileSemantic.Decor)]      // mushroom inside the window block: the LAST covering rule wins
        [TestCase("town", 45, 3, TileSemantic.Wall)]       // a window: the broad rule still applies next to the exception
        [TestCase("town", 28, 10, TileSemantic.Unclassified)] // no rule covers it: no meaning, no default
        [TestCase("cave", 9, 3, TileSemantic.Ground)]      // stone floor
        [TestCase("cave", 0, 10, TileSemantic.Blocked)]    // water
        [TestCase("cave", 22, 1, TileSemantic.Door)]
        [TestCase("cave", 12, 0, TileSemantic.Wall)]
        [TestCase("cave", 1, 0, TileSemantic.Prop)]
        public void TheLastCoveringRuleDecidesAndNothingCoveredMeansUnclassified(string sheet, int col, int row, TileSemantic expected)
        {
            Assert.AreEqual(expected, table.Resolve(sheet, col, row));
        }

        [Test]
        public void SpriteNamesParseToCells()
        {
            var town = table.SheetByKey("town");
            Assert.IsTrue(TileSemanticsTable.TryParseCell(town, "roguelikeSheet_transparent_12_34", out var col, out var row));
            Assert.AreEqual((12, 34), (col, row));
            Assert.IsFalse(TileSemanticsTable.TryParseCell(town, "roguelikeChar_transparent_1_1", out _, out _));
            Assert.IsFalse(TileSemanticsTable.TryParseCell(town, "roguelikeSheet_transparent_x_1", out _, out _));
        }

        // ---------------------------------------------------------------------------------------------
        // Generated tiles
        // ---------------------------------------------------------------------------------------------

        [Test]
        public void EveryClassifiedSpriteHasATileWithThatSemanticAndEveryUnclassifiedOneHasNone()
        {
            var classified = 0;
            foreach (var sheet in table.Sheets)
            {
                foreach (var sprite in AssetDatabase.LoadAllAssetsAtPath(sheet.Path).OfType<Sprite>())
                {
                    Assert.IsTrue(TileSemanticsTable.TryParseCell(sheet, sprite.name, out var col, out var row), sprite.name);
                    var semantic = TileSemanticsTable.Resolve(sheet, col, row);
                    var tile = Load(T(sheet.Key, col, row));
                    if (semantic == TileSemantic.Unclassified)
                    {
                        Assert.IsNull(tile, $"{sprite.name} is unclassified, so it must not be paintable");
                        continue;
                    }

                    classified++;
                    Assert.IsNotNull(tile, sprite.name);
                    Assert.AreEqual(semantic, tile.Semantic, sprite.name);
                    Assert.AreSame(sprite, tile.sprite, sprite.name);
                }
            }

            Assert.AreEqual(firstRun.Total, classified);
            Assert.Greater(classified, 500, "a useful first set");
        }

        [Test]
        public void AllSixSemanticsAreRepresentedAndTheTotalsMatchTheAssetsOnDisk()
        {
            foreach (var semantic in new[] { TileSemantic.Ground, TileSemantic.Blocked, TileSemantic.Wall, TileSemantic.Prop, TileSemantic.Decor, TileSemantic.Door })
                {
                firstRun.CountBySemantic.TryGetValue(semantic, out var count);
                Assert.Greater(count, 0, semantic.ToString());
            }

            var onDisk = table.Sheets.Sum(s => AssetDatabase.FindAssets("t:SemanticTile", new[] { BarrowsEndTileAuthor.TileFolder(s.Key) }).Length);
            Assert.AreEqual(firstRun.Total, onDisk, "the folders mirror the table exactly");
        }

        [Test]
        public void TheValidatorIsCleanOnEveryGeneratedTile()
        {
            var problems = new System.Collections.Generic.List<string>();
            foreach (var sheet in table.Sheets)
            foreach (var guid in AssetDatabase.FindAssets("t:SemanticTile", new[] { BarrowsEndTileAuthor.TileFolder(sheet.Key) }))
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                problems.AddRange(SemanticTileValidator.Validate(Load(path), path).Select(r => $"{path}: {r.Message}"));
            }

            Assert.IsEmpty(problems.Take(10).ToList());
        }

        [Test]
        public void SortedObjectTilesAllShareOneRuntimeOnlyPrefabWithASpriteRendererAndAStaticSorter()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(BarrowsEndTileAuthor.PrefabPath);
            Assert.IsNotNull(prefab);
            Assert.IsNotNull(prefab.GetComponent<StaticSpriteSorter>());
            Assert.AreEqual(SpriteSortingContract.WorldLayer, prefab.GetComponent<SpriteRenderer>().sortingLayerName);

            foreach (var (key, col, row) in new[] { ("town", 13, 9), ("town", 23, 17), ("town", 16, 7), ("cave", 1, 0) })
            {
                var tile = Load(T(key, col, row));
                Assert.IsTrue(tile.IsSortedObject);
                Assert.AreSame(prefab, tile.gameObject, $"{key}_{col}_{row}");
                Assert.AreNotEqual(0, (int)(tile.flags & TileFlags.InstantiateGameObjectRuntimeOnly));
            }

            foreach (var (key, col, row) in new[] { ("town", 5, 0), ("town", 0, 0), ("cave", 9, 3) })
            {
                var tile = Load(T(key, col, row));
                Assert.IsFalse(tile.IsSortedObject);
                Assert.IsNull(tile.gameObject, $"a flat tile carries no prefab ({key}_{col}_{row})");
            }
        }

        [Test]
        public void DoorTilesShareTheDoorPrefabThatCarriesTheDoorLogic()
        {
            var doorPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(BarrowsEndTileAuthor.DoorPrefabPath);
            Assert.IsNotNull(doorPrefab);
            Assert.IsNotNull(doorPrefab.GetComponent<CRPG.Gameplay.Interactions.Adapters.TileDoor>());
            Assert.IsNotNull(doorPrefab.GetComponent<CRPG.Gameplay.Interactions.Adapters.UseDoorInteractionAdapter>());
            Assert.IsNotNull(doorPrefab.GetComponent<CRPG.Navigation.Grid.GridWalkabilityBlocker>());
            Assert.IsNotNull(doorPrefab.GetComponent<CRPG.Gameplay.Interactions.WorldInteractable>());
            Assert.IsNotNull(doorPrefab.GetComponent<BoxCollider>(), "the router hits a collider to find the interactable");
            Assert.IsNotNull(doorPrefab.GetComponentInChildren<StaticSpriteSorter>(true));

            var wiring = new SerializedObject(doorPrefab.GetComponent<CRPG.Gameplay.Interactions.Adapters.TileDoor>());
            Assert.IsNotNull(wiring.FindProperty("blocker").objectReferenceValue);
            Assert.IsNotNull(wiring.FindProperty("anchor").objectReferenceValue);
            Assert.IsNotNull(wiring.FindProperty("interactable").objectReferenceValue);

            var doors = 0;
            foreach (var guid in AssetDatabase.FindAssets("t:SemanticTile", new[] { BarrowsEndTileAuthor.OutputDir }))
            {
                var tile = Load(AssetDatabase.GUIDToAssetPath(guid));
                if (tile.Semantic == TileSemantic.Door) { doors++; Assert.AreSame(doorPrefab, tile.gameObject, tile.name); }
                else if (tile.IsSortedObject) Assert.AreNotSame(doorPrefab, tile.gameObject, tile.name);
            }

            Assert.Greater(doors, 20);
        }

        [Test]
        public void MovementAndSightFlagsFollowTheSemantic()
        {
            Assert.IsFalse(Load(T("town", 5, 0)).BlocksMovement, "grass");
            Assert.IsTrue(Load(T("town", 0, 0)).BlocksMovement, "water");
            Assert.IsTrue(Load(T("town", 13, 9)).BlocksMovement, "tree");
            Assert.IsFalse(Load(T("town", 16, 7)).BlocksMovement, "a torch is walkable decor");
            Assert.IsFalse(Load(T("town", 32, 1)).BlocksMovement, "a door cell is owned by its blocker, not blocked from the start");
            Assert.IsTrue(Load(T("town", 23, 17)).BlocksSight, "walls block sight");
            Assert.IsFalse(Load(T("town", 13, 9)).BlocksSight, "trees do not");
        }

        // ---------------------------------------------------------------------------------------------
        // Idempotence and pruning
        // ---------------------------------------------------------------------------------------------

        [Test]
        public void AuthoringAgainKeepsEveryGuidAndCreatesOrDeletesNothing()
        {
            var sample = new[] { T("town", 5, 0), T("town", 13, 9), T("cave", 1, 0), BarrowsEndTileAuthor.PrefabPath, BarrowsEndTileAuthor.PalettePath("town"), BarrowsEndTileAuthor.PalettePath("cave") };
            var guids = sample.Select(AssetDatabase.AssetPathToGUID).ToList();

            var second = BarrowsEndTileAuthor.Author();

            CollectionAssert.AreEqual(guids, sample.Select(AssetDatabase.AssetPathToGUID).ToList());
            Assert.AreEqual(0, second.Created);
            Assert.AreEqual(0, second.Deleted);
            Assert.AreEqual(firstRun.Total, second.Updated);
        }

        [Test]
        public void ATileWhoseCellIsNoLongerClassifiedIsDeleted()
        {
            const string json = "{ \"sheets\": [ { \"key\": \"probe\", \"path\": \"Assets/Art/Source/roguelikeSheet_transparent.png\", \"spritePrefix\": \"roguelikeSheet_transparent\", \"rules\": [ " +
                                "{ \"semantic\": \"Ground\", \"cols\": [5, 6], \"rows\": [0, 0], \"note\": \"two grass tiles\" } ] } ] }";
            var wide = "Assets/Content/Tiles/probe_wide.json";
            var narrow = "Assets/Content/Tiles/probe_narrow.json";
            File.WriteAllText(wide, json);
            File.WriteAllText(narrow, json.Replace("[5, 6]", "[5, 5]"));
            AssetDatabase.Refresh();
            try
            {
                BarrowsEndTileAuthor.Author(wide);
                Assert.IsNotNull(Load(T("probe", 5, 0)));
                Assert.IsNotNull(Load(T("probe", 6, 0)));

                var result = BarrowsEndTileAuthor.Author(narrow);

                Assert.AreEqual(1, result.Deleted);
                Assert.IsNotNull(Load(T("probe", 5, 0)));
                Assert.IsNull(Load(T("probe", 6, 0)), "pruned: the folder mirrors the table");
            }
            finally
            {
                AssetDatabase.DeleteAsset(BarrowsEndTileAuthor.TileFolder("probe"));
                AssetDatabase.DeleteAsset(BarrowsEndTileAuthor.PalettePath("probe"));
                AssetDatabase.DeleteAsset(wide);
                AssetDatabase.DeleteAsset(narrow);
                AssetDatabase.Refresh();
            }
        }

        // ---------------------------------------------------------------------------------------------
        // Palettes
        // ---------------------------------------------------------------------------------------------

        [Test]
        public void EachPaletteHoldsEveryClassifiedTileLaidOutLikeTheSheet()
        {
            foreach (var sheet in table.Sheets)
            {
                var palette = AssetDatabase.LoadAssetAtPath<GameObject>(BarrowsEndTileAuthor.PalettePath(sheet.Key));
                Assert.IsNotNull(palette, sheet.Key);
                var tilemap = palette.GetComponentInChildren<Tilemap>();
                var expected = AssetDatabase.FindAssets("t:SemanticTile", new[] { BarrowsEndTileAuthor.TileFolder(sheet.Key) }).Length;

                var painted = 0;
                foreach (var pos in tilemap.cellBounds.allPositionsWithin)
                    if (tilemap.GetTile(pos) != null) painted++;
                Assert.AreEqual(expected, painted, $"{sheet.Key} palette has one cell per tile");

                var maxRow = Enumerable.Range(0, 200).Where(r => Enumerable.Range(0, 60).Any(c => Load(T(sheet.Key, c, r)) != null)).Max();
                var first = sheet.Key == "town" ? (5, 0) : (9, 3);
                var tileAt = tilemap.GetTile<SemanticTile>(new Vector3Int(first.Item1, maxRow - first.Item2, 0));
                Assert.AreSame(Load(T(sheet.Key, first.Item1, first.Item2)), tileAt, "column right, sheet row downward");
            }
        }
    }
}
