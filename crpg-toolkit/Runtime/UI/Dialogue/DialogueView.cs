using CRPG.Dialogue;
using UnityEngine;
using UnityEngine.UIElements;

namespace CRPG.UI
{
    /// <summary>
    /// Minimal functional Dialogue screen: speaker + line/check text, and whatever response the current node
    /// needs (a Continue button, a row of choice buttons, or a Roll button) -- built procedurally like Camp/
    /// Store, same first-pass-placeholder rationale. The interactive area is rebuilt as a fresh VisualElement
    /// every refresh rather than Clear()-and-repopulated in place, since the number of buttons shown changes
    /// from node to node -- the exact class of bug that corrupted StoreView's rows when its list length changed
    /// across a refresh (see StoreView.SwapContainer).
    /// </summary>
    [RequireComponent(typeof(UIDocument))]
    public sealed class DialogueView : MonoBehaviour, IConversationView
    {
        private DialoguePresenter presenter;
        private UIDocument document;
        private VisualElement root;
        private Label speakerLabel;
        private Label textLabel;
        private Label resultLabel;
        private VisualElement responseArea;

        /// <summary>Fired once the conversation reaches its End node, after the final content is shown.</summary>
        public event System.Action Completed;

        /// <summary>Fired after the player drove the conversation one step (Continue, a choice, a roll) and the new content is shown: the owner can react to what that step set in motion (an Open Store action
        /// reached in the middle of a conversation). Not fired for the step that completed the conversation.</summary>
        public event System.Action Driven;

        private bool plainStyle;
        private IDialogueCheckPresenter checkPresenter;
        private bool checkPresented;
        private bool disposed;

        /// <summary>Releases this component's presentation immediately. Its creator owns destroying the
        /// GameObject/document; OnDestroy is an idempotent fallback, not a second ownership transfer.</summary>
        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            Completed = null;
            Driven = null;
            enabled = false;
            if (root != null) root.Clear();
        }

        private void OnDestroy() => Dispose();

        /// <param name="plainStyle">False (default) keeps the look a game's own USS skin paints over (Barrow's End). True is a self-contained dark panel with light text, for a game with no skin: the default
        /// look is mid-grey with dark text, which only reads well under that skin.</param>
        /// <param name="checkPresenter">Optional: a game's own presentation of a skill check (a dice popup). Null keeps the built-in Roll button.</param>
        public void Initialize(DialoguePresenter dialoguePresenter, bool plainStyle = false, IDialogueCheckPresenter checkPresenter = null)
        {
            presenter = dialoguePresenter;
            this.plainStyle = plainStyle;
            this.checkPresenter = checkPresenter; checkPresented = false;
            document = GetComponent<UIDocument>();
            BuildLayout();
            Refresh();
        }

        // Task 3.1: registers with the shared pointer-capture provider so a world click landing on this
        // panel's own UI Toolkit elements is correctly suppressed rather than falling through to a Move/Select
        // command. Uses GetComponent directly (not the `document` field) since OnEnable can run before an
        // external Initialize() call sets it.
        private void OnEnable() => UIToolkitPointerCapture.Register(GetComponent<UIDocument>());

        private void OnDisable() => UIToolkitPointerCapture.Unregister(GetComponent<UIDocument>());

        private void BuildLayout()
        {
            root = document.rootVisualElement;
            root.Clear();
            root.style.flexGrow = 1;
            root.style.justifyContent = Justify.FlexEnd;
            root.style.paddingLeft = 24;
            root.style.paddingRight = 24;
            root.style.paddingBottom = 24;

            var panel = new VisualElement { style = { width = 640, paddingLeft = 16, paddingTop = 16, paddingRight = 16, paddingBottom = 16 } };
            panel.style.borderTopWidth = 1;
            panel.style.borderBottomWidth = 1;
            panel.style.borderLeftWidth = 1;
            panel.style.borderRightWidth = 1;
            panel.style.borderTopColor = Color.gray;
            panel.style.borderBottomColor = Color.gray;
            panel.style.borderLeftColor = Color.gray;
            panel.style.borderRightColor = Color.gray;
            panel.AddToClassList("be-panel");
            panel.AddToClassList("be-part--panel-wood");
            panel.style.backgroundColor = new Color(0.34f, 0.35f, 0.38f, 1f);
            panel.style.color = new Color(0.231f, 0.161f, 0.098f, 1f);
            if (plainStyle)
            {
                panel.style.backgroundColor = new Color(0.10f, 0.11f, 0.14f, 0.94f);
                panel.style.color = new Color(0.94f, 0.91f, 0.85f, 1f);
                var edge = new Color(0.62f, 0.55f, 0.40f, 1f);
                panel.style.borderTopColor = edge;
                panel.style.borderBottomColor = edge;
                panel.style.borderLeftColor = edge;
                panel.style.borderRightColor = edge;
                panel.style.borderTopLeftRadius = 6;
                panel.style.borderTopRightRadius = 6;
                panel.style.borderBottomLeftRadius = 6;
                panel.style.borderBottomRightRadius = 6;
            }

            root.Add(panel);

            speakerLabel = new Label { style = { fontSize = 18, unityFontStyleAndWeight = FontStyle.Bold, marginBottom = 4 } };
            panel.Add(speakerLabel);

            textLabel = new Label { style = { fontSize = 14, marginBottom = 8, whiteSpace = WhiteSpace.Normal } };
            panel.Add(textLabel);

            resultLabel = new Label { style = { fontSize = 13, marginBottom = 8, color = Color.yellow, whiteSpace = WhiteSpace.Normal } };
            resultLabel.style.display = DisplayStyle.None;
            panel.Add(resultLabel);

            responseArea = new VisualElement();
            panel.Add(responseArea);
        }

        private void Refresh()
        {
            if (presenter.IsComplete)
            {
                root.style.display = DisplayStyle.None;
                Completed?.Invoke();
                return;
            }

            speakerLabel.text = presenter.IsAtCheck ? "Check" : presenter.LastSpeakerName;
            textLabel.text = presenter.IsAtCheck
                ? $"{presenter.CheckLabel} (DC {presenter.CheckDifficultyClass})"
                : presenter.LastLineText;

            var newResponseArea = new VisualElement();

            if (presenter.IsAtLine)
            {
                var continueButton = new Button(OnContinueClicked) { text = "Continue" };
                StyleButton(continueButton);
                newResponseArea.Add(continueButton);
            }
            else if (presenter.IsAtChoice)
            {
                foreach (var choice in presenter.GetChoices())
                {
                    var optionIndex = choice.OptionIndex;
                    var choiceButton = new Button(() => OnChoiceClicked(optionIndex)) { text = choice.Text };
                    choiceButton.style.marginBottom = 4;
                    StyleButton(choiceButton);
                    newResponseArea.Add(choiceButton);
                }
            }
            else if (presenter.IsAtCheck && checkPresenter == null)
            {
                var rollButton = new Button(OnRollClicked) { text = "Roll" };
                StyleButton(rollButton);
                newResponseArea.Add(rollButton);
            }

            var index = responseArea.parent.IndexOf(responseArea);
            responseArea.parent.Insert(index, newResponseArea);
            responseArea.RemoveFromHierarchy();
            responseArea = newResponseArea;

            // Finish layout before invoking game code: a synchronous check completion may recursively
            // refresh, open a store, or dispose this view. There is no layout work after that callback.
            if (presenter.IsAtCheck && checkPresenter != null && !checkPresented)
            {
                checkPresented = true;
                root.style.display = DisplayStyle.None;
                checkPresenter.Present(presenter.PrepareCheck(), options =>
                {
                    if (disposed) throw new System.ObjectDisposedException(nameof(DialogueView));
                    return presenter.PerformCheck(options);
                }, () =>
                {
                    if (disposed) return; // A cancelled check popup may close after its conversation.
                    checkPresented = false;
                    root.style.display = DisplayStyle.Flex;
                    Refresh();
                    if (!presenter.IsComplete) Driven?.Invoke();
                });
            }
        }

        // The plain style's buttons: a dark fill with the panel's light text (the default button is light grey, on which the inherited light text would vanish).
        private void StyleButton(Button button)
        {
            if (!plainStyle) return;
            button.style.backgroundColor = new Color(0.22f, 0.24f, 0.30f, 1f);
            button.style.color = new Color(0.94f, 0.91f, 0.85f, 1f);
            var edge = new Color(0.62f, 0.55f, 0.40f, 1f);
            button.style.borderTopColor = edge;
            button.style.borderBottomColor = edge;
            button.style.borderLeftColor = edge;
            button.style.borderRightColor = edge;
        }

        private void OnContinueClicked()
        {
            HideResult();
            presenter.Advance();
            Refresh();
            if (!presenter.IsComplete) Driven?.Invoke();
        }

        private void OnChoiceClicked(int optionIndex)
        {
            HideResult();
            presenter.ChooseOption(optionIndex);
            Refresh();
            if (!presenter.IsComplete) Driven?.Invoke();
        }

        private void OnRollClicked()
        {
            var outcome = presenter.PerformCheck();
            resultLabel.style.display = DisplayStyle.Flex;
            resultLabel.text = outcome.Success
                ? $"Rolled {outcome.Rolls[0].Total} -- Success!"
                : $"Rolled {outcome.Rolls[0].Total} -- Failure.";
            Refresh();
            if (!presenter.IsComplete) Driven?.Invoke();
        }

        private void HideResult() => resultLabel.style.display = DisplayStyle.None;
    }
}
