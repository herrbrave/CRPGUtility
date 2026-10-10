using System.Linq;
using CRPG.Actors;
using CRPG.Editor.Validation;
using CRPG.Gameplay.Ambient;
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
            root.Add(new PropertyField(property.FindPropertyRelative("Dialogue"), "Dialogue") { name = "spawn-dialogue-field", tooltip = "What this NPC says when the party talks to it. Empty = it does not talk." });
            root.Add(new PropertyField(property.FindPropertyRelative("Store"), "Store") { name = "spawn-store-field", tooltip = "What this NPC sells. No listings = no shop. The dialogue opens it with an Open Store action." });

            var buttons = new VisualElement { style = { flexDirection = FlexDirection.Row } };
            Button Make(string text, string tooltip, System.Action click) { var b = new Button(click) { text = text, tooltip = tooltip, name = "spawn-" + text.ToLowerInvariant() }; b.style.flexGrow = 1; return b; }
            buttons.Add(Make("Select", "Select the anchor in the hierarchy (the Move tool then moves it).", () => { var a = AnchorOf(serialized, path); if (a != null) Selection.activeObject = a.gameObject; }));
            buttons.Add(Make("Frame", "Frame the anchor in the Scene view.", () => { var a = AnchorOf(serialized, path); if (a != null && SceneView.lastActiveSceneView != null) SceneView.lastActiveSceneView.Frame(new Bounds(a.position, Vector3.one * 3f), false); }));
            buttons.Add(Make("Snap", "Move the anchor onto the nearest walkable point.", () => Snap(serialized, path)));
            buttons.Add(Make("NPC", "Select the NpcDefinition asset (appearance, stats, ...).", () => SelectNpc(serialized, path)));
            buttons.Add(Make("Dialogue", "Open this NPC's dialogue graph (a new dialogue is created when it has none).", () => OpenDialogue(serialized, path)));
            root.Add(buttons);
            root.Add(BuildRoutine(serialized, property, path));

            root.Bind(serialized);
            void Refresh() => Paint(status, serialized, path);
            root.schedule.Execute(Refresh).Every(400);
            Refresh();
            return root;
        }

        /// <summary>Which routine field groups are shown for a kind: wander (area, pauses) and work loop (stops). Stand shows neither. Public so the rule is testable without a window.</summary>
        public static (bool wander, bool work) RoutineSections(NpcRoutineKind kind) => (kind == NpcRoutineKind.Wander, kind == NpcRoutineKind.WorkLoop);

        private static VisualElement BuildRoutine(SerializedObject serialized, SerializedProperty property, string path)
        {
            var routine = property.FindPropertyRelative("Routine");
            var section = new VisualElement { name = "spawn-routine" };
            section.style.marginTop = 2;

            var kind = routine.FindPropertyRelative("Kind");
            var kindField = new PropertyField(kind, "Routine") { name = "routine-kind" };
            kindField.tooltip = "What this NPC does when nothing else is asking: Stand, Wander an area, or WorkLoop between stops.";
            section.Add(kindField);

            var wander = new VisualElement { name = "routine-wander" };
            wander.Add(new PropertyField(routine.FindPropertyRelative("AreaCentre"), "Area centre") { tooltip = "Empty = the NPC's own anchor." });
            wander.Add(new PropertyField(routine.FindPropertyRelative("AreaRadius"), "Radius (m)"));
            wander.Add(new PropertyField(routine.FindPropertyRelative("PauseMin"), "Pause min (s)"));
            wander.Add(new PropertyField(routine.FindPropertyRelative("PauseMax"), "Pause max (s)"));
            section.Add(wander);

            var work = new VisualElement { name = "routine-work" };
            work.Add(new PropertyField(routine.FindPropertyRelative("Stops"), "Stops"));
            var addStop = new Button(() => AddStop(serialized, path, section)) { text = "Add stop", name = "routine-add-stop", tooltip = "Create a work stop in front of the NPC and add it to the loop. Drag it where the NPC should work." };
            work.Add(addStop);
            section.Add(work);

            var seed = new PropertyField(routine.FindPropertyRelative("Seed"), "Seed") { name = "routine-seed", tooltip = "0 = derived from the spawn id: the same NPC always wanders the same way." };
            section.Add(seed);

            void Apply(NpcRoutineKind value)
            {
                var (showWander, showWork) = RoutineSections(value);
                wander.style.display = showWander ? DisplayStyle.Flex : DisplayStyle.None;
                work.style.display = showWork ? DisplayStyle.Flex : DisplayStyle.None;
                seed.style.display = value == NpcRoutineKind.Stand ? DisplayStyle.None : DisplayStyle.Flex;
            }

            Apply((NpcRoutineKind)kind.enumValueIndex);
            section.TrackPropertyValue(kind, changed => Apply((NpcRoutineKind)changed.enumValueIndex));
            return section;
        }

        /// <summary>What the Add stop button does: append a new stop transform to the row's work loop.</summary>
        public static Transform AddStop(SerializedObject serialized, string path, VisualElement refresh = null)
        {
            if (serialized.targetObject is not LocationContent location) return null;
            var stop = LocationPlacement.AddStop(location, IndexOf(path));
            serialized.Update();
            if (refresh != null) refresh.Bind(serialized);
            return stop;
        }

        private static Transform AnchorOf(SerializedObject serialized, string path)
        {
            serialized.Update();
            var property = serialized.FindProperty(path);
            return property?.FindPropertyRelative("Anchor").objectReferenceValue as Transform;
        }

        /// <summary>What the NPC button does: select the row's NpcDefinition asset.</summary>
        public static void SelectNpc(SerializedObject serialized, string path) => LocationNpcAccess.SelectNpc(DefinitionOf(serialized, path));

        /// <summary>What the Dialogue button does: open the row's dialogue graph (creating the dialogue first when the spawn point has none).</summary>
        public static void OpenDialogue(SerializedObject serialized, string path)
        {
            if (serialized.targetObject is LocationContent location && IndexOf(path) >= 0) LocationNpcAccess.OpenDialogue(new SpawnRef(location, IndexOf(path)));
        }

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
            // No deep routine checks (path queries, prefab inspection): this runs every 400 ms per row.
            var results = LocationValidator.Validate(location, new NavMeshService(), deepChecks: false).Where(r => r.Field.StartsWith($"spawnPoints[{index}]")).ToList();
            var worst = results.Count == 0 ? (ValidationSeverity?)null : results.Any(r => r.Severity == ValidationSeverity.Error) ? ValidationSeverity.Error : ValidationSeverity.Warning;
            status.style.color = worst == ValidationSeverity.Error ? new Color(0.9f, 0.25f, 0.2f) : worst == ValidationSeverity.Warning ? new Color(0.95f, 0.75f, 0.2f) : new Color(0.3f, 0.75f, 0.35f);
            status.tooltip = results.Count == 0 ? "No problems found." : string.Join("\n", results.Select(r => r.Message));
        }
    }
}
