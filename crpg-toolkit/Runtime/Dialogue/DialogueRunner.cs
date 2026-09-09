using System;
using System.Collections.Generic;
using CRPG.Actors;
using CRPG.Core;

namespace CRPG.Dialogue
{
    /// <summary>
    /// Walks a DialogueDefinition's graph. BranchNode and ActionNode need no player interaction, so
    /// entering one of them auto-resolves ("settles") straight through to the next node that does --
    /// a Line, a Choice, a Check, or the End -- without the caller needing to know the difference.
    /// </summary>
    public sealed class DialogueRunner
    {
        private readonly DialogueContext context;
        private readonly Dictionary<string, DialogueNode> nodesById;

        public DialogueRunner(DialogueDefinition definition, DialogueContext context)
        {
            this.context = context;
            nodesById = new Dictionary<string, DialogueNode>();
            foreach (var node in definition.Nodes) nodesById[node.NodeId] = node;

            CurrentNode = nodesById[definition.StartNodeId];
            Settle();
        }

        public DialogueNode CurrentNode { get; private set; }
        public bool IsComplete => CurrentNode is EndNode;

        /// <summary>Valid on a LineNode.</summary>
        public void Advance()
        {
            if (CurrentNode is not LineNode line)
                throw new InvalidOperationException($"Advance() isn't valid on a {CurrentNode.GetType().Name}.");
            MoveTo(line.NextNodeId);
        }

        /// <summary>Valid on a ChoiceNode. Options gated by an unmet visibility condition can't be chosen.</summary>
        public void ChooseOption(int optionIndex)
        {
            if (CurrentNode is not ChoiceNode choice)
                throw new InvalidOperationException($"ChooseOption() isn't valid on a {CurrentNode.GetType().Name}.");

            var option = choice.Options[optionIndex];
            if (!option.IsVisible(context.Timeline))
                throw new InvalidOperationException("That choice option is not currently available.");

            MoveTo(option.NextNodeId);
        }

        /// <summary>Valid on a ChoiceNode. The subset of Options currently visible per each option's Timeline condition.</summary>
        public IReadOnlyList<ChoiceOption> GetVisibleOptions()
        {
            if (CurrentNode is not ChoiceNode choice)
                throw new InvalidOperationException($"GetVisibleOptions() isn't valid on a {CurrentNode.GetType().Name}.");

            var visible = new List<ChoiceOption>();
            foreach (var option in choice.Options)
                if (option.IsVisible(context.Timeline)) visible.Add(option);
            return visible;
        }

        /// <summary>Valid on a CheckNode. Rolls (once per checker -- more than one only for EntireParty), logs,
        /// branches, and returns every roll for the caller to display.</summary>
        public DialogueCheckOutcome PerformCheck()
        {
            if (CurrentNode is not CheckNode check)
                throw new InvalidOperationException($"PerformCheck() isn't valid on a {CurrentNode.GetType().Name}.");

            var checkers = DialogueCheckResolver.ResolveCheckers(
                check.Performer, context.NpcId, context.Party, context.Characters, context.RulesDatabase, check.QueryKey);

            var skillCheckRule = new DefaultSkillCheckRule(context.Characters, context.RulesDatabase);
            var rolls = new List<RollResult>(checkers.Count);
            foreach (var checkerId in checkers)
            {
                var bonus = skillCheckRule.ComputeSkillCheckBonus(checkerId, check.QueryKey);
                var advantage = skillCheckRule.ComputeAdvantage(checkerId, check.QueryKey);
                var modifiers = bonus != 0 ? new[] { new RollModifier(check.QueryKey, bonus) } : null;

                var roll = context.DiceService.Roll(new RollRequest(new DiceExpression(1, 20), RollPurpose.AbilityCheck, advantage, source: checkerId, modifiers: modifiers));
                context.CombatLog?.Log(new RollLogEntry(roll));
                rolls.Add(roll);
            }

            var success = DialogueCheckResolver.Succeeds(check.Performer, rolls, check.DifficultyClass);
            MoveTo(success ? check.SuccessNodeId : check.FailureNodeId);
            return new DialogueCheckOutcome(success, rolls);
        }

        private void MoveTo(string nodeId)
        {
            CurrentNode = nodesById[nodeId];
            Settle();
        }

        private void Settle()
        {
            while (true)
            {
                switch (CurrentNode)
                {
                    case BranchNode branch:
                        CurrentNode = nodesById[branch.Evaluate(context.Timeline) ? branch.TrueNodeId : branch.FalseNodeId];
                        continue;
                    case ActionNode action:
                        action.Action?.Execute(context);
                        CurrentNode = nodesById[action.NextNodeId];
                        continue;
                    default:
                        return;
                }
            }
        }
    }
}
