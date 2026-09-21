using System.Collections.Generic;
using System.IO;
using System.Linq;
using CRPG.Editor.Location;
using CRPG.Gameplay.Interactions;
using CRPG.Gameplay.Interactions.Adapters;
using CRPG.Gameplay.Scenes;
using CRPG.Navigation.Grid.Tilemaps;
using CRPG.Presentation.Sprites;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Tilemaps;

namespace BarrowsEnd.Editor
{
    /// <summary>
    /// Task 3.4: authors the two demo locations the Phase 3 shell travels between -- a town slice and a cave slice -- from code, using the REAL generated
    /// semantic tiles, then bakes each with <see cref="LocationBaker"/> (it throws if the bake is refused, so a broken map can never be saved). Each has
    /// arrival <see cref="SceneTravelDestination"/> anchors and one travel point leading to the other. The real campaign maps are painted by hand in
    /// Phase 6 with the same pipeline; these exist to prove camera, world authoring, travel and save/load end to end.
    /// </summary>
    public static class BarrowsEndLocationAuthor
    {
        public const string Folder = "Assets/Scenes/Locations";
        public const string TownPath = Folder + "/Town_Slice.unity";
        public const string CavePath = Folder + "/Cave_Slice.unity";

        public const string TownStart = "start";
        public const string TownFromCave = "from-cave";
        public const string CaveFromTown = "from-town";

        public const int TownWidth = 30, TownDepth = 20;
        public const int CaveWidth = 24, CaveDepth = 16;

        // Tiles by sheet, column, row (see Docs/TileCuration/*.png).
        private static readonly (string sheet, int col, int row) Grass = ("town", 5, 0), Dirt = ("town", 6, 0), Stone = ("town", 7, 0), WoodFloor = ("town", 0, 25),
            Water = ("town", 0, 0), WallTile = ("town", 21, 17), DoorTile = ("town", 32, 1), TreeA = ("town", 13, 9), TreeB = ("town", 15, 9), Barrel = ("town", 23, 0),
            Fence = ("town", 48, 23), CaveFloor = ("cave", 8, 2), CaveDirt = ("cave", 8, 5), CaveWall = ("cave", 9, 0), Rock = ("cave", 0, 0), RockB = ("cave", 1, 0),
            CaveWater = ("cave", 0, 10), Mushrooms = ("cave", 0, 3), Bones = ("cave", 0, 2);

        /// <summary>Authors the demo locations that do not exist yet. An existing location scene is NEVER overwritten (it may hold hand-painted changes): use
        /// <see cref="ResetAll"/> to regenerate from the code layouts on purpose.</summary>
        [MenuItem("Barrows End/Content/Author Demo Locations")]
        public static void AuthorAll() => Author(overwrite: false);

        [MenuItem("Barrows End/Content/Reset Demo Locations (OVERWRITES hand edits)")]
        public static void ResetAll()
        {
            if (!Application.isBatchMode && !EditorUtility.DisplayDialog("Reset demo locations?",
                    "This regenerates Town_Slice and Cave_Slice from the code layouts and DISCARDS anything painted or placed by hand in them.", "Reset", "Cancel")) return;
            Author(overwrite: true);
        }

        public static void Author(bool overwrite)
        {
            BarrowsEndTileAuthor.Author();
            Directory.CreateDirectory(Folder);
            AssetDatabase.Refresh();

            var authored = new List<string>();
            if (overwrite || !File.Exists(TownPath)) { AuthorTown(); authored.Add("town"); }
            if (overwrite || !File.Exists(CavePath)) { AuthorCave(); authored.Add("cave"); }
            Debug.Log(authored.Count == 0
                ? "[BarrowsEnd] Demo locations already exist: left untouched (use 'Reset Demo Locations' to regenerate)."
                : $"[BarrowsEnd] Demo locations authored: {string.Join(", ", authored)}.");
        }

        private sealed class Layout
        {
            public readonly int Width, Depth;
            public readonly (string sheet, int col, int row)?[,] Ground, Objects;

            public Layout(int width, int depth)
            {
                Width = width;
                Depth = depth;
                Ground = new (string, int, int)?[width, depth];
                Objects = new (string, int, int)?[width, depth];
            }

            public void FillGround((string, int, int) tile, int x0, int y0, int x1, int y1)
            {
                for (var x = x0; x <= x1; x++)
                for (var y = y0; y <= y1; y++)
                    Ground[x, y] = tile;
            }

            public void FillObjects((string, int, int) tile, int x0, int y0, int x1, int y1)
            {
                for (var x = x0; x <= x1; x++)
                for (var y = y0; y <= y1; y++)
                    Objects[x, y] = tile;
            }
        }

        public static BakeReport AuthorTown()
        {
            var l = new Layout(TownWidth, TownDepth);
            l.FillGround(Grass, 0, 0, TownWidth - 1, TownDepth - 1);
            l.FillGround(Dirt, 13, 0, 15, TownDepth - 1);                 // the north-south road
            l.FillGround(Stone, 9, 5, 19, 9);                             // a paved square across it
            l.FillGround(Water, 22, 11, 26, 14);                          // a pond
            l.FillGround(WoodFloor, 4, 12, 9, 15);                        // a house floor

            l.FillObjects(TreeA, 0, 0, 0, TownDepth - 1);                 // tree border
            l.FillObjects(TreeB, TownWidth - 1, 0, TownWidth - 1, TownDepth - 1);
            l.FillObjects(TreeA, 1, TownDepth - 1, 12, TownDepth - 1);
            l.FillObjects(TreeB, 16, TownDepth - 1, TownWidth - 2, TownDepth - 1);
            l.FillObjects(TreeB, 1, 0, 12, 0);
            l.FillObjects(TreeA, 16, 0, TownWidth - 2, 0);
            l.FillObjects(WallTile, 3, 11, 10, 16);                       // the house: walls all round ...
            l.Objects[6, 11] = DoorTile;                                  // ... with a door in the south wall
            for (var x = 4; x <= 9; x++)
            for (var y = 12; y <= 15; y++)
                l.Objects[x, y] = null;                                   // ... and an empty interior
            l.FillObjects(Fence, 1, 3, 11, 3);                            // a palisade with a gap for the road
            l.FillObjects(Fence, 17, 3, TownWidth - 2, 3);
            l.Objects[12, 8] = Barrel;
            l.Objects[20, 6] = Barrel;
            l.Objects[24, 8] = TreeA;
            l.Objects[19, 16] = TreeB;
            l.Objects[5, 5] = TreeA;

            var scene = NewLocationScene("Town", l, out var root, out var grid);
            AddDestination(root, TownStart, Cell(14, 2), grid);
            AddDestination(root, TownFromCave, Cell(14, 16), grid);
            AddTravelPoint(root, "TravelToCave", Cell(14, TownDepth - 1), Cell(14, TownDepth - 2), CavePath, CaveFromTown, ArchSprite());
            return BakeAndSave(scene, root, TownPath);
        }

        public static BakeReport AuthorCave()
        {
            var l = new Layout(CaveWidth, CaveDepth);
            l.FillGround(CaveFloor, 0, 0, CaveWidth - 1, CaveDepth - 1);
            l.FillGround(CaveDirt, 9, 0, 14, 9);                          // a dirt track from the exit
            l.FillGround(CaveWater, 16, 8, 20, 11);                       // an underground pool

            l.FillObjects(CaveWall, 0, 0, CaveWidth - 1, 0);              // wall all round, with a gap for the exit
            l.FillObjects(CaveWall, 0, CaveDepth - 1, CaveWidth - 1, CaveDepth - 1);
            l.FillObjects(CaveWall, 0, 0, 0, CaveDepth - 1);
            l.FillObjects(CaveWall, CaveWidth - 1, 0, CaveWidth - 1, CaveDepth - 1);
            l.Objects[12, 0] = null;
            l.Objects[3, 4] = Rock; l.Objects[4, 4] = RockB; l.Objects[3, 5] = RockB;
            l.Objects[18, 3] = Rock; l.Objects[19, 3] = RockB;
            l.Objects[7, 11] = Rock; l.Objects[8, 12] = RockB;
            l.Objects[5, 9] = Mushrooms; l.Objects[10, 13] = Mushrooms; l.Objects[20, 5] = Bones;

            var scene = NewLocationScene("Cave", l, out var root, out var grid);
            AddDestination(root, CaveFromTown, Cell(12, 3), grid);
            AddTravelPoint(root, "TravelToTown", Cell(12, 0), Cell(12, 1), TownPath, TownFromCave, ArchSprite());
            return BakeAndSave(scene, root, CavePath);
        }

        private static Vector2Int Cell(int x, int y) => new(x, y);

        private static Sprite ArchSprite() => AssetDatabase.LoadAllAssetsAtPath("Assets/Art/Source/roguelikeDungeon_transparent.png").OfType<Sprite>()
            .First(s => s.name == "roguelikeDungeon_transparent_24_4");

        private static SemanticTile Tile((string sheet, int col, int row) t) =>
            AssetDatabase.LoadAssetAtPath<SemanticTile>(BarrowsEndTileAuthor.TilePath(t.sheet, t.col, t.row))
            ?? throw new System.InvalidOperationException($"No generated tile {t.sheet}_{t.col}_{t.row} (is it classified in TileSemantics.json?).");

        private static UnityEngine.SceneManagement.Scene NewLocationScene(string name, Layout layout, out GameObject root, out Grid grid)
        {
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            root = new GameObject(name);
            var gridGo = new GameObject("Grid");
            gridGo.transform.SetParent(root.transform, false);
            grid = gridGo.AddComponent<Grid>();
            grid.cellSwizzle = GridLayout.CellSwizzle.XZY;

            Tilemap NewMap(string mapName, string sortingLayer)
            {
                var go = new GameObject(mapName);
                go.transform.SetParent(gridGo.transform, false);
                var map = go.AddComponent<Tilemap>();
                map.orientation = Tilemap.Orientation.XZ;
                var renderer = go.AddComponent<TilemapRenderer>();
                renderer.sortingLayerName = sortingLayer;
                renderer.sortingOrder = 0;
                return map;
            }

            var ground = NewMap("Ground", SpriteSortingContract.GroundLayer);
            var objects = NewMap("Objects", SpriteSortingContract.WorldLayer);
            var cache = new Dictionary<(string, int, int), SemanticTile>();
            SemanticTile Cached((string sheet, int col, int row) t) => cache.TryGetValue(t, out var tile) ? tile : cache[t] = Tile(t);

            for (var x = 0; x < layout.Width; x++)
            for (var y = 0; y < layout.Depth; y++)
            {
                if (layout.Ground[x, y].HasValue) ground.SetTile(new Vector3Int(x, y, 0), Cached(layout.Ground[x, y].Value));
                if (layout.Objects[x, y].HasValue) objects.SetTile(new Vector3Int(x, y, 0), Cached(layout.Objects[x, y].Value));
            }

            return scene;
        }

        private static Vector3 Centre(Vector2Int cell) => new(cell.x + 0.5f, 0f, cell.y + 0.5f);

        private static void AddDestination(GameObject root, string id, Vector2Int cell, Grid grid)
        {
            var go = new GameObject("Destination " + id);
            go.transform.SetParent(root.transform, false);
            go.transform.position = Centre(cell);
            var destination = go.AddComponent<SceneTravelDestination>();
            var so = new SerializedObject(destination);
            so.FindProperty("destinationId").stringValue = id;
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void AddTravelPoint(GameObject root, string name, Vector2Int cell, Vector2Int approach, string targetScenePath, string destinationId, Sprite visual)
        {
            var go = new GameObject(name);
            go.transform.SetParent(root.transform, false);
            go.transform.position = Centre(cell);

            var box = go.AddComponent<BoxCollider>(); // what the click router hits to find the interactable
            box.size = new Vector3(0.9f, 1f, 0.9f);
            box.center = new Vector3(0f, 0.5f, 0f);

            var visualGo = new GameObject("Visual");
            visualGo.transform.SetParent(go.transform, false);
            visualGo.transform.localPosition = new Vector3(0f, 0.02f, 0f);
            visualGo.transform.rotation = Quaternion.Euler(90f, 0f, 0f);
            var renderer = visualGo.AddComponent<SpriteRenderer>();
            renderer.sprite = visual;
            renderer.sortingLayerName = SpriteSortingContract.WorldLayer;
            visualGo.AddComponent<StaticSpriteSorter>();

            var anchor = new GameObject("Anchor").transform;
            anchor.SetParent(go.transform, false);
            anchor.position = Centre(approach);

            var interactable = go.AddComponent<WorldInteractable>();
            interactable.Configure(anchor, 1.5f);
            var adapter = go.AddComponent<TravelInteractionAdapter>();
            var so = new SerializedObject(adapter);
            so.FindProperty("targetScenePath").stringValue = targetScenePath;
            so.FindProperty("destinationId").stringValue = destinationId;
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        private static BakeReport BakeAndSave(UnityEngine.SceneManagement.Scene scene, GameObject root, string path)
        {
            var report = LocationBaker.Bake(root, BarrowsEndLocationBake.Options());
            if (!report.Baked)
                throw new System.InvalidOperationException($"{path} was not baked:\n{string.Join("\n", report.Errors)}");
            foreach (var warning in report.Warnings) Debug.LogWarning($"[BarrowsEnd] {path}: {warning}");

            EditorSceneManager.SaveScene(scene, path);
            return report;
        }
    }
}
