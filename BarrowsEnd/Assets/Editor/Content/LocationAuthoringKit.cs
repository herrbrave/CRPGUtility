using System.Collections.Generic;
using System.Linq;
using System.Reflection;
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
    /// Task 6.0: the general-purpose tile-authoring/travel-point/bake pipeline <see cref="BarrowsEndLocationAuthor"/>
    /// (Task 3.4) established for its own two demo slices, extracted so <c>BarrowsEndTownAuthor</c>/
    /// <c>BarrowsEndCavernsAuthor</c> can build the REAL campaign maps against the exact same pipeline instead of
    /// a hand-duplicated copy -- these helpers used to be private to <see cref="BarrowsEndLocationAuthor"/>, which
    /// itself is refactored to call this shared kit too, so there is one implementation, not two.
    /// </summary>
    internal static class LocationAuthoringKit
    {
        public sealed class Layout
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

        /// <summary>Carves a simple walled rectangular room: a wall ring at the inclusive bounds (four
        /// FillObjects calls forming a hollow rectangle, matching the existing hand-authored convention in
        /// BarrowsEndLocationAuthor's own cave demo), a floor fill for its interior, and exactly one Door tile
        /// replacing one wall cell -- satisfies <c>LocationBaker</c>'s bake-safety rule that a walkable interior
        /// needs a real, reachable Door, not just a Ground-painted gap. <paramref name="doorCell"/> must sit ON
        /// the perimeter (one of the four wall rows/columns) for the door to actually open through the wall.</summary>
        public static void CarveRoom(Layout layout, (string, int, int) wallTile, (string, int, int) floorTile, (string, int, int) doorTile,
            int x0, int y0, int x1, int y1, Vector2Int doorCell)
        {
            layout.FillObjects(wallTile, x0, y0, x1, y0);
            layout.FillObjects(wallTile, x0, y1, x1, y1);
            layout.FillObjects(wallTile, x0, y0, x0, y1);
            layout.FillObjects(wallTile, x1, y0, x1, y1);
            layout.FillGround(floorTile, x0 + 1, y0 + 1, x1 - 1, y1 - 1);
            layout.Objects[doorCell.x, doorCell.y] = doorTile;
        }

        public static Vector2Int Cell(int x, int y) => new(x, y);
        public static Vector3 Centre(Vector2Int cell) => new(cell.x + 0.5f, 0f, cell.y + 0.5f);

        /// <summary>The arch/gate visual every location-to-location travel point uses (originally
        /// BarrowsEndLocationAuthor's own private lookup, shared here since the real Town/Caverns travel points
        /// need the exact same sprite).</summary>
        public static Sprite ArchSprite() => AssetDatabase.LoadAllAssetsAtPath("Assets/Art/Source/roguelikeDungeon_transparent.png").OfType<Sprite>()
            .First(s => s.name == "roguelikeDungeon_transparent_24_4");

        public static SemanticTile Tile((string sheet, int col, int row) t) =>
            AssetDatabase.LoadAssetAtPath<SemanticTile>(BarrowsEndTileAuthor.TilePath(t.sheet, t.col, t.row))
            ?? throw new System.InvalidOperationException($"No generated tile {t.sheet}_{t.col}_{t.row} (is it classified in TileSemantics.json?).");

        public static UnityEngine.SceneManagement.Scene NewLocationScene(string name, Layout layout, out GameObject root, out Grid grid)
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

        public static void AddDestination(GameObject root, string id, Vector2Int cell, Grid grid)
        {
            var go = new GameObject("Destination " + id);
            go.transform.SetParent(root.transform, false);
            go.transform.position = Centre(cell);
            var destination = go.AddComponent<SceneTravelDestination>();
            var so = new SerializedObject(destination);
            so.FindProperty("destinationId").stringValue = id;
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        public static void AddTravelPoint(GameObject root, string name, Vector2Int cell, Vector2Int approach, string targetScenePath, string destinationId, Sprite visual)
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

        /// <summary>Task 6.0: the talkable/vendor-NPC counterpart to <see cref="AddTravelPoint"/> -- same box
        /// collider + visual sprite + approach anchor + <see cref="WorldInteractable"/> shape, wiring a
        /// <see cref="TalkInteractionAdapter"/> instead of a travel one. The caller configures the returned
        /// adapter's own dialogue/panelSettings/store fields afterward.</summary>
        public static (GameObject go, TalkInteractionAdapter adapter) AddTalkPoint(GameObject root, string name, Vector2Int cell, Vector2Int approach, Sprite visual)
        {
            var go = new GameObject(name);
            go.transform.SetParent(root.transform, false);
            go.transform.position = Centre(cell);

            var box = go.AddComponent<BoxCollider>();
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
            var adapter = go.AddComponent<TalkInteractionAdapter>();
            return (go, adapter);
        }

        /// <summary>Task 6.0: sets a private/[SerializeReference]-backed field by reflection -- the established
        /// idiom this whole project's `*Author.cs` scripts and test fixtures already use for content that has
        /// no public setter (e.g. dialogue nodes, `TalkInteractionAdapter`'s own `dialogue`/`panelSettings`
        /// fields), mirrored from `ConsumerDialogueAuthor.cs`'s own precedent (`ConsumerGame/Assets/Editor/
        /// ConsumerDialogueAuthor.cs`) so BarrowsEnd's own NPC/dialogue authoring (this task's placeholder NPCs,
        /// and every real one Task 6.1 adds) doesn't hand-duplicate it per file.</summary>
        public static void SetField(object target, string fieldName, object value)
        {
            var type = target.GetType();
            while (type != null)
            {
                var field = type.GetField(fieldName, BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.DeclaredOnly);
                if (field != null) { field.SetValue(target, value); return; }
                type = type.BaseType;
            }

            throw new System.Exception($"Field '{fieldName}' not found on {target.GetType().Name} or its base types.");
        }

        public static BakeReport BakeAndSave(UnityEngine.SceneManagement.Scene scene, GameObject root, string path)
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
