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
    public sealed class DialogueView : MonoBehaviour
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

        public void Initialize(DialoguePresenter dialoguePresenter)
        {
            presenter = dialoguePresenter;
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

            speakerLabel.text = presenter.IsAtCheck ? "Check" : presenter.SpeakerName;
            textLabel.text = presenter.IsAtCheck
                ? $"{presenter.CheckLabel} (DC {presenter.CheckDifficultyClass})"
                : presenter.LineText;

            var newResponseArea = new VisualElement();

            if (presenter.IsAtLine)
            {
                var continueButton = new Button(OnContinueClicked) { text = "Continue" };
                newResponseArea.Add(continueButton);
            }
            else if (presenter.IsAtChoice)
            {
                foreach (var choice in presenter.GetChoices())
                {
                    var optionIndex = choice.OptionIndex;
                    var choiceButton = new Button(() => OnChoiceClicked(optionIndex)) { text = choice.Text };
                    choiceButton.style.marginBottom = 4;
                    newResponseArea.Add(choiceButton);
                }
            }
            else if (presenter.IsAtCheck)
            {
                var rollButton = new Button(OnRollClicked) { text = "Roll" };
                newResponseArea.Add(rollButton);
            }

            var index = responseArea.parent.IndexOf(responseArea);
            responseArea.parent.Insert(index, newResponseArea);
            responseArea.RemoveFromHierarchy();
            responseArea = newResponseArea;
        }

        private void OnContinueClicked()
        {
            HideResult();
            presenter.Advance();
            Refresh();
        }

        private void OnChoiceClicked(int optionIndex)
        {
            HideResult();
            presenter.ChooseOption(optionIndex);
            Refresh();
        }

        private void OnRollClicked()
        {
            var outcome = presenter.PerformCheck();
            resultLabel.style.display = DisplayStyle.Flex;
            resultLabel.text = outcome.Success
                ? $"Rolled {outcome.Rolls[0].Total} -- Success!"
                : $"Rolled {outcome.Rolls[0].Total} -- Failure.";
            Refresh();
        }

        private void HideResult() => resultLabel.style.display = DisplayStyle.None;
    }
}
