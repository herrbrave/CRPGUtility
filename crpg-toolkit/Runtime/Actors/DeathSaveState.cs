namespace CRPG.Actors
{
    /// <summary>Tracks an unconscious, dying character's death-saving-throw progress. Lives as a nullable field
    /// on CharacterState (null = not currently dying) rather than a ResourceState, since death saves aren't a
    /// spend-down pool -- they accumulate two independent counters and get wiped entirely on stabilizing, dying,
    /// or healing.</summary>
    public sealed class DeathSaveState
    {
        public int Successes { get; set; }
        public int Failures { get; set; }
    }
}
