using System;
using System.Collections.Generic;
using CRPG.Dialogue;

namespace CRPG.UI
{
    /// <summary>
    /// Pure data/command layer wrapping a live DialogueRunner -- no UI Toolkit dependency, mirroring
    /// CampPresenter/StorePresenter's split. DialogueRunner already auto-settles through Branch/Action nodes,
    /// so CurrentNode is always a Line, Choice, Check, or End by the time this is asked about it.
    /// </summary>
    public sealed class DialoguePresenter
    {
        private readonly DialogueRunner runner;
        private readonly DialogueContext context;

        public DialoguePresenter(DialogueDefinition definition, DialogueContext context)
            : this(new DialogueRunner(definition, context), context)
        {
        }

        /// <summary>Wraps an ALREADY-RUNNING DialogueRunner instead of starting a fresh conversation --
        /// ConversationController uses this to rebuild the View layer around the same runner across a store
        /// hand-off round-trip (Task 3.4) without restarting the conversation from its StartNodeId.</summary>
        public DialoguePresenter(DialogueRunner runner, DialogueContext context)
        {
            this.context = context;
            this.runner = runner;
        }

        public bool IsComplete => runner.IsComplete;
        public bool IsAtLine => runner.CurrentNode is LineNode;
        public bool IsAtChoice => runner.CurrentNode is ChoiceNode;
        public bool IsAtCheck => runner.CurrentNode is CheckNode;

        public string SpeakerName => (runner.CurrentNode as LineNode)?.SpeakerName ?? string.Empty;
        public string LineText => (runner.CurrentNode as LineNode)?.Text ?? string.Empty;

        public string CheckLabel => (runner.CurrentNode as CheckNode)?.QueryKey ?? string.Empty;
        public int CheckDifficultyClass => (runner.CurrentNode as CheckNode)?.DifficultyClass ?? 0;

        public IReadOnlyList<DialogueChoiceSummary> GetChoices()
        {
            var summaries = new List<DialogueChoiceSummary>();
            if (runner.CurrentNode is not ChoiceNode choice) return summaries;

            for (var i = 0; i < choice.Options.Count; i++)
            {
                var option = choice.Options[i];
                if (option.IsVisible(context.Timeline)) summaries.Add(new DialogueChoiceSummary(i, option.Text));
            }

            return summaries;
        }

        public void Advance() => runner.Advance();
        public void ChooseOption(int optionIndex) => runner.ChooseOption(optionIndex);
        public DialogueCheckOutcome PerformCheck() => runner.PerformCheck();
    }
}
