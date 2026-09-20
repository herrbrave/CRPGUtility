using CRPG.Navigation.Grid;
using UnityEngine;

namespace CRPG.Gameplay.Interactions.Adapters
{
    /// <summary>
    /// Wires a door that a Tilemap instantiated from a Door tile's prefab. The prefab carries the door logic (a <see cref="GridWalkabilityBlocker"/>, a
    /// <see cref="WorldInteractable"/> with an anchor child and a <see cref="UseDoorInteractionAdapter"/>), but it cannot know WHICH grid it is in or
    /// which cell it stands on: only the instance does. In Start this finds the scene's <see cref="WalkabilityGridProvider"/>, takes the cell under the
    /// door, points the blocker at that cell and closes it, and moves the interaction anchor to the nearest walkable cell beside the door (south first,
    /// then north, west, east, then the diagonals) so a character has somewhere to stand to use it. No approach cell means the door can never be used,
    /// which the location baker already reports as an error; here it is a warning.
    /// </summary>
    [DefaultExecutionOrder(-10)]
    public sealed class TileDoor : MonoBehaviour
    {
        private static readonly Vector2Int[] Neighbours =
        {
            new(0, -1), new(0, 1), new(-1, 0), new(1, 0), new(-1, -1), new(1, -1), new(-1, 1), new(1, 1),
        };

        [SerializeField] private GridWalkabilityBlocker blocker;
        [SerializeField] private Transform anchor;
        [SerializeField] private WorldInteractable interactable;
        [SerializeField] private float interactionRange = 1.5f;

        /// <summary>The cell this door occupies, once wired.</summary>
        public GridCoordinate Cell { get; private set; }

        /// <summary>The walkable cell a character stands on to use the door, once wired; null when there is none.</summary>
        public GridCoordinate? ApproachCell { get; private set; }

        public bool IsWired { get; private set; }

        private void Start() => Wire();

        /// <summary>Wires (or re-wires) the door against the current scene's grid. Safe to call again.</summary>
        public void Wire()
        {
            var provider = WalkabilityGridProvider.FindInScene(gameObject.scene);
            if (provider == null)
            {
                Debug.LogError($"[TileDoor] '{name}' found no WalkabilityGridProvider in its scene, so it cannot block or open a cell.", this);
                return;
            }

            var grid = provider.Grid;
            Cell = grid.ToGrid(transform.position);
            blocker.Configure(provider, new[] { Cell }, startBlocking: true);
            blocker.SetBlocking(true);

            ApproachCell = FindApproach(grid, Cell);
            if (ApproachCell.HasValue)
            {
                anchor.position = grid.ToWorld(ApproachCell.Value);
                interactable.Configure(anchor, interactionRange);
            }
            else
            {
                Debug.LogWarning($"[TileDoor] '{name}' at cell {Cell.X},{Cell.Z} has no walkable neighbouring cell, so nobody can reach it to use it.", this);
            }

            IsWired = true;
        }

        private static GridCoordinate? FindApproach(WalkabilityGrid grid, GridCoordinate door)
        {
            foreach (var offset in Neighbours)
            {
                var candidate = new GridCoordinate(door.X + offset.x, door.Z + offset.y);
                if (grid.IsWalkable(candidate)) return candidate;
            }

            return null;
        }
    }
}
