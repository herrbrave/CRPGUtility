namespace CRPG.Gameplay.Scenes
{
    public sealed class SceneTravelResult
    {
        private SceneTravelResult(SceneTravelOutcome outcome, string message)
        {
            Outcome = outcome;
            Message = message;
        }

        public SceneTravelOutcome Outcome { get; }
        public string Message { get; }
        public bool Success => Outcome == SceneTravelOutcome.Success;

        public static SceneTravelResult Ok() => new(SceneTravelOutcome.Success, null);
        public static SceneTravelResult Fail(SceneTravelOutcome outcome, string message) => new(outcome, message);
    }
}
