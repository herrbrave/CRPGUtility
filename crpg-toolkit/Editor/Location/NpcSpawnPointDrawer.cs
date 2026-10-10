using System.Linq;
using CRPG.Actors;
using CRPG.Editor.Validation;
using CRPG.Gameplay.Scenes;
using CRPG.Navigation;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace CRPG.Editor.Location
{
    /// <summary>
    /// How one NPC spawn point of a <see cref="LocationContent"/> looks in the Inspector: its three fields (the default list around it, with reorder, multi-edit and Undo, is untouched, and no other
    /// <c>LocationContent</c> property or scene is affected), a status dot from the <see cref="LocationValidator"/> (red: an Error, yellow: a Warning, green: clean), and Select / Frame / Snap buttons for
    /// the anchor. A property drawer, not a custom editor, so the 2D scenes that carry an empty <c>LocationContent</c> keep their inspector.
    /// </summary>
    [CustomPropertyDrawer(typeof(NpcSpawnPoint))]
    public sealed class NpcSpawnPointDrawer : PropertyDrawer
    {
        public override VisualElement CreatePropertyGUI(SerializedProperty property)
        {
            var serialized = property.serializedObject;
            var path = property.propertyPath;
            var root = new VisualElement { name = "spawn-point" };
            root.style.marginBottom = 4;

            var header = new VisualElement { style = { flexDirection = FlexDirection.Row, alignItems = Align.Center } };
            var status = new Label("●") { name = "spawn-status" };
            status.style.width = 16; status.style.unityFontStyleAndWeight = FontStyle.Bold;
            var id = new PropertyField(property.FindPropertyRelative("SpawnId"), "Spawn id");
            id.style.flexGrow = 1;
            header.Add(status); header.Add(id);
            root.Add(header);
            root.Add(new PropertyField(property.FindPropertyRelative("Definition"), "NPC"));
            root.Add(new PropertyField(property.FindPropertyRelative("Anchor"), "Anchor"));

            var buttons = new VisualElement { style = { flexDirection = FlexDirection.Row } };
            Button Make(string text, string tooltip, System.Action click) { var b = new Button(click) { text = text, tooltip = tooltip, name = "spawn-" + text.ToLowerInvariant() }; b.style.flexGrow = 1; return b; }
            buttons.Add(Make("Select", "Select the anchor in the hierarchy (the Move tool then moves it).", () => { var a = AnchorOf(serialized, path); if (a != null) Selection.activeObject = a.gameObject; }));
            buttons.Add(Make("Frame", "Frame the anchor in the Scene view.", () => { var a = AnchorOf(serialized, path); if (a != null && SceneView.lastActiveSceneView != null) SceneView.lastActiveSceneView.Frame(new Bounds(a.position, Vector3.one * 3f), false); }));
            buttons.Add(Make("Snap", "Move the anchor onto the nearest walkable point.", () => Snap(serialized, path)));
            buttons.Add(Make("NPC", "Select the NpcDefinition asset (appearance, stats, ...).", () => SelectNpc(serialized, path)));
            buttons.Add(Make("Dialogue", "Open this NPC's dialogue graph (a new dialogue is created when it has none).", () => OpenDialogue(serialized, path)));
            root.Add(buttons);

            root.Bind(serialized);
            void Refresh() => Paint(status, serialized, path);
            root.schedule.Execute(Refresh).Every(400);
            Refresh();
            return root;
        }

        private static Transform AnchorOf(SerializedObject serialized, string path)
        {
            serialized.Update();
            var property = serialized.FindProperty(path);
            return property?.FindPropertyRelative("Anchor").objectReferenceValue as Transform;
        }

        /// <summary>What the NPC button does: select the row's NpcDefinition asset.</summary>
        public static void SelectNpc(SerializedObject serialized, string path) => LocationNpcAccess.SelectNpc(DefinitionOf(serialized, path));

        /// <summary>What the Dialogue button does: open the row's NPC dialogue graph (creating the dialogue first when there is none).</summary>
        public static void OpenDialogue(SerializedObject serialized, string path) => LocationNpcAccess.OpenDialogue(DefinitionOf(serialized, path));

        private static NpcDefinition DefinitionOf(SerializedObject serialized, string path)
        {
            serialized.Update();
            return serialized.FindProperty(path)?.FindPropertyRelative("Definition").objectReferenceValue as NpcDefinition;
        }

        private static int IndexOf(string path)
        {
            var open = path.LastIndexOf('[');
            var close = path.LastIndexOf(']');
            return open >= 0 && close > open && int.TryParse(path.Substring(open + 1, close - open - 1), out var i) ? i : -1;
        }

        private static void Snap(SerializedObject serialized, string path)
        {
            var anchor = AnchorOf(serialized, path);
            if (anchor == null || EditorUtility.IsPersistent(anchor)) return;
            if (new NavMeshService().TrySamplePosition(anchor.position, out var snapped, 5f))
            {
                Undo.RecordObject(anchor, "Snap NPC anchor");
                anchor.position = snapped;
            }
            else Debug.LogWarning($"[Location] no walkable point within 5 m of '{anchor.name}'.");
        }

        private static void Paint(Label status, SerializedObject serialized, string path)
        {
            if (serialized.targetObject is not LocationContent location) return;
            var index = IndexOf(path);
            var results = LocationValidator.Validate(location, new NavMeshService()).Where(r => r.Field.StartsWith($"spawnPoints[{index}]")).ToList();
            var worst = results.Count == 0 ? (ValidationSeverity?)null : results.Any(r => r.Severity == ValidationSeverity.Error) ? ValidationSeverity.Error : ValidationSeverity.Warning;
            status.style.color = worst == ValidationSeverity.Error ? new Color(0.9f, 0.25f, 0.2f) : worst == ValidationSeverity.Warning ? new Color(0.95f, 0.75f, 0.2f) : new Color(0.3f, 0.75f, 0.35f);
            status.tooltip = results.Count == 0 ? "No problems found." : string.Join("\n", results.Select(r => r.Message));
        }
    }
}
