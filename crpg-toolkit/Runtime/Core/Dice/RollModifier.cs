namespace CRPG.Core
{
    /// <summary>A flat bonus/penalty applied to a roll's total, tagged with a human-readable source for the combat log.</summary>
    public readonly struct RollModifier
    {
        public RollModifier(string source, double value)
        {
            Source = source;
            Value = value;
        }

        public string Source { get; }
        public double Value { get; }
    }
}
