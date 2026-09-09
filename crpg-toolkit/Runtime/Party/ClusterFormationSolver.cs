using System.Collections.Generic;
using UnityEngine;

namespace CRPG.Party
{
    /// <summary>Loose ring around the destination -- no strict ordering, minimal walking distance for a tight group.</summary>
    public sealed class ClusterFormationSolver : IFormationSolver
    {
        public IReadOnlyList<Vector3> Solve(Vector3 destination, Vector3 facing, int memberCount, float spacing)
        {
            var points = new List<Vector3>(memberCount);
            if (memberCount <= 0) return points;

            points.Add(destination);

            var ring = 1;
            var ringCapacity = 6;
            var placedInRing = 0;

            for (var i = 1; i < memberCount; i++)
            {
                if (placedInRing >= ringCapacity)
                {
                    ring++;
                    ringCapacity = 6 * ring;
                    placedInRing = 0;
                }

                var angle = (360f / ringCapacity) * placedInRing * Mathf.Deg2Rad;
                var radius = spacing * ring;
                var offset = new Vector3(Mathf.Sin(angle), 0f, Mathf.Cos(angle)) * radius;
                points.Add(destination + offset);
                placedInRing++;
            }

            return points;
        }
    }
}
