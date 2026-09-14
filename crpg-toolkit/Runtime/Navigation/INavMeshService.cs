using UnityEngine;
using UnityEngine.AI;

namespace CRPG.Navigation
{
    /// <summary>Thin, testable wrapper over UnityEngine.AI.NavMesh, so higher-level code (formation
    /// resolution, movement-range previews) can be unit-tested against a fake without a baked NavMesh.</summary>
    public interface INavMeshService
    {
        bool TrySamplePosition(Vector3 point, out Vector3 sampledPosition, float maxDistance = 5f, int areaMask = NavMesh.AllAreas);

        bool TryCalculatePathLength(Vector3 from, Vector3 to, out float length, int areaMask = NavMesh.AllAreas);

        /// <summary>Task 4.3: the real corner list for a path, needed because <see cref="CRPG.Core.MotionSnapshot"/>/
        /// <see cref="IMovable"/> deliberately only expose remaining-distance-to-final-destination (Task 2.1's
        /// contract), not intermediate corners -- CRPG.Gameplay.Actions.MoveActionExecution needs the corners
        /// themselves up front to plan segment-by-segment stops. Only a complete path counts as success (same
        /// convention as <see cref="TryCalculatePathLength"/>) -- a partial path is reported as failure here,
        /// not a truncated corner list.</summary>
        bool TryCalculatePath(Vector3 from, Vector3 to, out Vector3[] corners, int areaMask = NavMesh.AllAreas);
    }
}
