using System.Collections.Generic;
using CRPG.Core;
using UnityEngine;

namespace CRPG.Party
{
    /// <summary>
    /// A move command issued for a set of party members. Distributes one destination per member through an
    /// <see cref="IFormationSolver"/> instead of sending everyone to the same point. <see cref="Assignments"/>
    /// is ready to hand each member's NavMeshAgent its own destination once the Navigation assembly exists.
    /// </summary>
    public sealed class PartyMoveOrder
    {
        public PartyMoveOrder(IReadOnlyList<RuntimeEntityId> members, Vector3 destination, Vector3 facing, IFormationSolver formationSolver, float spacing = 1.5f)
        {
            Members = members;
            Destination = destination;
            Facing = facing;

            var points = formationSolver.Solve(destination, facing, members.Count, spacing);
            var assignments = new Dictionary<RuntimeEntityId, Vector3>(members.Count);
            for (var i = 0; i < members.Count; i++) assignments[members[i]] = points[i];
            Assignments = assignments;
        }

        public IReadOnlyList<RuntimeEntityId> Members { get; }
        public Vector3 Destination { get; }
        public Vector3 Facing { get; }
        public IReadOnlyDictionary<RuntimeEntityId, Vector3> Assignments { get; }
    }
}
