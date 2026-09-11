using UnityEngine;

namespace CRPG.Core
{
    /// <summary>
    /// Read-only motion state a mover exposes for other systems to consume -- "Expose a read-only motion
    /// snapshot from the motor: velocity, desired velocity, grounded/on-mesh validity, remaining path distance,
    /// move status and request ID. Animation consumes this, not NavMeshAgent directly" (Docs/Design/
    /// Production-Foundation.md sec.4.3). Deliberately backend-agnostic: a NavMesh-driven mover populates
    /// IsOnNavMesh from NavMeshAgent.isOnNavMesh, but nothing about this struct's shape assumes NavMesh
    /// specifically -- a future non-NavMesh mover can populate the same fields with its own equivalent notion
    /// of "grounded/valid."
    /// </summary>
    public readonly struct MotionSnapshot
    {
        public Vector3 Velocity { get; }
        public Vector3 DesiredVelocity { get; }
        public bool IsOnNavMesh { get; }
        public float RemainingDistance { get; }
        public MoveStatus Status { get; }
        public MoveRequestId RequestId { get; }

        public MotionSnapshot(Vector3 velocity, Vector3 desiredVelocity, bool isOnNavMesh, float remainingDistance, MoveStatus status, MoveRequestId requestId)
        {
            Velocity = velocity;
            DesiredVelocity = desiredVelocity;
            IsOnNavMesh = isOnNavMesh;
            RemainingDistance = remainingDistance;
            Status = status;
            RequestId = requestId;
        }

        /// <summary>No mover bound, or the mover has never received a request -- zero velocity, Arrived (the
        /// character is exactly where it should be), no request.</summary>
        public static readonly MotionSnapshot None = new(Vector3.zero, Vector3.zero, false, 0f, MoveStatus.Arrived, MoveRequestId.None);
    }
}
