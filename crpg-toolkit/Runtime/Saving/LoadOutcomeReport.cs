namespace CRPG.Saving
{
    /// <summary>Thin, UI-facing summary of a load attempt -- deliberately does not carry the live
    /// GameSession/WorldSpawnService a full CRPG.Gameplay LoadResult exposes (a presenter has no legitimate use
    /// for those; only the composition root does, to run ISessionConsumer rebinding).</summary>
    public sealed class LoadOutcomeReport
    {
        public LoadOutcomeReport(LoadOutcome outcome, string message)
        {
            Outcome = outcome;
            Message = message;
        }

        public LoadOutcome Outcome { get; }
        public string Message { get; }
        public bool Success => Outcome == LoadOutcome.Success;
    }
}
