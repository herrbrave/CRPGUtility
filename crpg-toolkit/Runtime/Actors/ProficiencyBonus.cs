namespace CRPG.Actors
{
    /// <summary>SRD5's proficiency-bonus-by-level formula: +2 at levels 1-4, +3 at 5-8, +4 at 9-12, +5 at 13-16,
    /// +6 at 17-20.</summary>
    public static class ProficiencyBonus
    {
        public static int From(int level) => 2 + (level - 1) / 4;
    }
}
