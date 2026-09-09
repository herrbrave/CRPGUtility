namespace CRPG.Core
{
    public sealed class TurnLogEntry : CombatLogEntry
    {
        public TurnLogEntry(RuntimeEntityId combatant, int round) : base(RuntimeEntityId.None, combatant)
        {
            Round = round;
        }

        public int Round { get; }

        public override string Summary() => $"Round {Round}: {Target}'s turn.";
    }
}
