namespace CRPG.Core
{
    /// <summary>Covers attack rolls, saving throws, ability checks and initiative alike -- they're all
    /// "a RollResult happened for some purpose" and don't need separate entry types.</summary>
    public sealed class RollLogEntry : CombatLogEntry
    {
        public RollLogEntry(RollResult result) : base(result.Request.Source, result.Request.Target)
        {
            Result = result;
        }

        public RollResult Result { get; }

        public override string Summary() => $"{Result.Request.Purpose}: {Result}";
    }
}
