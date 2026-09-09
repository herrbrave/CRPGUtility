using System.Collections.Generic;
using UnityEngine;

namespace CRPG.Party
{
    /// <summary>V-shape: the first member takes point at the destination, the rest fan out behind in alternating pairs.</summary>
    public sealed class WedgeFormationSolver : IFormationSolver
    {
        public IReadOnlyList<Vector3> Solve(Vector3 destination, Vector3 facing, int memberCount, float spacing)
        {
            var points = new List<Vector3>(memberCount);
            if (memberCount <= 0) return points;

            points.Add(destination);
            if (memberCount == 1) return points;

            var flatFacing = FormationMath.FlattenFacing(facing);
            var right = FormationMath.RightOf(flatFacing);
            var back = -flatFacing;

            var rank = 1;
            var side = 1;
            for (var i = 1; i < memberCount; i++)
            {
                var lateral = right * (side * rank * spacing * 0.5f);
                var depth = back * (rank * spacing * 0.75f);
                points.Add(destination + depth + lateral);

                side = -side;
                if (side == 1) rank++; // completed a left/right pair, drop back a row
            }

            return points;
        }
    }
}
