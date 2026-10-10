namespace VikingRaid.Game
{
    /// <summary>
    /// Catches a move that can never arrive (review blocker 1). The toolkit's NavMesh motor reports Arrived only within max(0.1, stopping distance) of the destination and
    /// <c>MoveActionExecution</c> has no timeout, while a move runs the AI driver, End Turn and the targeting gate all wait. Two grunts ordered onto the same hero end up ~0.2 m apart
    /// (agent radius 0.35, so they can never both arrive) and would jitter forever. The watchdog says "cancel it" when the move outlives what its budget could take, or when the agent
    /// has barely moved for a while; cancelling settles the distance actually walked.
    /// </summary>
    public sealed class MoveWatchdog
    {
        public const float StallSpeed = 0.05f, StallSeconds = 0.5f, Slack = 1.0f, NoProgressSeconds = 1.0f, ProgressEpsilon = 0.25f;

        private readonly float limitSeconds;
        private float elapsed, stalled, bestRemaining = float.PositiveInfinity, sinceProgress;

        /// <param name="budgetMetres">The most this move may cover (the mover's remaining movement budget).</param>
        /// <param name="speed">The agent's speed in metres per second.</param>
        public MoveWatchdog(float budgetMetres, float speed) => limitSeconds = (speed > 0.01f ? budgetMetres / speed : 0f) + Slack;

        public float Elapsed => elapsed;

        /// <summary>Advances one frame; true means cancel the move now. <paramref name="currentSpeed"/> is the agent's actual speed this frame; <paramref name="remainingDistance"/> is its path
        /// distance still to go (infinity while the path is pending). A mover that wobbles in a crowd keeps a non-zero speed but never gets closer, so "no progress" is judged on the remaining distance too.</summary>
        public bool ShouldCancel(float deltaTime, float currentSpeed, float remainingDistance = float.PositiveInfinity)
        {
            elapsed += deltaTime;
            stalled = currentSpeed < StallSpeed ? stalled + deltaTime : 0f;
            if (!float.IsInfinity(remainingDistance))
            {
                if (remainingDistance < bestRemaining - ProgressEpsilon) { bestRemaining = remainingDistance; sinceProgress = 0f; }
                else sinceProgress += deltaTime;
            }

            // a move legitimately starts from rest and decelerates into its destination, so only count a stall after the move has had a moment to get going
            return elapsed > limitSeconds || (elapsed > 0.5f && stalled >= StallSeconds) || (elapsed > 0.5f && sinceProgress >= NoProgressSeconds);
        }
    }
}
