namespace CRPG.UI
{
    public enum PartyCommandOutcome { Success, NotSelected }

    public sealed class PartyCommandResult
    {
        private PartyCommandResult(PartyCommandOutcome outcome)
        {
            Outcome = outcome;
        }

        public PartyCommandOutcome Outcome { get; }

        public static PartyCommandResult Ok() => new(PartyCommandOutcome.Success);
        public static PartyCommandResult NotSelected() => new(PartyCommandOutcome.NotSelected);
    }
}
