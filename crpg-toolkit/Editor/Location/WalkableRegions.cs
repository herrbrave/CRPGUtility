using System;
using System.Collections.Generic;
using UnityEngine;

namespace CRPG.Editor.Location
{
    /// <summary>
    /// Splits a walkable grid into connected regions using the SAME movement rule as <c>GridPathfindingService</c>: eight neighbours, and a diagonal
    /// step is only legal when both orthogonal cells beside it are walkable (no corner cutting). Pure logic over a walkability callback.
    /// </summary>
    public static class WalkableRegions
    {
        public sealed class Region
        {
            public readonly List<Vector2Int> Cells = new();
            public bool ContainsStart;
            public Vector2Int First => Cells[0];
        }

        private static readonly Vector2Int[] Steps =
        {
            new(1, 0), new(-1, 0), new(0, 1), new(0, -1), new(1, 1), new(1, -1), new(-1, 1), new(-1, -1),
        };

        public static List<Region> Find(int width, int depth, Func<int, int, bool> walkable, IEnumerable<Vector2Int> starts)
        {
            var startSet = new HashSet<Vector2Int>(starts ?? Array.Empty<Vector2Int>());
            var seen = new bool[width * depth];
            var regions = new List<Region>();

            bool Ok(int x, int y) => x >= 0 && x < width && y >= 0 && y < depth && walkable(x, y);

            for (var y = 0; y < depth; y++)
            for (var x = 0; x < width; x++)
            {
                if (seen[y * width + x] || !Ok(x, y)) continue;

                var region = new Region();
                var queue = new Queue<Vector2Int>();
                queue.Enqueue(new Vector2Int(x, y));
                seen[y * width + x] = true;

                while (queue.Count > 0)
                {
                    var cell = queue.Dequeue();
                    region.Cells.Add(cell);
                    if (startSet.Contains(cell)) region.ContainsStart = true;

                    foreach (var step in Steps)
                    {
                        var nx = cell.x + step.x;
                        var ny = cell.y + step.y;
                        if (!Ok(nx, ny) || seen[ny * width + nx]) continue;
                        if (step.x != 0 && step.y != 0 && (!Ok(cell.x + step.x, cell.y) || !Ok(cell.x, cell.y + step.y))) continue; // no corner cutting
                        seen[ny * width + nx] = true;
                        queue.Enqueue(new Vector2Int(nx, ny));
                    }
                }

                regions.Add(region);
            }

            return regions;
        }
    }
}
