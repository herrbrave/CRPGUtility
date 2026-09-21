using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.Tilemaps;

namespace CRPG.Navigation.Grid.Tilemaps
{
    /// <summary>
    /// A stable fingerprint of everything a location bake depends on: the Grid's swizzle and cell size and, for every painted cell on every tilemap under the
    /// Grid, the tilemap, the cell and the tile's semantic (not its name or sprite: swapping one grass tile for another leaves a bake unchanged). Painting,
    /// erasing, moving or re-classifying a tile changes it; placing an ordinary object, or baking again, does not. Deterministic (tilemaps and cells are visited in sorted order) and independent of the machine, so a fingerprint stored at
    /// bake time can be compared with one computed later, including at runtime. The location baker writes it; <see cref="BakeFingerprint"/> checks it.
    /// </summary>
    public static class TileFingerprint
    {
        public static string Compute(UnityEngine.Grid grid)
        {
            var text = new StringBuilder(4096);
            text.Append(grid.cellSwizzle).Append('|').Append(grid.cellSize.x).Append('|').Append(grid.cellSize.y).Append('\n');

            var maps = new List<Tilemap>(grid.GetComponentsInChildren<Tilemap>(true));
            maps.Sort((a, b) => string.CompareOrdinal(PathFrom(grid.transform, a.transform), PathFrom(grid.transform, b.transform)));

            var cells = new List<Vector3Int>();
            foreach (var map in maps)
            {
                text.Append('#').Append(PathFrom(grid.transform, map.transform)).Append('\n');
                cells.Clear();
                foreach (var position in map.cellBounds.allPositionsWithin)
                    if (map.GetTile(position) != null) cells.Add(position);
                cells.Sort((a, b) => a.y != b.y ? a.y.CompareTo(b.y) : a.x != b.x ? a.x.CompareTo(b.x) : a.z.CompareTo(b.z));

                foreach (var position in cells)
                {
                    var tile = map.GetTile(position);
                    var semantic = tile is SemanticTile semanticTile ? (int)semanticTile.Semantic : -1;
                    text.Append(position.x).Append(',').Append(position.y).Append(',').Append(position.z).Append(':').Append(semantic).Append('\n');
                }
            }

            return Hash(text.ToString());
        }

        private static string PathFrom(Transform root, Transform t)
        {
            var parts = new List<string>();
            for (; t != null && t != root; t = t.parent) parts.Add(t.name);
            parts.Reverse();
            return string.Join("/", parts);
        }

        // FNV-1a, 64 bit: tiny, deterministic across platforms (string.GetHashCode is not).
        private static string Hash(string value)
        {
            var hash = 14695981039346656037UL;
            foreach (var c in value)
            {
                hash ^= c;
                hash *= 1099511628211UL;
            }

            return hash.ToString("x16");
        }
    }
}
