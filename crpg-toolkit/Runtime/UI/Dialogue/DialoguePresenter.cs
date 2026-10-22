using System;
using System.Collections.Generic;
using CRPG.Core;
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

        /// <summary>Wraps an already-running runner. Retain this presenter when rebuilding views so the
        /// last spoken prompt survives choices and store hand-offs without replaying the graph.</summary>
        public DialoguePresenter(DialogueRunner runner, DialogueContext context)
        {
            this.context = context;
            this.runner = runner;
            RetainCurrentLine();
        }

        public bool IsComplete => runner.IsComplete;
        public bool IsAtLine => runner.CurrentNode is LineNode;
        public bool IsAtChoice => runner.CurrentNode is ChoiceNode;
        public bool IsAtCheck => runner.CurrentNode is CheckNode;

        public SerializableGuid CurrentNodeId => runner.CurrentNode.NodeId;

        /// <summary>The most recent settled Line, retained at choices/checks/end. Empty before any Line.</summary>
        public string LastLineText { get; private set; } = string.Empty;
        public string LastSpeakerName { get; private set; } = string.Empty;
        public SerializableGuid LastLineNodeId { get; private set; } = SerializableGuid.Empty;

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

        public void Advance()
        {
            runner.Advance();
            RetainCurrentLine();
        }

        public void ChooseOption(int optionIndex)
        {
            runner.ChooseOption(optionIndex);
            RetainCurrentLine();
        }

        public CheckPrompt PrepareCheck() => runner.PrepareCheck();

        public DialogueCheckOutcome PerformCheck(CheckRollOptions options = null)
        {
            var outcome = runner.PerformCheck(options);
            RetainCurrentLine();
            return outcome;
        }

        private void RetainCurrentLine()
        {
            if (runner.CurrentNode is LineNode line)
            {
                LastLineText = line.Text;
                LastSpeakerName = line.SpeakerName;
                LastLineNodeId = line.NodeId;
            }
        }
    }
}
