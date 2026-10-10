using CRPG.Actors;
using CRPG.Dialogue;
using CRPG.Gameplay.Scenes;
using UnityEditor;
using UnityEditor.EditorTools;
using UnityEngine;

namespace CRPG.Editor.Location
{
    /// <summary>
    /// Scene-view access to a placed NPC (Section 9 of <c>Docs/Plans/Toolkit-LocationTools.md</c>): a click on an NPC's beacon selects its anchor, and while an anchor is selected a small panel (top right) offers
    /// the next things an author does: open the NpcDefinition (its inspector has the appearance creator), frame it, remove it, and manage its dialogue (assign a <see cref="DialogueDefinition"/>, create one, open
    /// the dialogue graph). Always on for the open scenes (the beacon buttons are invisible: the markers themselves are drawn by <see cref="LocationGizmos"/>), off in play mode and while the Place NPC tool is
    /// active (its own clicks place NPCs).
    /// </summary>
    [InitializeOnLoad]
    public static class NpcQuickPanel
    {
        private const float PanelWidth = 300f;

        static NpcQuickPanel() => SceneView.duringSceneGui += OnScene;

        private static void OnScene(SceneView view)
        {
            if (Application.isPlaying || ToolManager.activeToolType == typeof(PlaceNpcTool)) return;

            foreach (var location in LocationNpcAccess.OpenLocations())
                foreach (var point in location.SpawnPoints)
                {
                    if (point.Anchor == null) continue;
                    var top = point.Anchor.position + Vector3.up * LocationGizmos.HeightOf(point.Definition);
                    var beacon = LocationGizmos.BeaconTop(top);
                    var unit = HandleUtility.GetHandleSize(top);
                    var previous = Handles.color;
                    Handles.color = new Color(1f, 1f, 1f, 0f);   // invisible: only the click matters
                    if (Handles.Button(beacon, Quaternion.identity, unit * 0.2f, unit * 0.3f, Handles.SphereHandleCap)) Selection.activeGameObject = point.Anchor.gameObject;
                    Handles.color = previous;
                }

            if (!LocationNpcAccess.TrySelectedSpawn(out var spawn)) return;
            DrawPanel(view, spawn);
        }

        private static void DrawPanel(SceneView view, SpawnRef spawn)
        {
            var point = spawn.Point;
            var npc = point.Definition;
            Handles.BeginGUI();
            using (new GUILayout.AreaScope(new Rect(view.position.width - PanelWidth - 12f, 12f, PanelWidth, npc != null ? 150f : 70f), GUIContent.none, EditorStyles.helpBox))
            {
                GUILayout.Label($"{(npc != null ? npc.DisplayName : "(no NPC)")}   [{point.SpawnId}]", EditorStyles.boldLabel);
                using (new GUILayout.HorizontalScope())
                {
                    using (new EditorGUI.DisabledScope(npc == null))
                        if (GUILayout.Button(new GUIContent("Open NPC", "Select the NpcDefinition asset: its inspector has the appearance creator, stats and more."))) LocationNpcAccess.SelectNpc(npc);
                    if (GUILayout.Button(new GUIContent("Frame", "Frame this NPC in the Scene view."))) view.Frame(new Bounds(point.Anchor.position + Vector3.up, Vector3.one * 4f), false);
                    if (GUILayout.Button(new GUIContent("Remove", "Remove this spawn point and its anchor (Undo brings it back)."))) { LocationPlacement.RemoveSpawn(spawn.Location, spawn.Index, deleteAnchor: true); GUIUtility.ExitGUI(); }
                }

                if (npc != null)
                {
                    var dialogue = LocationNpcAccess.SpawnDialogue(spawn);
                    EditorGUI.BeginChangeCheck();
                    var picked = (DialogueDefinition)EditorGUILayout.ObjectField("Dialogue", dialogue, typeof(DialogueDefinition), false);
                    if (EditorGUI.EndChangeCheck()) LocationNpcAccess.AssignDialogue(spawn, picked);
                    if (dialogue == null && npc.DialogueId.IsValid) EditorGUILayout.HelpBox($"No dialogue on this spawn point. The NPC's id '{npc.DialogueId.Value}' only works if the game's ruleset registers it.", MessageType.Info);
                    if (GUILayout.Button(dialogue != null ? "Open dialogue graph" : "Create dialogue and open graph")) LocationNpcAccess.OpenDialogue(spawn);
                }
            }

            Handles.EndGUI();
        }
    }
}
