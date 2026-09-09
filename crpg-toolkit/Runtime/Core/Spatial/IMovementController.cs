using UnityEngine;

namespace CRPG.Core
{
    /// <summary>The write-side counterpart to <see cref="IPositionProvider"/>: actually relocates a
    /// combatant, rather than just reporting where it is. Kept as a separate interface (not a mutating method
    /// bolted onto IPositionProvider) since plenty of position sources are legitimately read-only -- a static
    /// prop, a formation slot -- and shouldn't be forced to implement movement. A NavMesh-driven game would
    /// implement this over a NavMeshAgent; the 2D test scene implements it by moving a Transform directly.</summary>
    public interface IMovementController
    {
        /// <returns>How far the entity actually moved (0 if it has no registered position or was already
        /// at/past the destination) -- callers use this to know how much of a movement budget was spent,
        /// since a request can be clamped short of maxDistance by the destination itself being closer.</returns>
        float TryMoveTowards(RuntimeEntityId entity, Vector3 destination, float maxDistance);
    }
}
