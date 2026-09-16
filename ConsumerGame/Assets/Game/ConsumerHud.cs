using System;
using CRPG.UI;
using UnityEngine;
using UnityEngine.UIElements;

namespace CRPG.Consumer.Game
{
    /// <summary>
    /// Gate D's "distinct UI skin": a procedurally-built UI Toolkit panel (no .uxml/.uss -- same
    /// no-asset-authoring-tool pattern <c>CampView</c> already uses) with its own dark-navy/amber palette,
    /// visually unlike any existing TestProject demo. Every <c>CRPG.UI</c> presenter/view this game reuses
    /// (dialogue's own <c>DialogueView</c>, via <c>ConversationController</c>) stays untouched package code --
    /// only this HUD, which is entirely this project's own, carries the distinct skin.
    /// </summary>
    [RequireComponent(typeof(UIDocument))]
    public sealed class ConsumerHud : MonoBehaviour
    {
        private static readonly Color BackgroundColor = new(0.09f, 0.10f, 0.16f, 0.92f);
        private static readonly Color AmberColor = new(0.878f, 0.643f, 0.345f);
        private static readonly Color InkColor = new(0.949f, 0.914f, 0.863f);

        private UIDocument document;
        private Label statusLabel;
        private Label stressLabel;
        private Button strikeButton;
        private Button strikeInvokeButton;
        private Button saveButton;
        private Button loadButton;

        public event Action StrikeRequested;
        public event Action StrikeInvokingRequested;
        public event Action SaveRequested;
        public event Action LoadRequested;

        private void Awake()
        {
            document = GetComponent<UIDocument>();
            BuildLayout();
        }

        private void OnEnable() => UIToolkitPointerCapture.Register(document);
        private void OnDisable() => UIToolkitPointerCapture.Unregister(document);

        private void BuildLayout()
        {
            var root = document.rootVisualElement;
            root.Clear();
            root.style.flexGrow = 1;

            var panel = new VisualElement();
            panel.style.position = Position.Absolute;
            panel.style.left = 16;
            panel.style.bottom = 16;
            panel.style.width = 360;
            panel.style.paddingLeft = 14;
            panel.style.paddingRight = 14;
            panel.style.paddingTop = 12;
            panel.style.paddingBottom = 12;
            panel.style.backgroundColor = BackgroundColor;
            panel.style.borderTopLeftRadius = 8;
            panel.style.borderTopRightRadius = 8;
            panel.style.borderBottomLeftRadius = 8;
            panel.style.borderBottomRightRadius = 8;
            root.Add(panel);

            statusLabel = new Label("Exploring.");
            statusLabel.style.color = InkColor;
            statusLabel.style.fontSize = 16;
            statusLabel.style.marginBottom = 6;
            panel.Add(statusLabel);

            stressLabel = new Label(string.Empty);
            stressLabel.style.color = InkColor;
            stressLabel.style.marginBottom = 8;
            panel.Add(stressLabel);

            var buttonRow = new VisualElement();
            buttonRow.style.flexDirection = FlexDirection.Row;
            panel.Add(buttonRow);

            strikeButton = MakeButton("Strike", () => StrikeRequested?.Invoke());
            strikeInvokeButton = MakeButton("Strike (Invoke)", () => StrikeInvokingRequested?.Invoke());
            buttonRow.Add(strikeButton);
            buttonRow.Add(strikeInvokeButton);

            var saveRow = new VisualElement();
            saveRow.style.flexDirection = FlexDirection.Row;
            saveRow.style.marginTop = 8;
            panel.Add(saveRow);

            saveButton = MakeButton("Save", () => SaveRequested?.Invoke());
            loadButton = MakeButton("Load", () => LoadRequested?.Invoke());
            saveRow.Add(saveButton);
            saveRow.Add(loadButton);

            SetCombatVisible(false);
        }

        private static Button MakeButton(string text, Action onClick)
        {
            var button = new Button(onClick) { text = text };
            button.style.marginRight = 8;
            button.style.backgroundColor = AmberColor;
            button.style.color = new Color(0.1f, 0.08f, 0.02f);
            button.style.borderTopLeftRadius = 4;
            button.style.borderTopRightRadius = 4;
            button.style.borderBottomLeftRadius = 4;
            button.style.borderBottomRightRadius = 4;
            return button;
        }

        public void SetStatus(string text) => statusLabel.text = text;

        public void SetStress(int heroStress, int hostileStress) =>
            stressLabel.text = $"Party Stress: {heroStress}   Hostile Stress: {hostileStress}";

        public void SetCombatVisible(bool visible)
        {
            var display = visible ? DisplayStyle.Flex : DisplayStyle.None;
            strikeButton.style.display = display;
            strikeInvokeButton.style.display = display;
        }

        public void SetCombatButtonsEnabled(bool isEnabled)
        {
            strikeButton.SetEnabled(isEnabled);
            strikeInvokeButton.SetEnabled(isEnabled);
        }
    }
}
