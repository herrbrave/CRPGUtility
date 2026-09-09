namespace CRPG.Actors
{
    /// <summary>SRD5's passive-score formula: 10 + whatever modifier normally applies to the active check
    /// (e.g. Passive Perception = 10 + Perception skill check bonus).</summary>
    public static class PassiveScore
    {
        public static int From(int skillCheckBonus) => 10 + skillCheckBonus;
    }
}
