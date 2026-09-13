using UnityEngine;

namespace CRPG.Party
{
    /// <summary>Whether a formation-slot destination for one member of a <see cref="PartyMoveOrder"/> is
    /// actually reachable from that member's current position -- <see cref="Exact"/> the original slot,
    /// <see cref="Fallback"/> a practical nearby point found within a bounded retry, or
    /// <see cref="Unreachable"/> (nothing was found; the member's previous movement, if any, is left
    /// untouched rather than being sent somewhere meaningless).</summary>
    public enum FormationResolutionOutcome
    {
        Exact,
        Fallback,
        Unreachable
    }

    /// <summary>One member's resolved (or failed) formation destination. <see cref="ResolvedDestination"/> is
    /// null if and only if <see cref="Outcome"/> is <see cref="FormationResolutionOutcome.Unreachable"/> --
    /// this is what makes an unreachable formation slot a reportable, truthful outcome instead of silently
    /// standing in for a successful move.</summary>
    public readonly struct FormationDestinationResolution
    {
        public FormationDestinationResolution(Vector3 requestedDestination, Vector3? resolvedDestination, FormationResolutionOutcome outcome)
        {
            RequestedDestination = requestedDestination;
            ResolvedDestination = resolvedDestination;
            Outcome = outcome;
        }

        public Vector3 RequestedDestination { get; }
        public Vector3? ResolvedDestination { get; }
        public FormationResolutionOutcome Outcome { get; }
    }
}
