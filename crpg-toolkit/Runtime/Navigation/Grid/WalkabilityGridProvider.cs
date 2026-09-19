using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace CRPG.Navigation.Grid
{
    /// <summary>
    /// One per location scene: owns that scene's <see cref="WalkabilityGrid"/> as serialized data, so the grid
    /// loads (and unloads) with the scene. The persistent Core-scene <see cref="GridPathfindingService"/> is
    /// re-pointed at <see cref="Grid"/> whenever this location becomes the active one (SceneTravelCoordinator
    /// does this on travel; a composition root does it once for the initial scene via <see cref="FindInScene"/>).
    /// <para>Blocked cells are stored as <see cref="Vector2Int"/> (x, z). An Editor baking tool that derives them
    /// from a scene's collider layer is a later task; until then, author the fields directly or call
    /// <see cref="Configure"/>. The grid is built lazily, once -- configure before first use.</para>
    /// </summary>
    public sealed class WalkabilityGridProvider : MonoBehaviour
    {
        [SerializeField] private Vector3 origin;
        [SerializeField] private float cellSize = 1f;
        [SerializeField] private int width = 1;
        [SerializeField] private int depth = 1;
        [SerializeField] private List<Vector2Int> blockedCells = new();

        private WalkabilityGrid grid;

        /// <summary>The scene's grid. Throws (via <see cref="WalkabilityGrid"/>'s own validation) if the serialized
        /// data is invalid -- a loud failure beats a silently wrong map.</summary>
        public WalkabilityGrid Grid => grid ??= new WalkabilityGrid(
            origin, cellSize, width, depth, blockedCells.Select(c => new GridCoordinate(c.x, c.y)));

        public void Configure(Vector3 gridOrigin, float gridCellSize, int gridWidth, int gridDepth, IEnumerable<GridCoordinate> blocked = null)
        {
            origin = gridOrigin;
            cellSize = gridCellSize;
            width = gridWidth;
            depth = gridDepth;
            blockedCells = blocked == null ? new List<Vector2Int>() : blocked.Select(c => new Vector2Int(c.X, c.Z)).ToList();
            grid = null;
        }

        /// <summary>The provider that belongs to <paramref name="scene"/>, or null. Scene-scoped on purpose: two
        /// location scenes are briefly loaded together during travel, and each must resolve only its own grid.</summary>
        public static WalkabilityGridProvider FindInScene(Scene scene) =>
            FindObjectsByType<WalkabilityGridProvider>(FindObjectsInactive.Include, FindObjectsSortMode.None)
                .FirstOrDefault(p => p.gameObject.scene == scene);
    }
}
