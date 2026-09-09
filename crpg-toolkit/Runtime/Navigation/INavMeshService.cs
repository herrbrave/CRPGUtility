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
    }
}
