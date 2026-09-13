namespace CRPG.Saving
{
    /// <summary>Moved here from CRPG.Gameplay (Task 3.6) so <see cref="ISaveRequestPort"/> -- and a CRPG.UI
    /// SavePresenter that depends only on it -- never needs to reference CRPG.Gameplay directly (which would
    /// create a cycle, since CRPG.Gameplay already references CRPG.UI). Pure data, no Gameplay-type references.</summary>
    public enum SaveRequestOutcome { Success, Blocked, WriteFailed }

    public sealed class SaveRequestResult
    {
        private SaveRequestResult(SaveRequestOutcome outcome, SafePointBlockReason blockReason, string message)
        {
            Outcome = outcome;
            BlockReason = blockReason;
            Message = message;
        }

        public SaveRequestOutcome Outcome { get; }
        public SafePointBlockReason BlockReason { get; }
        public string Message { get; }

        public static SaveRequestResult Ok() => new(SaveRequestOutcome.Success, SafePointBlockReason.None, null);
        public static SaveRequestResult Blocked(SafePointBlockReason reason) => new(SaveRequestOutcome.Blocked, reason, null);
        public static SaveRequestResult WriteFailed(string message) => new(SaveRequestOutcome.WriteFailed, SafePointBlockReason.None, message);
    }
}
