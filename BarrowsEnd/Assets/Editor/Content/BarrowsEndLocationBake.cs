using CRPG.Editor.Location;
using UnityEditor;
using UnityEngine;

namespace BarrowsEnd.Editor
{
    /// <summary>
    /// Task 3.3: bakes the painted location under the current selection (or the first Grid in the open scene) into its walkability grid, ground plane, wall
    /// boxes and camera bounds, with Barrow's End's physics layers. See <see cref="LocationBaker"/>.
    /// </summary>
    public static class BarrowsEndLocationBake
    {
        public static LocationBakeOptions Options() => new()
        {
            GroundLayer = BarrowsEndLayers.Ground,
            ObstacleLayer = BarrowsEndLayers.WorldObstacle,
        };

        [MenuItem("Barrows End/Location/Bake Selected Location")]
        public static void BakeSelected()
        {
            var root = FindLocationRoot(Selection.activeGameObject);
            if (root == null)
            {
                Debug.LogError("[BarrowsEnd] Select a location root (or something inside one) that contains a Grid, then bake again.");
                return;
            }

            var report = LocationBaker.Bake(root, Options());
            foreach (var issue in report.Issues)
            {
                var text = $"[BarrowsEnd] {root.name}: {issue}";
                if (issue.Severity == BakeSeverity.Error) Debug.LogError(text, root);
                else if (issue.Severity == BakeSeverity.Warning) Debug.LogWarning(text, root);
                else Debug.Log(text, root);
            }

            if (report.Baked)
            {
                EditorUtility.SetDirty(root);
                Debug.Log($"[BarrowsEnd] Baked '{root.name}': {report.Width} x {report.Depth} cells, {report.WalkableCells} walkable, {report.BlockedCells} blocked, {report.WallBoxes} wall boxes, {report.DoorCells} doors.", root);
            }
            else
            {
                Debug.LogError($"[BarrowsEnd] '{root.name}' was NOT baked: fix the errors above. Nothing in the scene was changed.", root);
            }
        }

        private static GameObject FindLocationRoot(GameObject start)
        {
            if (start != null)
            {
                for (var t = start.transform; t != null; t = t.parent)
                    if (t.GetComponentInChildren<Grid>(true) != null) return t.gameObject;
                return null;
            }

            var grid = Object.FindFirstObjectByType<Grid>();
            return grid != null ? grid.transform.parent != null ? grid.transform.parent.gameObject : grid.gameObject : null;
        }
    }
}
