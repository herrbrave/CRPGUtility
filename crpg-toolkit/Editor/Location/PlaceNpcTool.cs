using System.Linq;
using CRPG.Actors;
using CRPG.Gameplay.Scenes;
using CRPG.Navigation;
using UnityEditor;
using UnityEditor.EditorTools;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace CRPG.Editor.Location
{
    /// <summary>
    /// The scene-view tool that places NPC spawn points (design <c>Docs/Plans/Toolkit-LocationTools.md</c>, Section 7). A GLOBAL tool (in the Tools overlay's custom tools list, and activatable from the
    /// <c>CRPG Toolkit/Location</c> menu): a component tool bound to <see cref="LocationContent"/> can only be activated while the Inspector holds that component's editor, which a menu command cannot
    /// guarantee (it threw "the current selection does not contain any objects editable by the component tool"). It places into <see cref="ResolveLocation"/>. Not in play mode. Pick an NPC definition in the panel; a ghost capsule follows the mouse over the ground (green: a walkable point, red: nowhere to stand); left click adds the spawn point
    /// and its anchor through <see cref="LocationPlacement.AddSpawn"/> (one Undo step; hold Shift to keep placing), R turns the NPC by 45 degrees, Escape or a right click leaves the tool. The ray takes the first
    /// physics hit whose point snaps onto the walkable surface, so a roof or a tree the ray passes first does not swallow the click (a package cannot know a project's layer names; the panel has a layer mask).
    /// Facing defaults to looking at the Scene view camera.
    /// </summary>
    [EditorTool("Place NPC")]
    public sealed class PlaceNpcTool : EditorTool
    {
        private const string DefinitionKey = "CRPG.PlaceNpc.Definition", MaskKey = "CRPG.PlaceNpc.Mask";
        private const float SnapRadius = 0.75f;
        private static readonly INavMeshService Nav = new NavMeshService();
        private float? manualYaw;

        public static NpcDefinition Picked
        {
            get
            {
                var guid = SessionState.GetString(DefinitionKey, "");
                return string.IsNullOrEmpty(guid) ? null : AssetDatabase.LoadAssetAtPath<NpcDefinition>(AssetDatabase.GUIDToAssetPath(guid));
            }
            set => SessionState.SetString(DefinitionKey, value == null ? "" : AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(value)));
        }

        /// <summary>The location the tool places into: the selected object's LocationContent (on it, above it or below it), else the first one in the selected object's scene, else the first open one.</summary>
        public static LocationContent ResolveLocation()
        {
            var selected = Selection.activeGameObject;
            if (selected != null)
            {
                var own = selected.GetComponentInParent<LocationContent>() ?? selected.GetComponentInChildren<LocationContent>(true);
                if (own != null) return own;
                var sameScene = Object.FindObjectsByType<LocationContent>(FindObjectsInactive.Exclude).FirstOrDefault(l => l.gameObject.scene == selected.scene);
                if (sameScene != null) return sameScene;
            }

            return Object.FindFirstObjectByType<LocationContent>();
        }

        public static int Mask { get => SessionState.GetInt(MaskKey, ~0); set => SessionState.SetInt(MaskKey, value); }

        public override void OnActivated() { Physics.SyncTransforms(); manualYaw = null; }

        /// <summary>The first physics hit along the ray whose point snaps onto the walkable surface (null when none does).</summary>
        public static Vector3? WalkablePointAlong(Ray ray, int mask, INavMeshService nav)
        {
            foreach (var hit in Physics.RaycastAll(ray, 5000f, mask, QueryTriggerInteraction.Ignore).OrderBy(h => h.distance))
                if (nav.TrySamplePosition(hit.point, out var snapped, SnapRadius)) return snapped;
            return null;
        }

        public override void OnToolGUI(EditorWindow window)
        {
            if (window is not SceneView sceneView || Application.isPlaying) return;
            var location = ResolveLocation();
            var e = Event.current;

            Handles.BeginGUI();
            using (new GUILayout.AreaScope(new Rect(12, 12, 280, 96), GUIContent.none, EditorStyles.helpBox))
            {
                GUILayout.Label(location != null ? $"Place NPC into '{location.name}' ({location.gameObject.scene.name})" : "Place NPC: no LocationContent in the open scenes", EditorStyles.boldLabel);
                Picked = (NpcDefinition)EditorGUILayout.ObjectField(Picked, typeof(NpcDefinition), false);
                var layers = Enumerable.Range(0, 32).Select(LayerMask.LayerToName).ToArray();
                Mask = EditorGUILayout.MaskField("Ground layers", Mask, layers.Select((n, i) => string.IsNullOrEmpty(n) ? "Layer " + i : n).ToArray());
                GUILayout.Label("Click: place   Shift: keep placing   R: turn   Esc: done", EditorStyles.miniLabel);
            }

            Handles.EndGUI();

            if (e.type == EventType.Layout) HandleUtility.AddDefaultControl(GUIUtility.GetControlID(FocusType.Passive));
            var definition = Picked;
            if (definition == null || location == null) return;

            if (e.type == EventType.KeyDown && e.keyCode == KeyCode.R) { manualYaw = (manualYaw ?? CameraYaw(sceneView)) + 45f; e.Use(); }
            if (e.type == EventType.KeyDown && e.keyCode == KeyCode.Escape) { ToolManager.RestorePreviousPersistentTool(); e.Use(); return; }
            if (e.type == EventType.MouseDown && e.button == 1 && !e.alt) { ToolManager.RestorePreviousPersistentTool(); e.Use(); return; }

            var point = WalkablePointAlong(HandleUtility.GUIPointToWorldRay(e.mousePosition), Mask, Nav);
            var rotation = Quaternion.Euler(0f, manualYaw ?? CameraYaw(sceneView), 0f);
            var colour = point.HasValue ? new Color(0.3f, 0.85f, 0.4f) : new Color(0.9f, 0.25f, 0.2f);
            if (point.HasValue) LocationGizmos.DrawNpc(point.Value, rotation, LocationGizmos.HeightOf(definition), colour, definition.DisplayName);

            if (e.type == EventType.MouseDown && e.button == 0 && !e.alt && point.HasValue)
            {
                var id = LocationPlacement.AddSpawn(location, definition, point.Value, rotation, null);
                var anchor = location.SpawnPoints.Last().Anchor;
                Debug.Log($"[Location] placed '{id}' at {point.Value:F1}.", anchor);
                LocationGizmos.Flash(anchor);              // the new marker pulses for a few seconds
                Selection.activeGameObject = anchor.gameObject;   // and Unity's own move handles show where it is (placing again still works: the tool resolves the location from the scene)
                EditorGUIUtility.PingObject(anchor);
                e.Use();
                if (!e.shift) ToolManager.RestorePreviousPersistentTool();
            }

            if (e.type == EventType.MouseMove || e.type == EventType.MouseDrag) sceneView.Repaint();
        }

        /// <summary>The yaw that turns an NPC to face the Scene view camera (so a freshly placed NPC looks at the author).</summary>
        private static float CameraYaw(SceneView sceneView)
        {
            var toCamera = -sceneView.camera.transform.forward; toCamera.y = 0f;
            return toCamera.sqrMagnitude < 1e-4f ? 0f : Quaternion.LookRotation(toCamera).eulerAngles.y;
        }
    }
}
