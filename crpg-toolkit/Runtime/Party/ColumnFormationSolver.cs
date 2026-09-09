using System.Collections.Generic;
using UnityEngine;

namespace CRPG.Party
{
    /// <summary>Single file, trailing behind the destination point opposite the facing direction.</summary>
    public sealed class ColumnFormationSolver : IFormationSolver
    {
        public IReadOnlyList<Vector3> Solve(Vector3 destination, Vector3 facing, int memberCount, float spacing)
        {
            var points = new List<Vector3>(memberCount);
            if (memberCount <= 0) return points;

            var back = -FormationMath.FlattenFacing(facing);

            for (var i = 0; i < memberCount; i++)
                points.Add(destination + back * (i * spacing));

            return points;
        }
    }
}
