namespace CRPG.Core
{
    public sealed class DamageLogEntry : CombatLogEntry
    {
        public DamageLogEntry(RuntimeEntityId source, RuntimeEntityId target, int amount, int remainingAfter) : base(source, target)
        {
            Amount = amount;
            RemainingAfter = remainingAfter;
        }

        public int Amount { get; }
        public int RemainingAfter { get; }

        public override string Summary() => $"{Target} takes {Amount} damage ({RemainingAfter} remaining).";
    }
}
