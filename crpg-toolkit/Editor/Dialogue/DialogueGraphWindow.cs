using CRPG.Dialogue;
using UnityEditor;
using UnityEditor.Callbacks;
using UnityEngine;
using UnityEngine.UIElements;

namespace CRPG.Editor.Dialogue
{
    /// <summary>The dialogue graph tool's window: a toolbar (Save button + unsaved-changes indicator in the
    /// title) over a DialogueGraphView. Opens on double-clicking a DialogueDefinition asset, the same
    /// convention Shader Graph/Animator windows use. Built on UnityEditor.Experimental.GraphView rather than
    /// the newer Unity 6.6 Graph Toolkit package -- GraphView ships in every Unity install with zero extra
    /// package installs, where Graph Toolkit is still experimental and requires the end user (a buyer of this
    /// toolkit) to manually install a preview package. See the design conversation this was decided in.</summary>
    public sealed class DialogueGraphWindow : EditorWindow
    {
        private DialogueDefinition dialogueDefinition;
        private DialogueGraphView graphView;
        private Label titleLabel;

        [OnOpenAsset]
        public static bool OnOpenAsset(EntityId entityId, int line)
        {
            if (EditorUtility.EntityIdToObject(entityId) is not DialogueDefinition dialogueDefinition) return false;

            Open(dialogueDefinition);
            return true;
        }

        public static void Open(DialogueDefinition dialogueDefinition)
        {
            var window = GetWindow<DialogueGraphWindow>();
            window.titleContent = new GUIContent("Dialogue Graph");
            window.Load(dialogueDefinition);
        }

        private void Load(DialogueDefinition newDialogueDefinition)
        {
            dialogueDefinition = newDialogueDefinition;
            rootVisualElement.Clear();

            // Plain VisualElement/Button rather than UnityEditor.UIElements.Toolbar/ToolbarButton -- those are
            // internal to Unity in this version, not usable from third-party Editor code.
            var toolbar = new VisualElement
            {
                style =
                {
                    flexDirection = FlexDirection.Row, height = 22, paddingLeft = 4, paddingRight = 4,
                    alignItems = Align.Center, borderBottomWidth = 1, borderBottomColor = new Color(0f, 0f, 0f, 0.3f),
                },
            };
            var saveButton = new Button(Save) { text = "Save" };
            toolbar.Add(saveButton);
            titleLabel = new Label(dialogueDefinition.DisplayName) { style = { unityTextAlign = TextAnchor.MiddleCenter, flexGrow = 1 } };
            toolbar.Add(titleLabel);
            rootVisualElement.Add(toolbar);

            graphView = new DialogueGraphView(dialogueDefinition) { style = { flexGrow = 1 } };
            graphView.DirtyStateChanged += RefreshTitle;
            rootVisualElement.Add(graphView);

            RefreshTitle();
        }

        private void Save()
        {
            graphView.CommitPendingChanges();
            EditorUtility.SetDirty(dialogueDefinition);
            AssetDatabase.SaveAssets();
            RefreshTitle();
        }

        private void RefreshTitle()
        {
            var dirty = dialogueDefinition != null && EditorUtility.IsDirty(dialogueDefinition);
            titleContent = new GUIContent(dirty ? "Dialogue Graph*" : "Dialogue Graph");
            if (titleLabel != null) titleLabel.text = dialogueDefinition.DisplayName + (dirty ? " *" : "");
        }

        private void OnDestroy()
        {
            if (graphView != null) graphView.DirtyStateChanged -= RefreshTitle;
        }
    }
}
