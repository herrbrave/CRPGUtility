using System;
using System.Collections.Generic;
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
        /// <summary>DialogueValidator should catch a malformed auto-settle cycle at author time; this is the
        /// runtime's own backstop for content that slipped through un-validated -- cuts a hang short with a
        /// diagnosable exception instead of freezing the game/editor.</summary>
        private const int MaxAutoSettleSteps = 256;

        private readonly DialogueDefinition definition;
        private readonly DialogueContext context;
        private readonly Dictionary<SerializableGuid, DialogueNode> nodesById;

        public DialogueRunner(DialogueDefinition definition, DialogueContext context)
        {
            this.definition = definition;
            this.context = context;
            nodesById = new Dictionary<SerializableGuid, DialogueNode>();
            foreach (var node in definition.Nodes)
            {
                if (!nodesById.TryAdd(node.NodeId, node))
                    throw new DialogueGraphException($"Dialogue '{definition.DisplayName}' has more than one node with id '{node.NodeId}'.");
            }

            CurrentNode = ResolveNode(definition.StartNodeId, "StartNodeId");
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

        /// <summary>Valid on a CheckNode. Looks at the check without rolling it: who rolls, with what bonus (named parts when the rule can name them), advantage and die. The conversation does not move.</summary>
        public CheckPrompt PrepareCheck()
        {
            if (CurrentNode is not CheckNode check)
                throw new InvalidOperationException($"PrepareCheck() isn't valid on a {CurrentNode.GetType().Name}.");
            if (context.SkillCheckRule == null)
                throw new InvalidOperationException("PrepareCheck() requires DialogueContext.SkillCheckRule to be set.");

            var checkers = DialogueCheckResolver.ResolveCheckers(
                check.Performer, context.NpcId, context.Party, context.Characters, context.SkillCheckRule, check.QueryKey);
            var prompts = new List<CheckerPrompt>(checkers.Count);
            foreach (var checkerId in checkers)
            {
                var bonus = context.SkillCheckRule.ComputeSkillCheckBonus(checkerId, check.QueryKey);
                IReadOnlyList<CRPG.Actors.CheckBonusPart> parts = context.SkillCheckRule is CRPG.Actors.IDetailedSkillCheckRule detailed
                    ? detailed.ComputeBreakdown(checkerId, check.QueryKey)
                    : new[] { new CRPG.Actors.CheckBonusPart(check.QueryKey, bonus) };
                prompts.Add(new CheckerPrompt(checkerId, parts, bonus, context.SkillCheckRule.ComputeAdvantage(checkerId, check.QueryKey), context.SkillCheckRule.CheckDie));
            }

            return new CheckPrompt(check.QueryKey, check.DifficultyClass, check.Performer, prompts);
        }

        /// <summary>Valid on a CheckNode. Rolls (once per checker -- more than one only for EntireParty), logs,
        /// branches, and returns every roll for the caller to display. <paramref name="options"/> are the player's extras (null = none).</summary>
        public DialogueCheckOutcome PerformCheck(CheckRollOptions options = null)
        {
            if (CurrentNode is not CheckNode check)
                throw new InvalidOperationException($"PerformCheck() isn't valid on a {CurrentNode.GetType().Name}.");

            // Task 4.1: no fallback construction of a concrete rule here -- a context built without one means
            // this conversation was never wired for checks, and that must fail loudly, not silently reintroduce
            // the exact SRD5 coupling this DI seam removes.
            if (context.SkillCheckRule == null)
                throw new InvalidOperationException("PerformCheck() requires DialogueContext.SkillCheckRule to be set.");

            var checkers = DialogueCheckResolver.ResolveCheckers(
                check.Performer, context.NpcId, context.Party, context.Characters, context.SkillCheckRule, check.QueryKey);

            var rolls = new List<RollResult>(checkers.Count);
            foreach (var checkerId in checkers)
            {
                var bonus = context.SkillCheckRule.ComputeSkillCheckBonus(checkerId, check.QueryKey);
                var advantage = context.SkillCheckRule.ComputeAdvantage(checkerId, check.QueryKey);
                var modifiers = new List<RollModifier>();
                if (bonus != 0) modifiers.Add(new RollModifier(check.QueryKey, bonus));
                if (options != null)
                {
                    modifiers.AddRange(options.ExtraModifiers);
                    advantage = AdvantageStateExtensions.Combine(new[] { advantage, options.ExtraAdvantage });
                }

                var roll = context.DiceService.Roll(new RollRequest(context.SkillCheckRule.CheckDie, RollPurpose.AbilityCheck, advantage, source: checkerId, modifiers: modifiers.Count > 0 ? modifiers : null));
                context.CombatLog?.Log(new RollLogEntry(roll));
                rolls.Add(roll);
            }

            var success = DialogueCheckResolver.Succeeds(check.Performer, rolls, check.DifficultyClass);
            MoveTo(success ? check.SuccessNodeId : check.FailureNodeId);
            return new DialogueCheckOutcome(success, rolls);
        }

        private void MoveTo(SerializableGuid nodeId)
        {
            CurrentNode = ResolveNode(nodeId, $"a reference from node '{CurrentNode.NodeId}'");
            Settle();
        }

        private DialogueNode ResolveNode(SerializableGuid nodeId, string fromField)
        {
            if (!nodesById.TryGetValue(nodeId, out var node))
                throw new DialogueGraphException($"Dialogue '{definition.DisplayName}': {fromField} points to node id '{nodeId}', which doesn't exist.");
            return node;
        }

        private void Settle()
        {
            for (var step = 0; step < MaxAutoSettleSteps; step++)
            {
                switch (CurrentNode)
                {
                    case BranchNode branch:
                        CurrentNode = ResolveNode(branch.Evaluate(context) ? branch.TrueNodeId : branch.FalseNodeId, $"BranchNode '{branch.NodeId}'");
                        continue;
                    case ActionNode action:
                        action.Action?.Execute(context);
                        CurrentNode = ResolveNode(action.NextNodeId, $"ActionNode '{action.NodeId}'");
                        continue;
                    default:
                        return;
                }
            }

            throw new DialogueGraphException(
                $"Dialogue '{definition.DisplayName}' exceeded {MaxAutoSettleSteps} automatic Branch/Action steps without reaching a Line/Choice/Check/End node -- likely an authored cycle. Last node reached: '{CurrentNode.NodeId}'.");
        }
    }
}
