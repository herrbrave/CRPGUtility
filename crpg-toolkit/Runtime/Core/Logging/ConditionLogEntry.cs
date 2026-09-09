namespace CRPG.Core
{
    public sealed class ConditionLogEntry : CombatLogEntry
    {
        public ConditionLogEntry(RuntimeEntityId source, RuntimeEntityId target, DefinitionId conditionId, bool applied) : base(source, target)
        {
            ConditionId = conditionId;
            Applied = applied;
        }

        public DefinitionId ConditionId { get; }

        /// <summary>true = applied, false = removed.</summary>
        public bool Applied { get; }

        public override string Summary() => $"{Target} {(Applied ? "gains" : "loses")} {ConditionId}.";
    }
}
