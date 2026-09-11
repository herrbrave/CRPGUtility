using System.Collections.Generic;
using CRPG.Navigation;
using UnityEngine;
using UnityEngine.AI;

namespace CRPG.Gameplay
{
    /// <summary>Facing priority tiers, highest wins -- "Facing priority: terminal/incapacitated policy ->
    /// committed action facing -> interaction facing -> motion facing -> idle facing. These are policy
    /// decisions of the action controller, not arbitrary transform writers" (Docs/Design/
    /// Production-Foundation.md sec.4.3). Only Motion is actually driven by this task; CommittedAction/
    /// Interaction/Terminal are extension points for Combat/Interaction/death-state work to request against
    /// later without changing FacingController's core loop. Idle needs no explicit request -- it's simply
    /// "nothing else is requesting," handled by TryGetHighestPriorityDirection returning false.</summary>
    public enum FacingPriority
    {
        Motion = 0,
        Interaction = 1,
        CommittedAction = 2,
        Terminal = 3,
    }

    /// <summary>
    /// Owns this character's visual orientation -- the sole writer of its Transform rotation, per ownership
    /// invariant #3 ("A facing controller alone writes its orientation; disable competing NavMeshAgent
    /// rotation"). Any system can request a facing direction at its own <see cref="FacingPriority"/>; the
    /// controller always turns toward whichever currently-active request has the highest priority, at an
    /// authored angular speed, and reports when it's within the facing-completion tolerance. Turning toward a
    /// lower-priority request never fights a higher-priority one -- only one direction is ever active.
    /// </summary>
    public sealed class FacingController : MonoBehaviour
    {
        [SerializeField] private float angularSpeedDegreesPerSecond = 360f;
        [SerializeField] private float facingCompletionToleranceDegrees = 1f;
        [SerializeField] private float movingVelocityThreshold = 0.1f;

        private IMovable movable;
        private readonly Dictionary<FacingPriority, Vector3> requestsByPriority = new();

        public bool IsFacingComplete { get; private set; } = true;

        private void Awake()
        {
            movable = GetComponent<IMovable>();

            var navAgent = GetComponent<NavMeshAgent>();
            if (navAgent != null) navAgent.updateRotation = false; // ownership invariant #3
        }

        /// <summary>Requests this controller turn toward <paramref name="direction"/> at <paramref name="priority"/>.
        /// Only the highest currently-active priority is ever honored. A near-zero direction is ignored
        /// (nothing to face). Callers own clearing their own request via <see cref="ClearFacing"/> once it no
        /// longer applies (e.g. an interaction ending) -- Motion clears itself automatically each frame.</summary>
        public void RequestFacing(FacingPriority priority, Vector3 direction)
        {
            direction.y = 0f;
            if (direction.sqrMagnitude < 0.0001f) return;
            requestsByPriority[priority] = direction.normalized;
        }

        public void ClearFacing(FacingPriority priority) => requestsByPriority.Remove(priority);

        private void Update()
        {
            UpdateMotionFacing();

            if (!TryGetHighestPriorityDirection(out var targetDirection))
            {
                IsFacingComplete = true;
                return;
            }

            var targetRotation = Quaternion.LookRotation(targetDirection, Vector3.up);
            IsFacingComplete = Quaternion.Angle(transform.rotation, targetRotation) <= facingCompletionToleranceDegrees;

            var maxDegreesThisFrame = angularSpeedDegreesPerSecond * Time.deltaTime;
            transform.rotation = Quaternion.RotateTowards(transform.rotation, targetRotation, maxDegreesThisFrame);
        }

        // The lowest tier: face the direction of travel while actually moving. Reads the bound IMovable's
        // MotionSnapshot rather than a concrete NavMeshAgent -- "Animation consumes this, not NavMeshAgent
        // directly" (design sec.4.3) applies just as much to facing as to animation.
        private void UpdateMotionFacing()
        {
            if (movable == null)
            {
                ClearFacing(FacingPriority.Motion);
                return;
            }

            var velocity = movable.Snapshot.Velocity;
            if (velocity.sqrMagnitude > movingVelocityThreshold * movingVelocityThreshold)
                RequestFacing(FacingPriority.Motion, velocity);
            else
                ClearFacing(FacingPriority.Motion);
        }

        private bool TryGetHighestPriorityDirection(out Vector3 direction)
        {
            var found = false;
            var bestPriority = FacingPriority.Motion;
            direction = default;

            foreach (var pair in requestsByPriority)
            {
                if (found && pair.Key <= bestPriority) continue;
                found = true;
                bestPriority = pair.Key;
                direction = pair.Value;
            }

            return found;
        }
    }
}
