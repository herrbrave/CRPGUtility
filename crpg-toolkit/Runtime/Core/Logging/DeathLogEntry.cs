namespace CRPG.Core
{
    public sealed class DeathLogEntry : CombatLogEntry
    {
        public DeathLogEntry(RuntimeEntityId source, RuntimeEntityId target) : base(source, target)
        {
        }

        public override string Summary() => $"{Target} falls.";
    }
}
