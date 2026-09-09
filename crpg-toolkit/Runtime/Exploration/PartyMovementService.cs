using System.Collections.Generic;
using CRPG.Core;
using CRPG.Navigation;
using CRPG.Party;
using UnityEngine;

namespace CRPG.Exploration
{
    /// <summary>Resolves a PartyMoveOrder's formation points onto the NavMesh, so members don't get sent to a
    /// point off the walkable surface.</summary>
    public sealed class PartyMovementService
    {
        private readonly INavMeshService navMesh;
        private readonly float sampleRadius;

        public PartyMovementService(INavMeshService navMesh, float sampleRadius = 5f)
        {
            this.navMesh = navMesh;
            this.sampleRadius = sampleRadius;
        }

        public IReadOnlyDictionary<RuntimeEntityId, Vector3> ResolveDestinations(PartyMoveOrder order)
        {
            var resolved = new Dictionary<RuntimeEntityId, Vector3>(order.Assignments.Count);

            foreach (var assignment in order.Assignments)
            {
                var destination = navMesh.TrySamplePosition(assignment.Value, out var sampled, sampleRadius)
                    ? sampled
                    : assignment.Value;
                resolved[assignment.Key] = destination;
            }

            return resolved;
        }
    }
}
