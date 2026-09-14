using CRPG.Core;

namespace CRPG.Combat
{
    /// <summary>Task 4.3: one point along a planned movement segment where a mover's distance to
    /// <see cref="Reactor"/> crosses <see cref="MovementResolutionService"/>'s threatRange -- see
    /// <see cref="MovementResolutionService.FindThreatBoundaryCrossings"/>. <see cref="Entering"/> is true for
    /// a crossing from outside the threat range to inside it, false for the reverse (SRD5's own "leaving a
    /// threatened area" opportunity-attack trigger only cares about the false case --
    /// <see cref="MovementResolutionService.ResolveReactionForCrossing"/> is where that ruleset-specific
    /// distinction is actually applied, not here; this struct is a neutral geometric fact).</summary>
    public readonly struct ThreatBoundaryCrossing
    {
        public ThreatBoundaryCrossing(float distanceAlongSegment, RuntimeEntityId reactor, bool entering)
        {
            DistanceAlongSegment = distanceAlongSegment;
            Reactor = reactor;
            Entering = entering;
        }

        public float DistanceAlongSegment { get; }
        public RuntimeEntityId Reactor { get; }
        public bool Entering { get; }
    }
}
