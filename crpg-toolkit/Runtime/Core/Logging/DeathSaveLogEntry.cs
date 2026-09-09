namespace CRPG.Core
{
    public sealed class DeathSaveLogEntry : CombatLogEntry
    {
        public DeathSaveLogEntry(RuntimeEntityId character, int naturalRoll, bool success, int successes, int failures)
            : base(character, character)
        {
            NaturalRoll = naturalRoll;
            Success = success;
            Successes = successes;
            Failures = failures;
        }

        public int NaturalRoll { get; }
        public bool Success { get; }
        public int Successes { get; }
        public int Failures { get; }

        public override string Summary() =>
            $"{Target} rolls a death save: {NaturalRoll} -- {(Success ? "Success" : "Failure")} ({Successes} successes, {Failures} failures)";
    }
}
