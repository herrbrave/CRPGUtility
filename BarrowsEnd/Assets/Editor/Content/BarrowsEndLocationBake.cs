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

        /// <summary>Opens a location scene, bakes it and saves it: for batch use and tests. Returns the report; the scene is only saved when the bake succeeded.</summary>
        public static BakeReport BakeScene(string scenePath)
        {
            var scene = UnityEditor.SceneManagement.EditorSceneManager.OpenScene(scenePath, UnityEditor.SceneManagement.OpenSceneMode.Single);
            var root = FindLocationRoot(null);
            var report = LocationBaker.Bake(root, Options());
            if (report.Baked) UnityEditor.SceneManagement.EditorSceneManager.SaveScene(scene);
            return report;
        }

        /// <summary>Bakes both demo locations (batch entry point: -executeMethod BarrowsEnd.Editor.BarrowsEndLocationBake.BakeDemoLocations).</summary>
        [MenuItem("Barrows End/Location/Bake Both Demo Locations")]
        public static void BakeDemoLocations()
        {
            foreach (var path in new[] { BarrowsEndLocationAuthor.TownPath, BarrowsEndLocationAuthor.CavePath })
            {
                var report = BakeScene(path);
                Debug.Log($"[BarrowsEnd] {path}: baked={report.Baked}, {report.WalkableCells} walkable, {report.BlockedCells} blocked, {report.WallBoxes} wall boxes, {report.Issues.Count} issue(s).");
                foreach (var issue in report.Issues) Debug.LogWarning($"[BarrowsEnd] {path}: {issue}");
            }
        }

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
