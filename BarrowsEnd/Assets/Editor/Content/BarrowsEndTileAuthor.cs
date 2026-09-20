using System.Collections.Generic;
using System.IO;
using System.Linq;
using CRPG.Gameplay.Interactions;
using CRPG.Gameplay.Interactions.Adapters;
using CRPG.Navigation.Grid;
using CRPG.Navigation.Grid.Tilemaps;
using CRPG.Presentation.Sprites;
using UnityEditor;
using UnityEditor.Tilemaps;
using UnityEngine;
using UnityEngine.Tilemaps;

namespace BarrowsEnd.Editor
{
    /// <summary>
    /// Task 3.2: generates one <see cref="SemanticTile"/> per CLASSIFIED sprite of the sliced Kenney sheets (see <see cref="TileSemanticsTable"/>), the shared
    /// sorted-object prefab those tiles instantiate at runtime, and one Tile Palette per sheet laid out exactly like the sheet (column to the right, row
    /// downward) so it can be browsed like the art. Idempotent: existing tile assets are updated in place (GUIDs stay), and tile assets for cells that are
    /// no longer classified are deleted, so the folders always mirror the table. A sprite no rule covers gets NO tile: it cannot be painted.
    /// <para>Tile asset names are permanent (<c>&lt;key&gt;_&lt;col&gt;_&lt;row&gt;</c>): painted maps reference them by asset.</para>
    /// </summary>
    public static class BarrowsEndTileAuthor
    {
        public const string OutputDir = "Assets/Content/Tiles";
        public const string PrefabPath = OutputDir + "/SortedTileObject.prefab";
        public const string DoorPrefabPath = OutputDir + "/DoorTileObject.prefab";
        public const string PaletteDir = OutputDir + "/Palettes";

        public static string TileFolder(string key) => $"{OutputDir}/{char.ToUpperInvariant(key[0])}{key.Substring(1)}";
        public static string TilePath(string key, int col, int row) => $"{TileFolder(key)}/{key}_{col}_{row}.asset";
        public static string PalettePath(string key) => $"{PaletteDir}/BarrowsEnd_{char.ToUpperInvariant(key[0])}{key.Substring(1)}.prefab";

        public sealed class Result
        {
            public readonly Dictionary<TileSemantic, int> CountBySemantic = new();
            public int Created, Updated, Deleted;
            public int Total => CountBySemantic.Values.Sum();
        }

        [MenuItem("Barrows End/Content/Author Tiles And Palettes")]
        public static void AuthorMenu()
        {
            var result = Author();
            Debug.Log($"[BarrowsEnd] Tiles: {result.Total} ({result.Created} created, {result.Updated} updated, {result.Deleted} deleted). " +
                      string.Join(", ", result.CountBySemantic.Select(p => $"{p.Key} {p.Value}")));
        }

        public static Result Author(string tablePath = TileSemanticsTable.DefaultPath)
        {
            var table = TileSemanticsTable.Load(tablePath);
            var result = new Result();
            Directory.CreateDirectory(PaletteDir);
            AssetDatabase.Refresh();

            var prefab = EnsureObjectPrefab();
            var doorPrefab = EnsureDoorPrefab();
            var tilesBySheet = new Dictionary<string, List<(int col, int row, SemanticTile tile)>>();

            AssetDatabase.StartAssetEditing();
            try
            {
                foreach (var sheet in table.Sheets)
                {
                    Directory.CreateDirectory(TileFolder(sheet.Key));
                    var wanted = new HashSet<string>();
                    var list = new List<(int, int, SemanticTile)>();

                    foreach (var sprite in AssetDatabase.LoadAllAssetsAtPath(sheet.Path).OfType<Sprite>())
                    {
                        if (!TileSemanticsTable.TryParseCell(sheet, sprite.name, out var col, out var row)) continue;
                        var semantic = TileSemanticsTable.Resolve(sheet, col, row);
                        if (semantic == TileSemantic.Unclassified) continue;

                        var path = TilePath(sheet.Key, col, row);
                        wanted.Add(path);
                        var tile = AssetDatabase.LoadAssetAtPath<SemanticTile>(path);
                        var isNew = tile == null;
                        if (isNew) tile = ScriptableObject.CreateInstance<SemanticTile>();

                        tile.Configure(semantic, sprite, semantic == TileSemantic.Door ? doorPrefab : prefab);
                        if (isNew) { AssetDatabase.CreateAsset(tile, path); result.Created++; }
                        else { EditorUtility.SetDirty(tile); result.Updated++; }

                        result.CountBySemantic.TryGetValue(semantic, out var seen);
                        result.CountBySemantic[semantic] = seen + 1;
                        list.Add((col, row, tile));
                    }

                    // Prune tile assets the table no longer classifies, so the folder mirrors it exactly.
                    foreach (var guid in AssetDatabase.FindAssets("t:SemanticTile", new[] { TileFolder(sheet.Key) }))
                    {
                        var path = AssetDatabase.GUIDToAssetPath(guid);
                        if (wanted.Contains(path)) continue;
                        AssetDatabase.DeleteAsset(path);
                        result.Deleted++;
                    }

                    tilesBySheet[sheet.Key] = list;
                }
            }
            finally
            {
                AssetDatabase.StopAssetEditing();
            }

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            foreach (var sheet in table.Sheets)
            {
                // Re-load by path: tiles created inside the asset-editing batch are only safely referenceable afterwards.
                var reloaded = tilesBySheet[sheet.Key]
                    .Select(t => (t.col, t.row, tile: AssetDatabase.LoadAssetAtPath<SemanticTile>(TilePath(sheet.Key, t.col, t.row))))
                    .ToList();
                BuildPalette(sheet.Key, reloaded);
            }

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            return result;
        }

        private static GameObject EnsureObjectPrefab()
        {
            var existing = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            if (existing != null) return existing;

            var go = new GameObject("SortedTileObject");
            var renderer = go.AddComponent<SpriteRenderer>();
            renderer.sortingLayerName = SpriteSortingContract.WorldLayer;
            go.AddComponent<StaticSpriteSorter>();
            var prefab = PrefabUtility.SaveAsPrefabAsset(go, PrefabPath);
            Object.DestroyImmediate(go);
            return prefab;
        }

        /// <summary>The Door tile's prefab: door logic on the root (collider, blocker, interactable with an anchor child, use-door adapter, TileDoor) and a Visual
        /// child that the adapter hides while the door is open. TileDoor finds the grid and cell at runtime, so the prefab needs no scene references.</summary>
        private static GameObject EnsureDoorPrefab()
        {
            var existing = AssetDatabase.LoadAssetAtPath<GameObject>(DoorPrefabPath);
            if (existing != null) return existing;

            var root = new GameObject("DoorTileObject");
            var visual = new GameObject("Visual");
            visual.transform.SetParent(root.transform, false);
            var renderer = visual.AddComponent<SpriteRenderer>();
            renderer.sortingLayerName = SpriteSortingContract.WorldLayer;
            visual.AddComponent<StaticSpriteSorter>();
            var anchor = new GameObject("Anchor").transform;
            anchor.SetParent(root.transform, false);

            var box = root.AddComponent<BoxCollider>(); // what the click router hits to find the interactable
            box.size = new Vector3(0.9f, 1f, 0.9f);
            box.center = new Vector3(0f, 0.5f, 0f);
            var blocker = root.AddComponent<GridWalkabilityBlocker>();
            var interactable = root.AddComponent<WorldInteractable>();
            interactable.Configure(anchor, 1.5f);
            var adapter = root.AddComponent<UseDoorInteractionAdapter>();
            var door = root.AddComponent<TileDoor>();

            SetObject(adapter, "closedVisual", visual);
            SetObject(door, "blocker", blocker);
            SetObject(door, "anchor", anchor);
            SetObject(door, "interactable", interactable);

            var prefab = PrefabUtility.SaveAsPrefabAsset(root, DoorPrefabPath);
            Object.DestroyImmediate(root);
            return prefab;
        }

        private static void SetObject(Object target, string field, Object value)
        {
            var so = new SerializedObject(target);
            so.FindProperty(field).objectReferenceValue = value;
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void BuildPalette(string key, List<(int col, int row, SemanticTile tile)> tiles)
        {
            var path = PalettePath(key);
            var name = Path.GetFileNameWithoutExtension(path);
            if (AssetDatabase.LoadAssetAtPath<GameObject>(path) == null)
                GridPaletteUtility.CreateNewPalette(PaletteDir, name, GridLayout.CellLayout.Rectangle, GridPalette.CellSizing.Automatic, new Vector3(1f, 1f, 0f), GridLayout.CellSwizzle.XYZ);

            var contents = PrefabUtility.LoadPrefabContents(path);
            try
            {
                var tilemap = contents.GetComponentInChildren<Tilemap>();
                tilemap.ClearAllTiles();
                var maxRow = tiles.Count == 0 ? 0 : tiles.Max(t => t.row);
                // Column to the right, sheet row downward (the palette's +Y is up), so the palette reads like the sheet.
                foreach (var (col, row, tile) in tiles) tilemap.SetTile(new Vector3Int(col, maxRow - row, 0), tile);
                tilemap.CompressBounds();
                PrefabUtility.SaveAsPrefabAsset(contents, path);
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(contents);
            }
        }
    }
}
