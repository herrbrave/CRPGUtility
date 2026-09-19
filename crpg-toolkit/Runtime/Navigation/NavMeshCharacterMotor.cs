using CRPG.Core;
using UnityEngine;
using UnityEngine.AI;

namespace CRPG.Navigation
{
    /// <summary>Reference <see cref="IMovable"/> backed by a NavMeshAgent on the same GameObject.</summary>
    [RequireComponent(typeof(NavMeshAgent))]
    public sealed class NavMeshCharacterMotor : MonoBehaviour, IMovable
    {
        [SerializeField] private float arrivalThreshold = 0.1f;

        [Tooltip("Max distance from the NavMesh at which Warp will still snap the character onto it; farther is refused.")]
        [SerializeField] private float warpSampleMaxDistance = 1f;

        private NavMeshAgent agent;
        private MoveRequestId currentRequestId = MoveRequestId.None;
        private bool currentRequestCancelled;

        private void Awake() => agent = GetComponent<NavMeshAgent>();

        public MoveResult SetDestination(Vector3 destination)
        {
            // Validate before superseding: only touch currentRequestId/currentRequestCancelled once the
            // agent has actually accepted the request, so a rejected destination leaves whatever was
            // previously in flight (if anything) completely untouched.
            if (agent == null || !agent.isActiveAndEnabled || !agent.isOnNavMesh) return MoveResult.Invalid();
            if (!agent.SetDestination(destination)) return MoveResult.Invalid();

            currentRequestId = MoveRequestId.NewId();
            currentRequestCancelled = false;
            return new MoveResult(MoveStatus.Pending, currentRequestId);
        }

        public void Stop()
        {
            if (agent == null) return;
            agent.ResetPath();
            if (currentRequestId.IsValid) currentRequestCancelled = true;
        }

        public bool Warp(Vector3 position)
        {
            if (agent == null || !agent.isActiveAndEnabled) return false;

            // Refuse WITHOUT touching the agent. Raw NavMeshAgent.Warp writes transform.position even when it
            // fails (stranding the agent off-mesh and destroying its path), and it accepts points up to ~20x
            // agent.radius away from the mesh, then silently relocates the character on the next frame. Sampling
            // first makes a refusal free (an in-flight request is untouched) and makes "true" mean the
            // character is really standing on the mesh.
            if (!NavMesh.SamplePosition(position, out var hit, warpSampleMaxDistance, agent.areaMask)) return false;

            var previous = transform.position;
            if (!agent.Warp(hit.position))
            {
                // Defensive: should not happen after a successful sample. Undo Warp's unconditional transform
                // write and try to re-place the agent where it was.
                transform.position = previous;
                agent.Warp(previous);
                return false;
            }

            if (currentRequestId.IsValid) currentRequestCancelled = true;
            return true;
        }

        public MotionSnapshot Snapshot
        {
            get
            {
                if (agent == null || !agent.isOnNavMesh)
                    return new MotionSnapshot(Vector3.zero, Vector3.zero, false, 0f, MoveStatus.Invalid, currentRequestId);

                if (!currentRequestId.IsValid) return MotionSnapshot.None;
                if (currentRequestCancelled)
                    return new MotionSnapshot(agent.velocity, agent.desiredVelocity, true, RemainingDistanceOrInfinity(), MoveStatus.Cancelled, currentRequestId);

                if (agent.pathPending)
                    return new MotionSnapshot(agent.velocity, agent.desiredVelocity, true, float.PositiveInfinity, MoveStatus.Pending, currentRequestId);

                if (agent.pathStatus == NavMeshPathStatus.PathInvalid)
                    return new MotionSnapshot(agent.velocity, agent.desiredVelocity, true, agent.remainingDistance, MoveStatus.Invalid, currentRequestId);

                var hasArrivedAtCurrentPathsEnd = agent.remainingDistance <= Mathf.Max(arrivalThreshold, agent.stoppingDistance);
                if (hasArrivedAtCurrentPathsEnd)
                {
                    // A partial path's "end" is the closest reachable point, not the requested destination --
                    // never reported as Arrived. See MoveStatus.Blocked.
                    var status = agent.pathStatus == NavMeshPathStatus.PathComplete ? MoveStatus.Arrived : MoveStatus.Blocked;
                    return new MotionSnapshot(agent.velocity, agent.desiredVelocity, true, agent.remainingDistance, status, currentRequestId);
                }

                return new MotionSnapshot(agent.velocity, agent.desiredVelocity, true, agent.remainingDistance, MoveStatus.Moving, currentRequestId);
            }
        }

        private float RemainingDistanceOrInfinity() => agent.pathPending ? float.PositiveInfinity : agent.remainingDistance;
    }
}
