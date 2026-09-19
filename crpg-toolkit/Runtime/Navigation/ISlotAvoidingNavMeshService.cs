using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

namespace CRPG.Navigation
{
    /// <summary>
    /// Optional capability of an <see cref="INavMeshService"/> whose characters cannot share a spot (a grid backend:
    /// one character per cell). Party formation resolution resolves each member independently, so with several
    /// formation slots closer together than one cell they would all snap to the SAME cell and all but one member
    /// would then be blocked from ever arriving. A backend that implements this lets the resolver ask for "the
    /// nearest place that isn't already taken" instead. A backend where characters can overlap (NavMesh, which
    /// has agent avoidance) simply doesn't implement it and behaves exactly as before.
    /// </summary>
    public interface ISlotAvoidingNavMeshService : INavMeshService
    {
        /// <summary>Like <see cref="INavMeshService.TrySamplePosition"/>, but never returns a spot equivalent to
        /// any of <paramref name="claimed"/> (what "equivalent" means is the backend's business -- a grid treats two
        /// positions in the same cell as the same spot). Fails if there is no free spot within
        /// <paramref name="maxDistance"/>.</summary>
        bool TrySamplePositionAvoiding(Vector3 point, out Vector3 sampledPosition, float maxDistance, IReadOnlyCollection<Vector3> claimed, int areaMask = NavMesh.AllAreas);
    }
}
