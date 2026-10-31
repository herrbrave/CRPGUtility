using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

namespace CRPG.Navigation
{
    /// <summary>
    /// The walkable floor of an area sampled into square cells: one point on the navmesh per cell that has any, in a fixed grid so a world position finds its cell in constant time. It is the cache that things which need to ask a question of "every place someone could stand"
    /// (which of them are watched, where a character could hide) build on; the rules themselves always ask the pointwise query, never this. Build it once per location load (and again when the navmesh is rebaked); the sampler is a plain function so tests need no scene.
    /// </summary>
    public sealed class WalkableCells
    {
        private readonly Vector3[] points;
        private readonly int[] cellAt;   // grid slot -> index into points, or -1 for a slot with no walkable floor
        private readonly Vector2 min;

        private WalkableCells(Rect area, float cellSize, int columns, int rows, Vector3[] points, int[] cellAt)
        {
            Area = area; CellSize = cellSize; Columns = columns; Rows = rows; this.points = points; this.cellAt = cellAt; min = area.min;
        }

        public Rect Area { get; }
        public float CellSize { get; }
        public int Columns { get; }
        public int Rows { get; }
        public int Count => points.Length;
        public Vector3 PointOf(int index) => points[index];
        public IReadOnlyList<Vector3> Points => points;

        /// <summary>The default sampler: the nearest navmesh point within just over half a cell of the cell centre, or null when the cell is off the mesh.</summary>
        public static Func<Vector3, Vector3?> NavMeshSampler(float cellSize, float floorY) => centre =>
            NavMesh.SamplePosition(new Vector3(centre.x, floorY, centre.z), out var hit, cellSize * 0.75f, NavMesh.AllAreas) ? hit.position : (Vector3?)null;

        /// <param name="area">The ground rectangle to cover (x, z).</param>
        /// <param name="sampler">Gets the cell centre (at floor height) and returns where a character could stand there, or null when nobody could.</param>
        public static WalkableCells Build(Rect area, float cellSize, float floorY, Func<Vector3, Vector3?> sampler)
        {
            if (cellSize <= 0f) throw new ArgumentOutOfRangeException(nameof(cellSize));
            var columns = Mathf.Max(1, Mathf.CeilToInt(area.width / cellSize)); var rows = Mathf.Max(1, Mathf.CeilToInt(area.height / cellSize));
            var found = new List<Vector3>(); var slots = new int[columns * rows];
            for (var row = 0; row < rows; row++)
            {
                for (var column = 0; column < columns; column++)
                {
                    var centre = new Vector3(area.xMin + (column + 0.5f) * cellSize, floorY, area.yMin + (row + 0.5f) * cellSize);
                    var point = sampler(centre);
                    slots[row * columns + column] = point.HasValue ? found.Count : -1;
                    if (point.HasValue) found.Add(point.Value);
                }
            }

            return new WalkableCells(area, cellSize, columns, rows, found.ToArray(), slots);
        }

        /// <summary>The cell holding a world position, or -1 when it is outside the area or on a slot with no floor.</summary>
        public int IndexAt(Vector3 world)
        {
            var column = Mathf.FloorToInt((world.x - min.x) / CellSize); var row = Mathf.FloorToInt((world.z - min.y) / CellSize);
            if (column < 0 || row < 0 || column >= Columns || row >= Rows) return -1;
            return cellAt[row * Columns + column];
        }
    }
}
