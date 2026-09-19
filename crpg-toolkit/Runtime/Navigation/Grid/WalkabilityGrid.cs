using System;
using System.Collections.Generic;
using CRPG.Core;
using UnityEngine;

namespace CRPG.Navigation.Grid
{
    /// <summary>
    /// One location's walkable-cell map plus which character stands in which cell. Plain C# (not a
    /// ScriptableObject) -- real content bakes one of these from a scene's collider layer (a later task).
    /// <para>Two independent kinds of "not available": a cell can be <em>blocked</em> (a wall, a closed door --
    /// static-ish terrain that changes rarely and is what pathfinding routes around) or <em>occupied</em> (a
    /// character stands there -- dynamic, and NOT part of pathfinding; the mover enforces it at move time).
    /// They are kept separate deliberately so a door toggling and a character stepping into a cell can never
    /// interfere with each other.</para>
    /// <para>Occupancy lives on the grid object (rather than a global registry) so it is automatically scoped
    /// to one location: a new scene gets a new grid, and two additively-loaded scenes never share a table.</para>
    /// </summary>
    public sealed class WalkabilityGrid
    {
        private readonly bool[] blocked;
        private readonly Dictionary<GridCoordinate, RuntimeEntityId> occupants = new();

        /// <param name="origin">World position of the grid's minimum corner; its Y is the ground height every
        /// cell centre uses (this campaign has no multi-elevation terrain).</param>
        public WalkabilityGrid(Vector3 origin, float cellSize, int width, int depth, IEnumerable<GridCoordinate> blockedCells = null)
        {
            if (cellSize <= 0f) throw new ArgumentOutOfRangeException(nameof(cellSize), "Cell size must be positive.");
            if (width <= 0) throw new ArgumentOutOfRangeException(nameof(width));
            if (depth <= 0) throw new ArgumentOutOfRangeException(nameof(depth));

            Origin = origin;
            CellSize = cellSize;
            Width = width;
            Depth = depth;
            blocked = new bool[width * depth];

            if (blockedCells == null) return;
            foreach (var cell in blockedCells)
            {
                if (!IsInBounds(cell)) throw new ArgumentOutOfRangeException(nameof(blockedCells), $"Blocked cell {cell} is outside the {width}x{depth} grid.");
                blocked[IndexOf(cell)] = true;
            }
        }

        public Vector3 Origin { get; }
        public float CellSize { get; }
        public int Width { get; }
        public int Depth { get; }

        /// <summary>The ground height of every cell centre.</summary>
        public float GroundY => Origin.y;

        public int CellCount => Width * Depth;

        internal int IndexOf(GridCoordinate cell) => cell.Z * Width + cell.X;

        internal GridCoordinate FromIndex(int index) => new(index % Width, index / Width);

        /// <summary>The cell containing <paramref name="worldPosition"/> (X/Z only). May be out of bounds --
        /// check <see cref="IsInBounds"/>.</summary>
        public GridCoordinate ToGrid(Vector3 worldPosition) => new(
            Mathf.FloorToInt((worldPosition.x - Origin.x) / CellSize),
            Mathf.FloorToInt((worldPosition.z - Origin.z) / CellSize));

        /// <summary>World position of a cell's centre, at <see cref="GroundY"/>.</summary>
        public Vector3 ToWorld(GridCoordinate cell) => new(
            Origin.x + (cell.X + 0.5f) * CellSize,
            Origin.y,
            Origin.z + (cell.Z + 0.5f) * CellSize);

        public bool IsInBounds(GridCoordinate cell) => cell.X >= 0 && cell.X < Width && cell.Z >= 0 && cell.Z < Depth;

        /// <summary>False if out of bounds or blocked. Says nothing about occupancy.</summary>
        public bool IsWalkable(GridCoordinate cell) => IsInBounds(cell) && !blocked[IndexOf(cell)];

        public bool IsWalkableWorld(Vector3 worldPosition) => IsWalkable(ToGrid(worldPosition));

        /// <summary>Blocks or unblocks a cell (the door/portcullis seam). Out-of-bounds cells are ignored.</summary>
        public void SetBlocked(GridCoordinate cell, bool isBlocked)
        {
            if (IsInBounds(cell)) blocked[IndexOf(cell)] = isBlocked;
        }

        /// <summary>Claims a cell for <paramref name="occupant"/>. False if another entity already holds it or the
        /// cell is out of bounds; true (idempotent) if the same entity already holds it.</summary>
        public bool TryOccupy(GridCoordinate cell, RuntimeEntityId occupant)
        {
            if (!IsInBounds(cell)) return false;
            if (occupants.TryGetValue(cell, out var current)) return current == occupant;

            occupants[cell] = occupant;
            return true;
        }

        /// <summary>Releases a cell, but only if <paramref name="occupant"/> is the one holding it -- a stale
        /// release from a character that has since been displaced can never evict the current holder.</summary>
        public void Release(GridCoordinate cell, RuntimeEntityId occupant)
        {
            if (occupants.TryGetValue(cell, out var current) && current == occupant) occupants.Remove(cell);
        }

        public bool IsOccupied(GridCoordinate cell, out RuntimeEntityId occupant) => occupants.TryGetValue(cell, out occupant);

        /// <summary>Adds every cell currently held by anyone other than <paramref name="except"/> to
        /// <paramref name="into"/> -- what a mover re-planning around its neighbours treats as temporarily blocked.</summary>
        public void CollectOccupiedCells(RuntimeEntityId except, ISet<GridCoordinate> into)
        {
            foreach (var entry in occupants)
            {
                if (entry.Value != except) into.Add(entry.Key);
            }
        }
    }
}
