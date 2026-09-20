using System.Collections.Generic;
using UnityEngine;

namespace CRPG.Editor.Location
{
    /// <summary>
    /// Covers a set of grid cells with non-overlapping rectangles, so a long wall becomes a handful of box colliders instead of one per cell.
    /// Deterministic: it scans row by row from the lowest row, and each not-yet-covered cell grows to the widest run on its row and then as tall as
    /// every row above still holds that whole run. The cover is exact (every cell in exactly one rectangle) though not guaranteed minimal for odd shapes.
    /// </summary>
    public static class WallMerger
    {
        public static List<RectInt> Merge(IEnumerable<Vector2Int> cells)
        {
            var remaining = new HashSet<Vector2Int>(cells);
            var ordered = new List<Vector2Int>(remaining);
            ordered.Sort((a, b) => a.y != b.y ? a.y.CompareTo(b.y) : a.x.CompareTo(b.x));

            var rectangles = new List<RectInt>();
            foreach (var start in ordered)
            {
                if (!remaining.Contains(start)) continue;

                var width = 1;
                while (remaining.Contains(new Vector2Int(start.x + width, start.y))) width++;

                var height = 1;
                while (RowIsFull(remaining, start.x, start.y + height, width)) height++;

                for (var dy = 0; dy < height; dy++)
                for (var dx = 0; dx < width; dx++)
                    remaining.Remove(new Vector2Int(start.x + dx, start.y + dy));

                rectangles.Add(new RectInt(start.x, start.y, width, height));
            }

            return rectangles;
        }

        private static bool RowIsFull(HashSet<Vector2Int> cells, int x, int y, int width)
        {
            for (var dx = 0; dx < width; dx++)
                if (!cells.Contains(new Vector2Int(x + dx, y))) return false;
            return true;
        }
    }
}
