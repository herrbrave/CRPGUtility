using System.Collections.Generic;
using UnityEngine;

namespace CRPG.Party
{
    /// <summary>
    /// Turns one shared destination into one world-space point per party member, so characters walk to
    /// formation slots instead of chasing a leader's exact transform (spec "Party formation"). Deliberately
    /// takes just a member count, not character data -- the caller pairs the returned points with member
    /// ids in order. Authoring a custom formation just means implementing this interface.
    /// </summary>
    public interface IFormationSolver
    {
        IReadOnlyList<Vector3> Solve(Vector3 destination, Vector3 facing, int memberCount, float spacing);
    }
}
