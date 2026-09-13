namespace CRPG.Gameplay.Interactions
{
    /// <summary>How one <see cref="InteractionRequest"/> ended -- reported via
    /// <see cref="InteractionController.RequestEnded"/> so tests (and, eventually, UI) can tell success apart
    /// from every distinct way it can fail, rather than a single opaque "it didn't work."</summary>
    public enum InteractionOutcome
    {
        /// <summary>The adapter's execution reported success.</summary>
        Completed,

        /// <summary>The adapter's execution reported failure (e.g. a failed Warp, an already-empty container).</summary>
        Failed,

        /// <summary>Ended by an explicit Cancel (player input, or a host being disabled/destroyed).</summary>
        Cancelled,

        /// <summary>The approach never reached Arrived -- NavMesh reported Blocked/Invalid/Cancelled instead.</summary>
        Blocked,

        /// <summary>Arrived, but the resolved stop point is still outside the target's InteractionRange.</summary>
        ApproachInsufficient,

        /// <summary>The approach never reached a terminal MoveStatus within the configured timeout.</summary>
        ApproachTimedOut,

        /// <summary>The target was destroyed or disabled mid-request.</summary>
        TargetLost,

        /// <summary>The performer was destroyed, disabled, unbound, or rebound to a different session mid-request.</summary>
        PerformerLost,

        /// <summary>The adapter (or its configuration) is missing, or IsAvailable returned false when checked.</summary>
        Unavailable,

        /// <summary>An accepted new move order (a genuinely different MoveRequestId) or a fresh Interact command
        /// took ownership of the performer before this request finished.</summary>
        Superseded,

        /// <summary>The owning GameSession stopped accepting commands (disposed) mid-request.</summary>
        SessionEnded
    }
}
