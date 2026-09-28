using System;
using CRPG.UI;
using UnityEngine;
using UnityEngine.UIElements;

namespace BarrowsEnd.Shell
{
    /// <summary>
    /// The shell's title screen: a title and two buttons, <c>new-game-button</c> and <c>continue-button</c> (enabled only while a save exists). Like the
    /// creator screen it fills the panel, registers with <see cref="UIToolkitPointerCapture"/> only while shown, and is hidden with <see cref="Hide"/>
    /// (never by disabling the UIDocument, which would destroy its root). "Is there a save?" is asked when the screen is shown and then about twice a
    /// second while it is visible, not every frame (a syscall per frame), yet still soon enough for a test that sets the save path after the scene loads.
    /// Its buttons lie within 400 px of the panel origin so they stay clickable in a small game view.
    /// <para>Task 5.3: real skin classes (<c>be-screen</c>, <c>be-panel</c> + <c>be-part--panel-wood</c>, <c>be-label</c>, <c>be-button</c> +
    /// <c>be-part--button-secondary</c>) applied directly, unconditionally -- unlike the toolkit's own re-skinnable views (<c>PartyView</c>,
    /// <c>CharacterCreatorScreen</c>, the doll's <c>DollView</c>), this is a BarrowsEnd-only class with exactly one instance and no other consumer, so
    /// there is no "other consumer on the default theme" a placeholder-style flag would need to protect -- retrofitting one here would be planned code
    /// this screen doesn't need (a Task 5.3 review finding).</para>
    /// </summary>
    [RequireComponent(typeof(UIDocument))]
    public sealed class ShellTitleScreen : MonoBehaviour
    {
        private const float PollSeconds = 0.5f;

        private UIDocument document;
        private Func<bool> hasSave;
        private Action onNewGame;
        private Action onContinue;
        private Button continueButton;
        private bool visible = true;
        private bool initialized;
        private float nextPoll;

        public void Initialize(Func<bool> hasSave, Action onNewGame, Action onContinue)
        {
            this.hasSave = hasSave;
            this.onNewGame = onNewGame;
            this.onContinue = onContinue;
            initialized = true;
            if (isActiveAndEnabled) Build();
        }

        public void Show()
        {
            visible = true;
            document = document != null ? document : GetComponent<UIDocument>();
            if (document.rootVisualElement != null) document.rootVisualElement.style.display = DisplayStyle.Flex;
            if (isActiveAndEnabled) UIToolkitPointerCapture.Register(document);
            RefreshContinue();
        }

        public void Hide()
        {
            visible = false;
            document = document != null ? document : GetComponent<UIDocument>();
            if (document.rootVisualElement != null) document.rootVisualElement.style.display = DisplayStyle.None;
            UIToolkitPointerCapture.Unregister(document);
        }

        private void OnEnable()
        {
            document = GetComponent<UIDocument>();
            if (visible) UIToolkitPointerCapture.Register(document);
            if (initialized) Build();
        }

        private void OnDisable()
        {
            if (document != null) UIToolkitPointerCapture.Unregister(document);
        }

        private void Update()
        {
            if (!visible || continueButton == null || Time.unscaledTime < nextPoll) return;
            RefreshContinue();
        }

        private void RefreshContinue()
        {
            nextPoll = Time.unscaledTime + PollSeconds;
            continueButton?.SetEnabled(hasSave != null && hasSave());
        }

        private void Build()
        {
            var root = document.rootVisualElement;
            root.Clear();
            root.style.display = visible ? DisplayStyle.Flex : DisplayStyle.None;

            var screen = new VisualElement { name = "shell-title" };
            screen.AddToClassList("shell-title");
            screen.AddToClassList("be-screen");
            screen.style.position = Position.Absolute;
            screen.style.left = screen.style.top = screen.style.right = screen.style.bottom = 0;
            root.Add(screen);

            var panel = new VisualElement { name = "shell-title-panel" };
            panel.AddToClassList("be-panel");
            panel.AddToClassList("be-part--panel-wood");
            panel.style.position = Position.Absolute;
            panel.style.left = 48;
            panel.style.top = 48;
            panel.style.width = 300;
            screen.Add(panel);

            // NOT be-label--title: that token's --be-ink-on-dark is meant for text directly on the be-screen
            // backdrop, but this label sits inside the wood-panel child -- light ink on brown wood, the same
            // contrast mistake Task 5.2's skin pass already found once for a different label on the same wood
            // art. Plain be-label (full-strength --be-ink) plus this element's own font-size rule instead.
            var title = new Label("Barrow's End") { name = "shell-title-label" };
            title.AddToClassList("be-label");
            title.style.marginBottom = 24;
            panel.Add(title);

            panel.Add(MenuButton("new-game-button", "New Game", () => onNewGame?.Invoke()));
            continueButton = MenuButton("continue-button", "Continue", () => onContinue?.Invoke());
            panel.Add(continueButton);
            RefreshContinue();
        }

        private static Button MenuButton(string name, string text, Action onClick)
        {
            var button = new Button(onClick) { name = name, text = text };
            button.AddToClassList("be-button");
            button.AddToClassList("be-part--button-secondary");
            button.style.width = 220;
            button.style.height = 40;
            button.style.marginBottom = 8;
            return button;
        }
    }
}
