using System.Collections.Generic;

namespace CRPG.Core
{
    /// <summary>One (Behavior, Target) pair a UtilityAIDecisionProvider evaluated, and the score it got --
    /// this is the actual tune-a-curve/replay/read-the-trace debugging loop utility AI needs.</summary>
    public readonly struct UtilityDecisionTraceEntry
    {
        public UtilityDecisionTraceEntry(string behaviorName, RuntimeEntityId target, float score)
        {
            BehaviorName = behaviorName;
            Target = target;
            Score = score;
        }

        public string BehaviorName { get; }
        public RuntimeEntityId Target { get; }
        public float Score { get; }
    }

    /// <summary>Structured record of one Brain decision -- every Behavior/Target pair it scored, and which one
    /// won. Lives in CRPG.Core alongside the other *LogEntry types (ICombatLog is Core-level infrastructure);
    /// takes only primitive/Core data rather than a CRPG.AI TurnDecision reference, since CRPG.AI depends on
    /// CRPG.Core and not the other way around.</summary>
    public sealed class UtilityDecisionLogEntry : CombatLogEntry
    {
        public UtilityDecisionLogEntry(
            RuntimeEntityId self, RuntimeEntityId chosenTarget, string chosenBehaviorName, float chosenScore, IReadOnlyList<UtilityDecisionTraceEntry> trace)
            : base(self, chosenTarget)
        {
            ChosenBehaviorName = chosenBehaviorName;
            ChosenScore = chosenScore;
            Trace = trace;
        }

        public string ChosenBehaviorName { get; }
        public float ChosenScore { get; }
        public IReadOnlyList<UtilityDecisionTraceEntry> Trace { get; }

        public override string Summary() => Target.IsValid
            ? $"{Source} chooses {ChosenBehaviorName} against {Target} (score {ChosenScore:F2})."
            : $"{Source} has nothing worth doing this turn.";
    }
}
