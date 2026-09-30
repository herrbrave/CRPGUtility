using CRPG.Core;
using CRPG.Gameplay;
using UnityEngine;

namespace BarrowsEnd.Gate
{
    /// <summary>Task 6.1: <c>MovementResolutionService</c>'s write-side <c>IMovementController</c> for
    /// BarrowsEnd's grid stack -- no toolkit implementation of this interface exists for grid movement (only a
    /// NavMesh-agent one and test fixtures; confirmed by search), and the interface's own doc comment names
    /// exactly this pattern as the intended shape: "the 2D test scene implements it by moving a Transform
    /// directly." A synchronous <see cref="CRPG.Navigation.IMovable.Warp"/> (not an animated
    /// <c>SetDestination</c>) on purpose -- <see cref="CRPG.Combat.MovementResolutionService.MoveToward"/> reads
    /// the return value as "how far the entity actually moved" and immediately spends that much of the
    /// Movement resource, so the position must already be final by the time this returns; an animated walk
    /// would leave <see cref="LiveCharacterPositionProvider"/> reporting a stale position for the next
    /// range/line-of-sight check in the same turn. A real walk animation during a tactical move is a flagged
    /// visual simplification for this task, not a functional gap.</summary>
    public sealed class TacticalMovementController : IMovementController
    {
        public float TryMoveTowards(RuntimeEntityId entity, Vector3 destination, float maxDistance)
        {
            var agent = GateSessionServices.FindAgent(entity);
            if (agent == null || agent.Motor == null) return 0f;

            var from = agent.transform.position;
            var toDestination = destination - from;
            var fullDistance = toDestination.magnitude;
            if (fullDistance <= 0f) return 0f;

            var moveDistance = Mathf.Min(fullDistance, maxDistance);
            var to = from + toDestination.normalized * moveDistance;
            agent.Motor.Warp(to);
            return moveDistance;
        }
    }

    /// <summary>Task 6.1: <c>IPositionProvider</c> for BarrowsEnd's live world -- reads a combatant's real,
    /// current transform position via the same <see cref="GateSessionServices.FindAgent"/> lookup every other
    /// live-agent query in this project already uses. <see cref="Vector3.positiveInfinity"/> for an unresolved
    /// entity (not <see cref="Vector3.zero"/>, which would put a missing combatant at the world origin and make
    /// every range/distance check against them silently wrong instead of obviously wrong).</summary>
    public sealed class LiveCharacterPositionProvider : IPositionProvider
    {
        public Vector3 GetPosition(RuntimeEntityId entity)
        {
            var agent = GateSessionServices.FindAgent(entity);
            return agent != null ? agent.transform.position : Vector3.positiveInfinity;
        }
    }
}
