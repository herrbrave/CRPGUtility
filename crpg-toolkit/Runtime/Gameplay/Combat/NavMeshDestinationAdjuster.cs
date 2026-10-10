using System;
using System.Collections.Generic;
using CRPG.Core;
using UnityEngine;
using UnityEngine.AI;

namespace CRPG.Gameplay.Combat
{
    /// <summary>
    /// A <see cref="TacticalPlayerMoveAdapter"/> destination adjuster for NavMesh games. A click in a fight is turned into a destination a move can actually ARRIVE at, because a move that never
    /// arrives blocks the AI driver, End Turn and the targeting gate: (1) the click is snapped onto the NavMesh within <see cref="SampleRadius"/> metres, or refused ("You cannot go there.");
    /// (2) it is pushed out of the footprint of every OTHER combatant (their <c>NavMeshAgent.radius</c> plus the mover's plus <see cref="Clearance"/>), because two agents ordered onto the
    /// same spot sit about 0.2 m apart (radius 0.35 each) and can never both arrive. A character without a <c>NavMeshAgent</c> counts as <see cref="DefaultRadius"/>.
    /// </summary>
    public static class NavMeshDestinationAdjuster
    {
        public const float SampleRadius = 1.5f, Clearance = 0.2f, DefaultRadius = 0.35f;
        public const string OffMeshMessage = "You cannot go there.";

        /// <param name="others">The characters to keep clear of (typically the encounter's combatants); the mover itself is skipped if listed.</param>
        public static Func<RuntimeEntityId, Vector3, (Vector3 Destination, string Refusal)> Create(Func<IEnumerable<RuntimeEntityId>> others) =>
            (mover, destination) => Adjust(mover, destination, others());

        public static (Vector3 Destination, string Refusal) Adjust(RuntimeEntityId mover, Vector3 destination, IEnumerable<RuntimeEntityId> others)
        {
            if (!NavMesh.SamplePosition(destination, out var hit, SampleRadius, NavMesh.AllAreas)) return (destination, OffMeshMessage);
            destination = hit.position;

            var moverAgent = FindAgent(mover);
            var myRadius = Radius(moverAgent);
            foreach (var id in others)
            {
                if (id == mover) continue;
                var other = FindAgent(id);
                if (other == null) continue;

                var min = myRadius + Radius(other) + Clearance;
                var away = destination - other.transform.position; away.y = 0f;
                if (away.magnitude >= min) continue;

                if (away.sqrMagnitude < 1e-4f && moverAgent != null) away = moverAgent.transform.position - other.transform.position;   // exactly on top: push toward where the mover is coming from
                destination = other.transform.position + away.normalized * min;
                if (NavMesh.SamplePosition(destination, out var pushed, SampleRadius, NavMesh.AllAreas)) destination = pushed.position;
            }

            return (destination, null);
        }

        // TryGetComponent, not GetComponent + a null pattern: in the Editor a missing component comes back as a "fake null" that passes a C# null check and throws on first use
        private static float Radius(CharacterAgent agent) => agent != null && agent.TryGetComponent<NavMeshAgent>(out var nav) ? nav.radius : DefaultRadius;

        private static CharacterAgent FindAgent(RuntimeEntityId id)
        {
            foreach (var agent in CharacterAgent.AllLive)
            {
                if (agent != null && agent.EntityId == id) return agent;
            }

            return null;
        }
    }
}
