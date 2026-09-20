using System.Collections.Generic;
using System.Linq;
using CRPG.Gameplay.Scenes;
using CRPG.Navigation.Grid;
using CRPG.Navigation.Grid.Tilemaps;
using CRPG.Presentation.Sprites;
using UnityEditor;
using UnityEngine;
using UnityEngine.Tilemaps;

namespace CRPG.Editor.Location
{
    public sealed class LocationBakeOptions
    {
        /// <summary>The physics layer of the ground plane (the click router's hittable mask includes it).</summary>
        public int GroundLayer;

        /// <summary>The physics layer of wall boxes: line of sight is tested against it, the router's mask must EXCLUDE it. A package cannot own project layer
        /// names, so the game passes the indices.</summary>
        public int ObstacleLayer;

        /// <summary>Wall boxes straddle the ground plane (centre at ground Y), so a linecast between two characters standing on it crosses them.</summary>
        public float WallHeight = 2f;

        public float GroundThickness = 0.1f;
    }

    public enum BakeSeverity { Info, Warning, Error }

    public readonly struct BakeIssue
    {
        public BakeIssue(BakeSeverity severity, string message, Vector2Int? cell = null)
        {
            Severity = severity;
            Message = message;
            Cell = cell;
        }

        public BakeSeverity Severity { get; }
        public string Message { get; }
        public Vector2Int? Cell { get; }
        public override string ToString() => $"{Severity}: {Message}{(Cell.HasValue ? $" (cell {Cell.Value.x},{Cell.Value.y})" : "")}";
    }

    public sealed class BakeReport
    {
        public readonly List<BakeIssue> Issues = new();
        public bool HasErrors => Issues.Any(i => i.Severity == BakeSeverity.Error);

        /// <summary>True only when the scene was actually written (there were no errors).</summary>
        public bool Baked;

        public RectInt CellBounds;
        public Rect WorldBoundsXZ;
        public int Width, Depth, WalkableCells, BlockedCells, DoorCells, WallBoxes;

        public void Add(BakeSeverity severity, string message, Vector2Int? cell = null) => Issues.Add(new BakeIssue(severity, message, cell));
        public IEnumerable<BakeIssue> Errors => Issues.Where(i => i.Severity == BakeSeverity.Error);
        public IEnumerable<BakeIssue> Warnings => Issues.Where(i => i.Severity == BakeSeverity.Warning);
    }

    /// <summary>
    /// Bakes a painted location (a Grid with Tilemaps of <see cref="SemanticTile"/>s on the XZ plane) into everything gameplay needs: the walkability grid,
    /// a ground collider, merged wall boxes for line of sight, and the camera bounds -- see Docs/Plans/2D-Campaign-Phase3-CameraAndWorld.md (Task 3.3).
    /// <para><b>Fails closed:</b> if anything is wrong (a tile with no meaning, a door nobody can reach, an anchor inside a wall ...) it returns the
    /// report and touches NOTHING, so bad data never becomes a silently wrong grid. On success it replaces only the <c>Baked</c> child, so hand-placed
    /// objects survive, and the output is deterministic: baking twice gives the same result.</para>
    /// <para>Doors need no baking: the Door tile's prefab carries the door logic and <c>TileDoor</c> wires it at runtime.</para>
    /// </summary>
    public static class LocationBaker
    {
        public const string BakedRootName = "Baked";

        private sealed class Cell
        {
            public bool Ground, Blocking, Wall, Door, Decor;
        }

        public static BakeReport Bake(GameObject root, LocationBakeOptions options)
        {
            var report = new BakeReport();
            if (root == null || options == null)
            {
                report.Add(BakeSeverity.Error, "A location root and bake options are required.");
                return report;
            }

            if (options.GroundLayer < 0 || options.GroundLayer > 31 || options.ObstacleLayer < 0 || options.ObstacleLayer > 31 || options.GroundLayer == options.ObstacleLayer)
                report.Add(BakeSeverity.Error, "Ground and obstacle layers must be two different physics layers (0-31).");

            var grid = root.GetComponentInChildren<Grid>(true);
            if (grid == null)
            {
                report.Add(BakeSeverity.Error, "The location has no Grid.");
                return report;
            }

            if (grid.cellSwizzle != GridLayout.CellSwizzle.XZY)
                report.Add(BakeSeverity.Error, $"The Grid's cell swizzle is {grid.cellSwizzle}; a location must use XZY so cells lie on the XZ ground plane.");
            if (grid.transform.rotation != Quaternion.identity || grid.transform.lossyScale != Vector3.one)
                report.Add(BakeSeverity.Error, "The Grid's transform must have no rotation or scale.");
            if (!Mathf.Approximately(grid.cellSize.x, grid.cellSize.y) || grid.cellSize.x <= 0f)
                report.Add(BakeSeverity.Error, $"Grid cells must be square and positive (cell size {grid.cellSize}).");
            if (report.HasErrors) return report;

            var cellSize = grid.cellSize.x;
            var cells = new Dictionary<Vector2Int, Cell>();

            foreach (var tilemap in grid.GetComponentsInChildren<Tilemap>(true))
            {
                if (IsUnderBaked(tilemap.transform, root.transform)) continue;
                if (tilemap.orientation != Tilemap.Orientation.XZ)
                    report.Add(BakeSeverity.Warning, $"Tilemap '{tilemap.name}' orientation is {tilemap.orientation}, not XZ: its sprites will not lie flat.");

                foreach (var position in tilemap.cellBounds.allPositionsWithin)
                {
                    var tile = tilemap.GetTile(position);
                    if (tile == null) continue;

                    var xy = new Vector2Int(position.x, position.y);
                    if (tile is not SemanticTile semantic)
                    {
                        report.Add(BakeSeverity.Error, $"Tilemap '{tilemap.name}' holds '{tile.name}', which is not a SemanticTile, so its meaning is unknown.", xy);
                        continue;
                    }

                    if (!semantic.IsClassified)
                    {
                        report.Add(BakeSeverity.Error, $"Tilemap '{tilemap.name}' holds '{tile.name}', which has no semantic.", xy);
                        continue;
                    }

                    if (!cells.TryGetValue(xy, out var cell)) cells[xy] = cell = new Cell();
                    switch (semantic.Semantic)
                    {
                        case TileSemantic.Ground: cell.Ground = true; break;
                        case TileSemantic.Blocked: cell.Blocking = true; break;
                        case TileSemantic.Wall: cell.Blocking = true; cell.Wall = true; break;
                        case TileSemantic.Prop: cell.Blocking = true; break;
                        case TileSemantic.Decor: cell.Decor = true; break;
                        case TileSemantic.Door: cell.Door = true; break;
                    }
                }
            }

            if (cells.Count == 0)
            {
                report.Add(BakeSeverity.Error, "The location has no painted tiles.");
                return report;
            }

            var minX = cells.Keys.Min(c => c.x);
            var maxX = cells.Keys.Max(c => c.x);
            var minY = cells.Keys.Min(c => c.y);
            var maxY = cells.Keys.Max(c => c.y);
            var width = maxX - minX + 1;
            var depth = maxY - minY + 1;
            var corner = grid.CellToWorld(new Vector3Int(minX, minY, 0));
            var origin = new Vector3(corner.x, corner.y, corner.z);

            report.CellBounds = new RectInt(minX, minY, width, depth);
            report.Width = width;
            report.Depth = depth;
            report.WorldBoundsXZ = new Rect(origin.x, origin.z, width * cellSize, depth * cellSize);

            if (Mathf.Abs(origin.z) > SpriteSortingContract.MaxWorldZ || Mathf.Abs(origin.z + depth * cellSize) > SpriteSortingContract.MaxWorldZ)
                report.Add(BakeSeverity.Error, $"The location spans world Z {origin.z} to {origin.z + depth * cellSize}, outside +-{SpriteSortingContract.MaxWorldZ}: sprite depth sorting would clamp. Move the Grid nearer the origin.");

            bool Walkable(Vector2Int c) => cells.TryGetValue(c, out var cell) && !cell.Blocking && (cell.Ground || cell.Door);

            foreach (var pair in cells)
            {
                if (pair.Value.Decor && !pair.Value.Ground && !pair.Value.Door && !pair.Value.Blocking)
                    report.Add(BakeSeverity.Warning, "A Decor tile floats on a cell with no ground under it.", pair.Key);
                if (pair.Value.Door && pair.Value.Blocking)
                    report.Add(BakeSeverity.Warning, "A Door shares its cell with a wall, prop or water tile, so it is blocked for good.", pair.Key);
            }

            var doorCells = cells.Where(p => p.Value.Door).Select(p => p.Key).ToList();
            foreach (var door in doorCells)
            {
                var approach = false;
                for (var dx = -1; dx <= 1 && !approach; dx++)
                for (var dy = -1; dy <= 1 && !approach; dy++)
                    if ((dx != 0 || dy != 0) && Walkable(new Vector2Int(door.x + dx, door.y + dy)) && !cells[new Vector2Int(door.x + dx, door.y + dy)].Door) approach = true;
                if (!approach) report.Add(BakeSeverity.Error, "A Door has no walkable neighbouring cell, so nobody could ever reach it to open it.", door);
            }

            // Travel anchors must stand on walkable ground.
            var starts = new List<Vector2Int>();
            foreach (var destination in Object.FindObjectsByType<SceneTravelDestination>(FindObjectsInactive.Include, FindObjectsSortMode.None)
                         .Where(d => d.gameObject.scene == root.scene))
            {
                var world = destination.Anchor.position;
                var cell = new Vector2Int(minX + Mathf.FloorToInt((world.x - origin.x) / cellSize), minY + Mathf.FloorToInt((world.z - origin.z) / cellSize));
                if (!Walkable(cell)) report.Add(BakeSeverity.Error, $"Travel destination '{destination.DestinationId}' is not on a walkable cell.", cell);
                else starts.Add(new Vector2Int(cell.x - minX, cell.y - minY));
            }

            if (report.HasErrors) return report;

            var blocked = new List<GridCoordinate>();
            var walkableCount = 0;
            for (var y = 0; y < depth; y++)
            for (var x = 0; x < width; x++)
            {
                if (Walkable(new Vector2Int(minX + x, minY + y))) walkableCount++;
                else blocked.Add(new GridCoordinate(x, y));
            }

            report.WalkableCells = walkableCount;
            report.BlockedCells = blocked.Count;
            report.DoorCells = doorCells.Count;

            if (starts.Count > 0)
            {
                var regions = WalkableRegions.Find(width, depth, (x, y) => Walkable(new Vector2Int(minX + x, minY + y)), starts);
                foreach (var region in regions.Where(r => !r.ContainsStart))
                    report.Add(BakeSeverity.Warning, $"{region.Cells.Count} walkable cell(s) cannot be reached from any travel destination (doors count as open).",
                        new Vector2Int(minX + region.First.x, minY + region.First.y));
            }
            else
            {
                report.Add(BakeSeverity.Info, "No travel destinations in this scene, so reachability was not checked.");
            }

            var walls = WallMerger.Merge(cells.Where(p => p.Value.Wall).Select(p => p.Key));
            report.WallBoxes = walls.Count;

            Write(root, options, origin, cellSize, width, depth, blocked, walls, minX, minY, report);
            report.Baked = true;
            return report;
        }

        private static bool IsUnderBaked(Transform transform, Transform root)
        {
            for (var t = transform; t != null && t != root; t = t.parent)
                if (t.name == BakedRootName && t.parent == root) return true;
            return false;
        }

        private static void Write(GameObject root, LocationBakeOptions options, Vector3 origin, float cellSize, int width, int depth,
            List<GridCoordinate> blocked, List<RectInt> walls, int minX, int minY, BakeReport report)
        {
            var existing = root.transform.Find(BakedRootName);
            if (existing != null) Object.DestroyImmediate(existing.gameObject);

            var baked = new GameObject(BakedRootName);
            baked.transform.SetParent(root.transform, false);

            var gridGo = new GameObject("WalkabilityGrid");
            gridGo.transform.SetParent(baked.transform, false);
            gridGo.AddComponent<WalkabilityGridProvider>().Configure(origin, cellSize, width, depth, blocked);

            var ground = new GameObject("Ground") { layer = options.GroundLayer };
            ground.transform.SetParent(baked.transform, false);
            ground.transform.position = new Vector3(origin.x + width * cellSize * 0.5f, origin.y - options.GroundThickness * 0.5f, origin.z + depth * cellSize * 0.5f);
            ground.AddComponent<BoxCollider>().size = new Vector3(width * cellSize, options.GroundThickness, depth * cellSize);

            var wallsRoot = new GameObject("Walls");
            wallsRoot.transform.SetParent(baked.transform, false);
            foreach (var rect in walls)
            {
                var wall = new GameObject($"Wall {rect.x},{rect.y} {rect.width}x{rect.height}") { layer = options.ObstacleLayer };
                wall.transform.SetParent(wallsRoot.transform, false);
                var min = new Vector3(origin.x + (rect.x - minX) * cellSize, origin.y, origin.z + (rect.y - minY) * cellSize);
                wall.transform.position = min + new Vector3(rect.width * cellSize * 0.5f, 0f, rect.height * cellSize * 0.5f);
                wall.AddComponent<BoxCollider>().size = new Vector3(rect.width * cellSize, options.WallHeight, rect.height * cellSize);
            }

            var content = root.GetComponent<LocationContent>() ?? root.AddComponent<LocationContent>();
            var so = new SerializedObject(content);
            so.FindProperty("hasCameraBounds").boolValue = true;
            so.FindProperty("cameraBoundsXZ").rectValue = report.WorldBoundsXZ;
            so.ApplyModifiedPropertiesWithoutUndo();

            Physics.SyncTransforms(); // auto-sync is off in these projects: make the new colliders visible to queries at once
        }
    }
}
