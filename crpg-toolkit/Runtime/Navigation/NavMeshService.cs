using UnityEngine;
using UnityEngine.AI;

namespace CRPG.Navigation
{
    public sealed class NavMeshService : INavMeshService
    {
        public bool TrySamplePosition(Vector3 point, out Vector3 sampledPosition, float maxDistance = 5f, int areaMask = NavMesh.AllAreas)
        {
            if (NavMesh.SamplePosition(point, out var hit, maxDistance, areaMask))
            {
                sampledPosition = hit.position;
                return true;
            }

            sampledPosition = point;
            return false;
        }

        public bool TryCalculatePathLength(Vector3 from, Vector3 to, out float length, int areaMask = NavMesh.AllAreas)
        {
            var path = new NavMeshPath();
            if (NavMesh.CalculatePath(from, to, areaMask, path) && path.status == NavMeshPathStatus.PathComplete)
            {
                length = ComputeLength(path);
                return true;
            }

            length = 0f;
            return false;
        }

        private static float ComputeLength(NavMeshPath path)
        {
            var corners = path.corners;
            var length = 0f;
            for (var i = 1; i < corners.Length; i++)
                length += Vector3.Distance(corners[i - 1], corners[i]);
            return length;
        }
    }
}
