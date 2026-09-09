namespace CRPG.Core
{
    public sealed class SavingThrowLogEntry : CombatLogEntry
    {
        public SavingThrowLogEntry(RuntimeEntityId source, RuntimeEntityId target, RollResult roll, int difficultyClass, DefinitionId savingStatId, bool succeeded)
            : base(source, target)
        {
            Roll = roll;
            DifficultyClass = difficultyClass;
            SavingStatId = savingStatId;
            Succeeded = succeeded;
        }

        public RollResult Roll { get; }
        public int DifficultyClass { get; }
        public DefinitionId SavingStatId { get; }
        public bool Succeeded { get; }

        public override string Summary() =>
            $"{Target} makes a DC {DifficultyClass} {SavingStatId} saving throw: {Roll.Total} -- {(Succeeded ? "Success!" : "Failure.")}";
    }
}
