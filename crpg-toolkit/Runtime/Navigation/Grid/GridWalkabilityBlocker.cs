using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace CRPG.Navigation.Grid
{
    /// <summary>
    /// Grid-backend <see cref="IWalkabilityBlocker"/> for a door/portcullis: blocks or unblocks a set of cells on
    /// its own scene's grid (reached through a direct reference to that scene's
    /// <see cref="WalkabilityGridProvider"/>, so it can never touch another location's grid).
    /// <para>Unlike a NavMeshObstacle -- whose serialized <c>enabled</c> flag already IS its initial state -- a
    /// grid's blocked-cell set starts empty, so the initial state (<c>blockingAtStart</c>) is pushed explicitly
    /// on first enable. Later enables re-push the CURRENT state rather than resetting it. No cleanup is needed
    /// on unload: each location gets a wholly new grid from its own provider, so nothing leaks between scenes.</para>
    /// </summary>
    public sealed class GridWalkabilityBlocker : MonoBehaviour, IWalkabilityBlocker
    {
        [SerializeField] private WalkabilityGridProvider provider;
        [SerializeField] private List<Vector2Int> cells = new();
        [SerializeField] private bool blockingAtStart = true;

        private bool initialized;
        private bool blocking;

        public bool IsBlocking => initialized ? blocking : blockingAtStart;

        public void Configure(WalkabilityGridProvider gridProvider, IEnumerable<GridCoordinate> blockedCells, bool startBlocking)
        {
            provider = gridProvider;
            cells = blockedCells.Select(c => new Vector2Int(c.X, c.Z)).ToList();
            blockingAtStart = startBlocking;
        }

        public void SetBlocking(bool isBlocking)
        {
            initialized = true;
            blocking = isBlocking;
            Push();
        }

        private void OnEnable()
        {
            if (!initialized)
            {
                initialized = true;
                blocking = blockingAtStart;
            }

            Push();
        }

        private void Push()
        {
            var grid = provider != null ? provider.Grid : null;
            if (grid == null) return;

            foreach (var cell in cells) grid.SetBlocked(new GridCoordinate(cell.x, cell.y), blocking);
        }
    }
}
