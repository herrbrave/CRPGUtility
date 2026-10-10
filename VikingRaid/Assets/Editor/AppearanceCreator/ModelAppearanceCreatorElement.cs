using System;
using System.Collections.Generic;
using System.Linq;
using CRPG.Actors;
using CRPG.Core;
using CRPG.Editor.Npc;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace VikingRaid.Appearance.Editor
{
    /// <summary>Plugs the model appearance creator (every family: Viking, Goblin) into the toolkit's NpcDefinition inspector: it applies to an NPC with no visual prefab yet (offering each family's base prefab) or whose prefab has a <see cref="ModelAppearanceBinder"/>.</summary>
    public sealed class ModelAppearanceCreatorEditor : INpcAppearanceEditor
    {
        public bool Handles(NpcDefinition npc)
        {
            if (npc == null) return false;
            var prefab = npc.VisualPrefab;
            return prefab == null || prefab.TryGetComponent<ModelAppearanceBinder>(out _);
        }

        public VisualElement Create(SerializedObject npc) => new ModelAppearanceCreatorElement(npc);
    }

    /// <summary>
    /// The creator UI on the NPC inspector: three tabs of `&lt; Title: Value &gt;` rows, a live 3D preview (drag to orbit, scroll to zoom), Randomize this tab / Randomize all / Reset. Every change is written
    /// straight into the NPC's <c>appearance</c> list through the SerializedObject (so undo works and the raw foldout above shows the data); entries for rows the catalog does not know are kept as they are.
    /// </summary>
    public sealed class ModelAppearanceCreatorElement : VisualElement, IDisposable
    {
        private const string FoldoutKey = "VikingRaid.AppearanceCreator.Tab";

        private readonly SerializedObject npc;
        private readonly SerializedProperty appearance;
        private readonly ModelAppearanceCatalog catalog;
        private readonly ModelAppearanceCreatorModel model;
        private readonly ModelAppearancePreview preview;
        private readonly VisualElement rowsHost = new() { name = "appearance-rows" };
        private readonly VisualElement tabBar = new() { name = "appearance-tabs" };
        private readonly Dictionary<string, Button> tabButtons = new();
        private IVisualElementScheduledItem repaintWatch;
        private bool writing;
        private readonly Label titleLabel;

        public ModelAppearanceCreatorElement(SerializedObject npc, bool withPreview = true)
        {
            this.npc = npc;
            name = "model-appearance";
            style.marginTop = 4; style.marginBottom = 6;
            style.paddingLeft = 6; style.paddingRight = 6; style.paddingTop = 4; style.paddingBottom = 6;
            style.borderTopWidth = style.borderBottomWidth = style.borderLeftWidth = style.borderRightWidth = 1;
            var edge = new Color(0.4f, 0.4f, 0.4f, 0.6f);
            style.borderTopColor = style.borderBottomColor = style.borderLeftColor = style.borderRightColor = edge;

            titleLabel = new Label("Appearance") { style = { unityFontStyleAndWeight = FontStyle.Bold, marginBottom = 4 } };
            Add(titleLabel);

            if (npc.FindProperty("visualPrefab").objectReferenceValue == null)
            {
                BuildPrefabOffer();
                return;
            }

            catalog = ModelAppearanceFamilies.CatalogOf(npc.FindProperty("visualPrefab").objectReferenceValue as GameObject);
            if (catalog == null)
            {
                Add(new HelpBox("This NPC's visual prefab has no appearance binder, so there is nothing to dress. Clear the visual prefab to pick a base prefab.", HelpBoxMessageType.Info));
                return;
            }

            titleLabel.text = catalog.Family + " Appearance";
            appearance = npc.FindProperty("appearance");
            model = new ModelAppearanceCreatorModel(catalog);
            PruneOtherFamilies();
            LoadFromAsset();

            Add(tabBar);
            BuildTabs();

            if (withPreview) preview = TryCreatePreview();
            if (preview != null) Add(BuildPreviewView());
            Add(rowsHost);
            Add(BuildButtons());

            model.Changed += OnModelChanged;
            this.TrackPropertyValue(appearance, _ => { if (!writing) { LoadFromAsset(); RefreshRows(); Dress(); } });

            var first = catalog.Tabs.FirstOrDefault() ?? "";
            var remembered = SessionState.GetString(FoldoutKey, first);
            SelectTab(catalog.Tabs.Contains(remembered) ? remembered : first);
            Dress();
        }

        public ModelAppearanceCatalog Catalog => catalog;
        public ModelAppearanceCreatorModel Model => model;
        public ModelAppearancePreview Preview => preview;
        public string CurrentTab { get; private set; }

        /// <summary>Whether the preview frames the head for the current tab (the catalog's head tab: Viking Head, goblin Face).</summary>
        public bool HeadFramed { get; private set; }

        // ------------------------------------------------------------------ no prefab yet
        private void BuildPrefabOffer()
        {
            var offered = 0;
            foreach (var (family, path) in ModelAppearanceFamilies.All)
            {
                if (AssetDatabase.LoadAssetAtPath<GameObject>(path) == null) continue;
                if (offered++ == 0) Add(new HelpBox("This NPC has no visual prefab. Pick a family's base prefab to dress it from here.", HelpBoxMessageType.Info));
                var captured = family;
                Add(new Button(() => UseBasePrefab(captured)) { text = $"Use the {family} NPC base prefab", name = "use-base-prefab-" + family.ToLowerInvariant() });
            }

            if (offered == 0) Add(new HelpBox("No NPC base prefab exists yet. Run 'Viking Raid/Village/Author Villager Characters' (Viking) or 'Viking Raid/Appearance/Author Goblin Base Prefab' (Goblin), then reselect this NPC.", HelpBoxMessageType.Warning));
        }

        /// <summary>Assigns a family's NPC base prefab as the NPC's visual prefab (the inspector then rebuilds its extensions, and this creator shows its rows).</summary>
        public void UseBasePrefab(string family)
        {
            var path = ModelAppearanceFamilies.BasePrefabPath(family);
            var basePrefab = path == null ? null : AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (basePrefab == null) return;
            npc.Update();
            npc.FindProperty("visualPrefab").objectReferenceValue = basePrefab;
            npc.ApplyModifiedProperties();
        }

        // ------------------------------------------------------------------ tabs and rows
        private void BuildTabs()
        {
            tabBar.style.flexDirection = FlexDirection.Row;
            tabBar.style.marginBottom = 4;
            foreach (var tab in catalog.Tabs)
            {
                var captured = tab;
                var button = new Button(() => SelectTab(captured)) { text = tab, name = "tab-" + tab.ToLowerInvariant() };
                button.style.flexGrow = 1;
                tabButtons[tab] = button;
                tabBar.Add(button);
            }
        }

        public void SelectTab(string tab)
        {
            if (!tabButtons.ContainsKey(tab)) return;
            CurrentTab = tab;
            SessionState.SetString(FoldoutKey, tab);
            foreach (var pair in tabButtons) pair.Value.style.backgroundColor = pair.Key == tab ? new StyleColor(new Color(0.24f, 0.37f, 0.58f)) : new StyleColor(StyleKeyword.Null);
            HeadFramed = tab == catalog.HeadTab;
            preview?.FocusHead(HeadFramed);
            RefreshRows();
            RepaintPreview();
        }

        private void RefreshRows()
        {
            rowsHost.Clear();
            foreach (var slot in catalog.SlotsOn(CurrentTab ?? catalog.Tabs.First()))
            {
                var row = model.RowFor(slot.id);
                var line = new VisualElement { name = "row-" + slot.id };
                line.style.flexDirection = FlexDirection.Row;
                line.style.alignItems = Align.Center;
                line.style.marginBottom = 2;

                var title = new Label(slot.title) { name = "title-" + slot.id };
                title.style.width = 90;
                var value = new Label(row.Selected.Name) { name = "value-" + slot.id };
                value.style.flexGrow = 1; value.style.unityTextAlign = TextAnchor.MiddleCenter;
                var swatch = new VisualElement { name = "swatch-" + slot.id };
                swatch.style.width = 14; swatch.style.height = 14; swatch.style.marginRight = 4;
                swatch.style.display = row.Selected.HasSwatch ? DisplayStyle.Flex : DisplayStyle.None;
                if (row.Selected.HasSwatch) swatch.style.backgroundColor = (Color)row.Selected.Swatch;

                var previous = new Button(() => row.Advance(-1)) { text = "<", name = "prev-" + slot.id };
                var next = new Button(() => row.Advance(1)) { text = ">", name = "next-" + slot.id };
                var position = new Label($"{row.Index + 1}/{row.Count}") { name = "position-" + slot.id };
                position.style.width = 48; position.style.unityTextAlign = TextAnchor.MiddleRight;
                position.style.color = new Color(0.65f, 0.65f, 0.65f);

                line.Add(title); line.Add(previous); line.Add(swatch); line.Add(value); line.Add(next); line.Add(position);
                rowsHost.Add(line);
            }
        }

        private VisualElement BuildButtons()
        {
            var bar = new VisualElement { name = "appearance-buttons" };
            bar.style.flexDirection = FlexDirection.Row;
            bar.style.marginTop = 4;
            void AddButton(string text, string id, Action action)
            {
                var button = new Button(action) { text = text, name = id };
                button.style.flexGrow = 1;
                bar.Add(button);
            }

            AddButton("Randomize tab", "randomize-tab", () => model.Randomize(Environment.TickCount, CurrentTab));
            AddButton("Randomize all", "randomize-all", () => model.Randomize(Environment.TickCount));
            AddButton("Reset", "reset", () => model.Reset());
            return bar;
        }

        // ------------------------------------------------------------------ data
        /// <summary>Drops appearance entries that are clearly ANOTHER family's: a row this catalog does not have but another family's catalog does, or an option id of a row this catalog has that only another
        /// family's catalog knows (the NPC was switched from one family's prefab to another's). Entries nobody knows are kept (a genuinely foreign creator's data, or a typo to fix by hand).</summary>
        private void PruneOtherFamilies()
        {
            npc.Update();
            var others = ModelAppearanceFamilies.Catalogs().Where(c => c != catalog).ToList();
            var drop = new List<int>();
            for (var i = 0; i < appearance.arraySize; i++)
            {
                var entry = appearance.GetArrayElementAtIndex(i);
                var slotId = entry.FindPropertyRelative("layerSlot.value").stringValue;
                var optionId = entry.FindPropertyRelative("option.value").stringValue;
                var mine = catalog.FindSlot(slotId);
                if (mine != null && (string.IsNullOrEmpty(optionId) || mine.Find(optionId) != null)) continue;
                var other = mine == null ? others.Any(c => c.FindSlot(slotId) != null) : others.Any(c => c.FindSlot(slotId)?.Find(optionId) != null);
                if (other) drop.Add(i);
            }

            if (drop.Count == 0) return;
            for (var k = drop.Count - 1; k >= 0; k--) appearance.DeleteArrayElementAtIndex(drop[k]);
            npc.ApplyModifiedProperties();
            Debug.Log($"[ModelAppearanceCreator] removed {drop.Count} appearance entr{(drop.Count == 1 ? "y" : "ies")} that belong to another family.");
        }

        private void LoadFromAsset()
        {
            var stored = new Dictionary<DefinitionId, DefinitionId>();
            for (var i = 0; i < appearance.arraySize; i++)
            {
                var entry = appearance.GetArrayElementAtIndex(i);
                var slot = entry.FindPropertyRelative("layerSlot.value").stringValue;
                var option = entry.FindPropertyRelative("option.value").stringValue;
                if (!string.IsNullOrEmpty(slot)) stored[new DefinitionId(slot)] = new DefinitionId(option);
            }

            model.Load(stored);
        }

        private void OnModelChanged()
        {
            WriteToAsset();
            RefreshRows();
            Dress();
        }

        /// <summary>Rewrites the appearance list: entries for rows the catalog does not own stay, then one entry per non-None row, in catalog order.</summary>
        private void WriteToAsset()
        {
            npc.Update();
            var foreign = new List<(string slot, string option)>();
            for (var i = 0; i < appearance.arraySize; i++)
            {
                var entry = appearance.GetArrayElementAtIndex(i);
                var slot = entry.FindPropertyRelative("layerSlot.value").stringValue;
                if (catalog.FindSlot(slot) == null) foreign.Add((slot, entry.FindPropertyRelative("option.value").stringValue));
            }

            var ours = model.Choices;
            var all = foreign.Concat(catalog.Slots.Where(s => ours.ContainsKey(new DefinitionId(s.id))).Select(s => (slot: s.id, option: ours[new DefinitionId(s.id)].Value))).ToList();

            writing = true;
            try
            {
                appearance.arraySize = all.Count;
                for (var i = 0; i < all.Count; i++)
                {
                    var entry = appearance.GetArrayElementAtIndex(i);
                    entry.FindPropertyRelative("layerSlot.value").stringValue = all[i].slot;
                    entry.FindPropertyRelative("option.value").stringValue = all[i].option;
                }

                npc.ApplyModifiedProperties();
            }
            finally { writing = false; }
        }

        // ------------------------------------------------------------------ preview
        private ModelAppearancePreview TryCreatePreview()
        {
            try { return new ModelAppearancePreview(catalog); }
            catch (Exception e)
            {
                Add(new HelpBox("The 3D preview could not start: " + e.Message, HelpBoxMessageType.Warning));
                return null;
            }
        }

        private VisualElement BuildPreviewView()
        {
            var view = new IMGUIContainer(DrawPreview) { name = "appearance-preview" };
            view.style.height = 300;
            view.style.marginBottom = 4;
            // the first frames of a Synty shader graph are placeholders until compilation ends: keep repainting while the editor compiles shaders
            repaintWatch = view.schedule.Execute(() => { if (ShaderUtil.anythingCompiling) view.MarkDirtyRepaint(); }).Every(250);
            return view;
        }

        private void DrawPreview()
        {
            if (preview == null) return;
            var rect = GUILayoutUtility.GetRect(10, 300, GUILayout.ExpandWidth(true));
            var e = Event.current;
            if (rect.Contains(e.mousePosition))
            {
                if (e.type == EventType.MouseDrag) { preview.Orbit(e.delta.x, e.delta.y); e.Use(); RepaintPreview(); }
                else if (e.type == EventType.ScrollWheel) { preview.Zoom(e.delta.y); e.Use(); RepaintPreview(); }
            }

            if (e.type != EventType.Repaint) return;
            var texture = preview.Render(rect);
            if (texture != null) GUI.DrawTexture(rect, texture, ScaleMode.StretchToFill, false);
        }

        private void RepaintPreview() => this.Q<IMGUIContainer>("appearance-preview")?.MarkDirtyRepaint();

        private void Dress()
        {
            preview?.Apply(model.Choices);
            RepaintPreview();
        }

        public void Dispose()
        {
            repaintWatch?.Pause();
            preview?.Dispose();
        }
    }
}
