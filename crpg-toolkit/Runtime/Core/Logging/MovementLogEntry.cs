namespace CRPG.Core
{
    public sealed class MovementLogEntry : CombatLogEntry
    {
        public MovementLogEntry(RuntimeEntityId source, RuntimeEntityId towardTarget, float distance) : base(source, towardTarget)
        {
            Distance = distance;
        }

        public float Distance { get; }

        public override string Summary() => $"{Source} moves {Distance:F1} units toward {Target}.";
    }
}
