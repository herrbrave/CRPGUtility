using UnityEngine;

namespace CRPG.Core
{
    /// <summary>Task 4.2: whether two world points can see each other -- lives alongside IPositionProvider for
    /// the same reason (CRPG.Abilities and CRPG.AI both need it, CRPG.AI already depends on CRPG.Abilities, so
    /// it can't live in either without a circular reference). Deliberately just two Vector3s, not two
    /// RuntimeEntityIds -- a concrete implementation only needs geometry, not entity lookup, and callers
    /// already have positions in hand via IPositionProvider by the time they'd need this.</summary>
    public interface ILineOfSightProvider
    {
        bool HasLineOfSight(Vector3 from, Vector3 to);
    }
}
