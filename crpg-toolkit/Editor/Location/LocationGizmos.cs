using System.Collections.Generic;
using System.Linq;
using CRPG.Actors;
using CRPG.Gameplay.Ambient;
using CRPG.Gameplay.Scenes;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace CRPG.Editor.Location
{
    /// <summary>
    /// Draws every NPC spawn point of a <see cref="LocationContent"/> in the Scene view (shown for every location in the open scenes; the Gizmos toggle hides it). A village is hundreds of metres wide and a 1.8 m
    /// wire capsule disappears in it, so each marker is built to be found: the body is as tall as the NPC's visual prefab, but a ground ring, a facing arrow and a BEACON (a line up to a downward cone) keep a
    /// minimum SCREEN size at any zoom, everything is drawn twice (faint where a building hides it, solid where it is visible) so it shows through walls, and the label sits on a dark plate.
    /// Red = a cheap data error (no id, duplicate id, no definition, no anchor), green otherwise; the full checks (walkability, reachability) live in the validator and the inspector's status dots, never here
    /// (a path query per frame would be far too slow). A newly placed NPC pulses for <see cref="FlashSeconds"/>.
    /// </summary>
    public static class LocationGizmos
    {
        public const float DefaultHeight = 1.8f, Radius = 0.35f, FlashSeconds = 3f;
        private static readonly Dictionary<GameObject, float> Heights = new();
        private static GUIStyle labelStyle;
        private static Transform flashAnchor;
        private static double flashUntil;

        /// <summary>Makes the marker of this anchor pulse for a few seconds (called when an NPC was just placed).</summary>
        public static void Flash(Transform anchor)
        {
            flashAnchor = anchor;
            flashUntil = EditorApplication.timeSinceStartup + FlashSeconds;
            SceneView.RepaintAll();
        }

        [DrawGizmo(GizmoType.Selected | GizmoType.NonSelected | GizmoType.Pickable)]
        private static void Draw(LocationContent location, GizmoType type)
        {
            var spawns = location.SpawnPoints;
            for (var i = 0; i < spawns.Count; i++)
            {
                var point = spawns[i];
                if (point.Anchor == null) continue;
                var seen = spawns.Take(i).Any(p => p.SpawnId == point.SpawnId);
                var bad = string.IsNullOrEmpty(point.SpawnId) || seen || point.Definition == null || point.Definition.VisualPrefab == null;
                var name = point.Definition != null ? point.Definition.DisplayName : "?";
                var pulse = 0f;
                if (point.Anchor == flashAnchor && EditorApplication.timeSinceStartup < flashUntil)
                {
                    pulse = 1f - (float)((flashUntil - EditorApplication.timeSinceStartup) / FlashSeconds);
                    SceneView.RepaintAll();
                }

                DrawNpc(point.Anchor.position, point.Anchor.rotation, HeightOf(point.Definition), bad ? new Color(0.95f, 0.25f, 0.2f) : new Color(0.25f, 0.9f, 0.4f), $"{point.SpawnId} ({name})", pulse);
                DrawRoutine(point);
            }
        }

        /// <summary>A colour for one NPC's routine overlay, stable per spawn id so the wander circle and stop path of an NPC match each other and differ from the next NPC's. (The marker itself is green/red for data errors, so it cannot carry this.)</summary>
        public static Color RoutineColour(string spawnId)
        {
            var hash = NpcRoutines.Seed(spawnId, 0);
            return Color.HSVToRGB(((uint)hash % 360u) / 360f, 0.55f, 1f);
        }

        /// <summary>True when the selection is this NPC's anchor, wander centre or one of its stops (or a child of one): its routine is drawn strong, every other one faint.</summary>
        public static bool IsRoutineSelected(NpcSpawnPoint point)
        {
            var selected = Selection.transforms;
            if (selected == null || selected.Length == 0) return false;

            bool Hit(Transform t) => t != null && selected.Any(sel => sel == t || sel.IsChildOf(t));
            if (Hit(point.Anchor)) return true;
            var routine = point.Routine;
            if (routine == null) return false;
            if (Hit(routine.AreaCentre)) return true;
            return routine.Stops != null && routine.Stops.Any(stop => stop != null && Hit(stop.Point));
        }

        private static void DrawRoutine(NpcSpawnPoint point)
        {
            var routine = point.Routine;
            if (routine == null || !routine.IsActive) return;

            var strong = IsRoutineSelected(point);
            var colour = RoutineColour(point.SpawnId);
            colour.a = strong ? 1f : 0.3f;
            var previous = Handles.color; var previousZ = Handles.zTest;
            Handles.zTest = CompareFunction.Always;
            Handles.color = colour;

            if (routine.Kind == NpcRoutineKind.Wander)
            {
                var centre = routine.AreaCentre != null ? routine.AreaCentre.position : point.Anchor.position;
                Handles.DrawWireDisc(centre, Vector3.up, routine.AreaRadius, strong ? 3f : 1.5f);
                var fill = colour; fill.a = strong ? 0.1f : 0.04f;
                Handles.color = fill;
                Handles.DrawSolidDisc(centre, Vector3.up, routine.AreaRadius);
                if (strong) Handles.Label(centre + Vector3.up * 0.2f, $"{point.SpawnId}: wander {routine.AreaRadius:0.#} m", LabelStyle());
            }
            else if (routine.Kind == NpcRoutineKind.WorkLoop && routine.Stops != null)
            {
                var stops = routine.Stops.Where(stop => stop != null && stop.Point != null).ToList();
                for (var j = 0; j < stops.Count; j++)
                {
                    var here = stops[j].Point;
                    var unit = HandleUtility.GetHandleSize(here.position);
                    Handles.color = colour;
                    Handles.DrawWireDisc(here.position, Vector3.up, Mathf.Max(0.35f, unit * 0.12f), strong ? 3f : 1.5f);
                    var forward = here.forward; forward.y = 0f;
                    if (forward.sqrMagnitude > 0.001f) Handles.DrawLine(here.position, here.position + forward.normalized * Mathf.Max(0.8f, unit * 0.35f), strong ? 3f : 1.5f);

                    if (stops.Count > 1 || j == 0)
                    {
                        var next = stops[(j + 1) % stops.Count].Point;
                        if (next != here) Handles.DrawDottedLine(here.position + Vector3.up * 0.05f, next.position + Vector3.up * 0.05f, 4f);
                    }

                    if (strong) Handles.Label(here.position + Vector3.up * 0.4f, $"{j + 1}: {stops[j].Action}{(stops[j].CueId.IsValid ? " (" + stops[j].CueId + ")" : "")}", LabelStyle());
                }

                if (stops.Count > 0) Handles.DrawDottedLine(point.Anchor.position, stops[0].Point.position, 2f);
            }

            Handles.color = previous; Handles.zTest = previousZ;
        }

        private static GUIStyle LabelStyle()
        {
            if (labelStyle != null && labelStyle.normal.background != null) return labelStyle;
            var plate = new Texture2D(1, 1) { hideFlags = HideFlags.HideAndDontSave };
            plate.SetPixel(0, 0, new Color(0.07f, 0.08f, 0.1f, 0.85f));
            plate.Apply();
            labelStyle = new GUIStyle(EditorStyles.boldLabel) { fontSize = 12, alignment = TextAnchor.MiddleCenter, padding = new RectOffset(6, 6, 2, 3), wordWrap = false };
            labelStyle.normal.textColor = Color.white;
            labelStyle.normal.background = plate;
            return labelStyle;
        }

        /// <summary>Where the beacon cone sits above an NPC whose head top is at <paramref name="headTop"/> (the click target of the quick panel).</summary>
        public static Vector3 BeaconTop(Vector3 headTop) => headTop + Vector3.up * Mathf.Max(0.4f, HandleUtility.GetHandleSize(headTop) * 0.9f);

        /// <summary>The marker of one NPC at a pose (also the ghost of the placement tool). <paramref name="pulse"/> 0 is off; 0..1 is the progress of the "just placed" pulse.</summary>
        public static void DrawNpc(Vector3 position, Quaternion rotation, float height, Color colour, string label, float pulse = 0f)
        {
            var previousColour = Handles.color; var previousZ = Handles.zTest;
            var top = position + Vector3.up * height;
            var unit = HandleUtility.GetHandleSize(top);                         // about the same on screen at any zoom
            var ring = Mathf.Max(Radius, unit * 0.16f);                          // never smaller than a few pixels
            var forward = rotation * Vector3.forward;
            var arrowLength = Mathf.Max(Radius + 0.45f, unit * 0.5f);
            var beaconTop = BeaconTop(top);
            var side = Vector3.Cross(Vector3.up, forward);

            // pass 1: the part a building hides (faint); pass 2: the visible part (solid)
            foreach (var (z, alpha) in new[] { (CompareFunction.Greater, 0.4f), (CompareFunction.LessEqual, 1f) })
            {
                Handles.zTest = z;
                var c = colour; c.a = alpha;
                Handles.color = c;
                Handles.DrawWireDisc(position, Vector3.up, ring);
                Handles.DrawWireDisc(top, Vector3.up, Radius);
                foreach (var d in new[] { Vector3.forward, Vector3.back, Vector3.left, Vector3.right }) Handles.DrawLine(position + d * Radius, top + d * Radius, 2f);
                var fill = colour; fill.a = 0.22f * alpha;
                Handles.color = fill;
                Handles.DrawSolidDisc(position, Vector3.up, ring);
                for (var s = 1; s <= 6; s++) Handles.DrawSolidDisc(Vector3.Lerp(position, top, s / 7f), Vector3.up, Radius);   // a see-through column
                Handles.color = c;
                var from = position + Vector3.up * (height * 0.5f) + forward * Radius;
                var tip = position + Vector3.up * (height * 0.5f) + forward * arrowLength;
                Handles.DrawLine(from, tip, 3f);
                Handles.DrawLine(tip, tip - forward * 0.25f * Mathf.Max(1f, unit) + side * 0.14f * Mathf.Max(1f, unit), 3f);
                Handles.DrawLine(tip, tip - forward * 0.25f * Mathf.Max(1f, unit) - side * 0.14f * Mathf.Max(1f, unit), 3f);
                Handles.DrawLine(top, beaconTop, 2f);
                Handles.ConeHandleCap(0, beaconTop, Quaternion.LookRotation(Vector3.down), unit * 0.22f, EventType.Repaint);
                if (pulse > 0f)
                {
                    var wave = colour; wave.a = (1f - pulse) * alpha;
                    Handles.color = wave;
                    for (var r = 0; r < 3; r++) Handles.DrawWireDisc(position, Vector3.up, ring + (pulse + r * 0.33f) % 1f * unit * 1.6f, 3f);
                }
            }

            Handles.zTest = CompareFunction.Always;
            Handles.Label(beaconTop + Vector3.up * unit * 0.35f, label, LabelStyle());
            Handles.color = previousColour; Handles.zTest = previousZ;
        }

        /// <summary>The height of an NPC from its visual prefab's meshes (mesh bounds: a prefab asset's renderer bounds are not reliable); the default when it has none.</summary>
        public static float HeightOf(NpcDefinition definition)
        {
            var prefab = definition != null ? definition.VisualPrefab : null;
            if (prefab == null) return DefaultHeight;
            if (Heights.TryGetValue(prefab, out var cached)) return cached;
            var meshes = prefab.GetComponentsInChildren<MeshFilter>(true).Select(f => f.sharedMesh).Concat(prefab.GetComponentsInChildren<SkinnedMeshRenderer>(true).Select(s => s.sharedMesh)).Where(m => m != null).ToList();
            var height = meshes.Count == 0 ? DefaultHeight : Mathf.Clamp(meshes.Max(m => m.bounds.max.y) - Mathf.Min(0f, meshes.Min(m => m.bounds.min.y)), 0.5f, 6f);
            Heights[prefab] = height;
            return height;
        }
    }
}
