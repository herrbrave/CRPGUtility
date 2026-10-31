using System;
using UnityEngine;

namespace CRPG.Navigation
{
    /// <summary>
    /// Which walkable cells are watched: a cache of the pointwise sight query over every cell, for the two things that need the whole picture at once (an overlay showing the player where they would be seen, and the AI picking an unwatched route). The sight question is a function the
    /// caller supplies (it asks whether anyone hostile can see this point), so the field knows nothing of rules. It is rebuilt when an observer moves or turns, a light changes or a door opens; <see cref="Version"/> counts rebuilds so a display knows to redraw.
    /// </summary>
    public sealed class VisionField
    {
        private readonly WalkableCells cells;
        private readonly bool[] watched;

        public VisionField(WalkableCells cells)
        {
            this.cells = cells ?? throw new ArgumentNullException(nameof(cells));
            watched = new bool[cells.Count];
        }

        public WalkableCells Cells => cells;
        public int Version { get; private set; }
        public int WatchedCount { get; private set; }

        /// <summary>Recomputes every cell; <paramref name="isWatched"/> gets a point where a character could stand.</summary>
        public void Rebuild(Func<Vector3, bool> isWatched)
        {
            WatchedCount = 0;
            for (var i = 0; i < watched.Length; i++)
            {
                watched[i] = isWatched(cells.PointOf(i));
                if (watched[i]) WatchedCount++;
            }

            Version++;
        }

        public bool IsWatchedCell(int index) => watched[index];

        /// <summary>Whether a world position is in a watched cell; a position off the walkable floor is not watched (nobody stands there).</summary>
        public bool IsWatched(Vector3 world)
        {
            var index = cells.IndexAt(world);
            return index >= 0 && watched[index];
        }
    }
}
