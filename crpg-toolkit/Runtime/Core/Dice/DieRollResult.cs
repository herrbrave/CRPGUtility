namespace CRPG.Core
{
    /// <summary>One physical die's result. <see cref="Kept"/> is false for the discarded half of an
    /// advantage/disadvantage roll (kept for the "reveal individual rolls" combat-log UX).</summary>
    public readonly struct DieRollResult
    {
        public DieRollResult(int sides, int value, bool kept)
        {
            Sides = sides;
            Value = value;
            Kept = kept;
        }

        public int Sides { get; }
        public int Value { get; }
        public bool Kept { get; }
    }
}
