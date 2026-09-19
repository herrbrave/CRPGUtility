using System;

namespace CRPG.Navigation.Grid
{
    /// <summary>A cell on a <see cref="WalkabilityGrid"/>. <see cref="Z"/> maps to world Z (the ground plane),
    /// never world Y (elevation) -- the same X/Z ground-plane convention every scene in this codebase uses.</summary>
    public readonly struct GridCoordinate : IEquatable<GridCoordinate>
    {
        public GridCoordinate(int x, int z)
        {
            X = x;
            Z = z;
        }

        public int X { get; }
        public int Z { get; }

        /// <summary>The 8 neighbour offsets (cardinals first, then diagonals).</summary>
        public static readonly (int dx, int dz)[] NeighbourOffsets =
        {
            (1, 0), (-1, 0), (0, 1), (0, -1),
            (1, 1), (1, -1), (-1, 1), (-1, -1),
        };

        public bool Equals(GridCoordinate other) => X == other.X && Z == other.Z;
        public override bool Equals(object obj) => obj is GridCoordinate other && Equals(other);
        public override int GetHashCode() => unchecked((X * 397) ^ Z);
        public override string ToString() => $"({X}, {Z})";

        public static bool operator ==(GridCoordinate a, GridCoordinate b) => a.Equals(b);
        public static bool operator !=(GridCoordinate a, GridCoordinate b) => !a.Equals(b);
    }
}
