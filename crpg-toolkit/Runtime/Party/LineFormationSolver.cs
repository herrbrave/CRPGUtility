using System.Collections.Generic;
using UnityEngine;

namespace CRPG.Party
{
    /// <summary>Side by side, perpendicular to the facing direction, centered on the destination.</summary>
    public sealed class LineFormationSolver : IFormationSolver
    {
        public IReadOnlyList<Vector3> Solve(Vector3 destination, Vector3 facing, int memberCount, float spacing)
        {
            var points = new List<Vector3>(memberCount);
            if (memberCount <= 0) return points;

            var right = FormationMath.RightOf(FormationMath.FlattenFacing(facing));
            var startOffset = -(memberCount - 1) * 0.5f;

            for (var i = 0; i < memberCount; i++)
                points.Add(destination + right * ((startOffset + i) * spacing));

            return points;
        }
    }
}
