using System.Collections.Generic;
using CRPG.Actors;
using CRPG.Combat;
using CRPG.Core;
using CRPG.Navigation;
using UnityEngine;

namespace CRPG.Gameplay.Actions
{
    /// <summary>
    /// Task 4.3: the real-navigation move action -- "traverse path segments, stop before interrupt points,
    /// resolve eligible reactions, revalidate then resume/cancel" (design sec.6), driven by a real
    /// <see cref="IMovable"/> motor rather than <see cref="MovementResolutionService.MoveToward"/>'s
    /// instant/legacy teleport-and-check-once. Never constructed by <see cref="MovementResolutionService"/>
    /// itself (that would cycle CRPG.Combat back into CRPG.Gameplay) -- see <see cref="ActionScheduler"/>,
    /// which owns constructing this and calling down into <see cref="MovementResolutionService"/>'s small new
    /// public primitives for the resource/reaction math.
    /// </summary>
    public sealed class MoveActionExecution : ActionExecution
    {
        private const float ArrivalEpsilon = 0.05f;

        private readonly IMovable motor;
        private readonly MovementResolutionService movementResolution;
        private readonly ICharacterRegistry characters;
        private readonly IPositionProvider positions;
        private readonly DefinitionId movementResourceId;
        private readonly List<PlannedStop> stops = new();

        private int nextStopIndex;
        private bool awaitingArrival;
        private bool ranOutOfBudgetThisLeg;
        private Vector3 legStartPosition;
        private MoveRequestId ownedRequestId = MoveRequestId.None;

        private float distanceTraveled;
        private int alreadySpent;

        private readonly struct PlannedStop
        {
            public PlannedStop(Vector3 position, bool isCrossing, RuntimeEntityId reactor, bool entering)
            {
                Position = position;
                IsCrossing = isCrossing;
                Reactor = reactor;
                Entering = entering;
            }

            public Vector3 Position { get; }
            public bool IsCrossing { get; }
            public RuntimeEntityId Reactor { get; }
            public bool Entering { get; }
        }

        public MoveActionExecution(
            RuntimeEntityId source,
            IMovable motor,
            MovementResolutionService movementResolution,
            INavMeshService navMeshService,
            ICharacterRegistry characters,
            IPositionProvider positions,
            DefinitionId movementResourceId,
            RuntimeEntityId towardTarget,
            float stopDistance,
            IReadOnlyList<RuntimeEntityId> potentialReactors = null)
            : base(source)
        {
            this.motor = motor;
            this.movementResolution = movementResolution;
            this.characters = characters;
            this.positions = positions;
            this.movementResourceId = movementResourceId;

            Phase = ActionPhase.Preparing;

            var selfPosition = positions.GetPosition(source);
            var targetPosition = positions.GetPosition(towardTarget);
            var toTarget = targetPosition - selfPosition;
            var fullDistance = toTarget.magnitude;
            var desiredDistance = Mathf.Max(0f, fullDistance - stopDistance);

            if (desiredDistance <= 0f)
            {
                // Already within stopDistance -- success, nothing to plan or spend.
                ArrivedAtDestination = true;
                Finish(ActionOutcome.Succeeded);
                return;
            }

            var destination = selfPosition + toTarget.normalized * desiredDistance;
            PlanRoute(navMeshService, selfPosition, destination, potentialReactors);
        }

        /// <summary>Task 4.5: the position-based counterpart to the entity-target constructor above -- combat
        /// click-to-move needs "walk to this exact ground point," which the entity-target overload cannot
        /// express at all (it always resolves a target's live position, never an arbitrary point). There is no
        /// "stop short by" concept for a bare point, so this always walks the full distance to
        /// <paramref name="destination"/>. Never constructed by <see cref="ActionScheduler"/> for a
        /// melee-closing-distance move -- that keeps using the entity-target overload unchanged.</summary>
        public MoveActionExecution(
            RuntimeEntityId source,
            IMovable motor,
            MovementResolutionService movementResolution,
            INavMeshService navMeshService,
            ICharacterRegistry characters,
            IPositionProvider positions,
            DefinitionId movementResourceId,
            Vector3 destination,
            IReadOnlyList<RuntimeEntityId> potentialReactors = null)
            : base(source)
        {
            this.motor = motor;
            this.movementResolution = movementResolution;
            this.characters = characters;
            this.positions = positions;
            this.movementResourceId = movementResourceId;

            Phase = ActionPhase.Preparing;

            var selfPosition = positions.GetPosition(source);
            var desiredDistance = (destination - selfPosition).magnitude;

            if (desiredDistance <= 0f)
            {
                // Already standing on the clicked point -- success, nothing to plan or spend.
                ArrivedAtDestination = true;
                Finish(ActionOutcome.Succeeded);
                return;
            }

            PlanRoute(navMeshService, selfPosition, destination, potentialReactors);
        }

        /// <summary>Shared tail for both constructors once each has resolved its own real-world
        /// <paramref name="destination"/> and confirmed there's real distance to cover. <paramref name="navMeshService"/>
        /// is a constructor-only collaborator (never stored as a field -- neither constructor needs it again
        /// after planning), so it's threaded through here explicitly rather than promoted to a field.</summary>
        private void PlanRoute(INavMeshService navMeshService, Vector3 selfPosition, Vector3 destination, IReadOnlyList<RuntimeEntityId> potentialReactors)
        {
            if (!navMeshService.TryCalculatePath(selfPosition, destination, out var corners))
            {
                Finish(ActionOutcome.Rejected);
                return;
            }

            BuildPlannedStops(corners, potentialReactors);

            legStartPosition = selfPosition;
            Phase = ActionPhase.Committing;
        }

        /// <summary>True once this action actually reached the requested destination (stop-short-of-target
        /// point); false whenever it ended some other way -- ran out of movement budget partway, was blocked,
        /// interrupted, or cancelled. Both a real arrival and running out of budget are
        /// <see cref="ActionExecution.Outcome"/> == <see cref="ActionOutcome.Succeeded"/> (running out of
        /// movement is an ordinary, expected way for a tactical move to end -- same as tabletop "you stop when
        /// you're out of speed") -- this is the only way to tell them apart.</summary>
        public bool ArrivedAtDestination { get; private set; }

        private void BuildPlannedStops(IReadOnlyList<Vector3> corners, IReadOnlyList<RuntimeEntityId> potentialReactors)
        {
            for (var i = 0; i < corners.Count - 1; i++)
            {
                var segmentStart = corners[i];
                var segmentEnd = corners[i + 1];

                if (potentialReactors != null)
                {
                    var crossings = movementResolution.FindThreatBoundaryCrossings(segmentStart, segmentEnd, potentialReactors);
                    foreach (var crossing in crossings)
                    {
                        var segmentLength = Vector3.Distance(segmentStart, segmentEnd);
                        var t = segmentLength > 0f ? crossing.DistanceAlongSegment / segmentLength : 0f;
                        var crossingPosition = Vector3.Lerp(segmentStart, segmentEnd, t);
                        stops.Add(new PlannedStop(crossingPosition, isCrossing: true, crossing.Reactor, crossing.Entering));
                    }
                }

                stops.Add(new PlannedStop(segmentEnd, isCrossing: false, RuntimeEntityId.None, entering: false));
            }
        }

        public override void Tick(float deltaTime)
        {
            // deltaTime is only consulted to detect a paused frame (Time.timeScale == 0 makes ActionScheduler
            // pass 0 here) -- this class has no duration timer of its own; real progress is entirely driven by
            // the live motor, which Time.timeScale == 0 also freezes at the Unity level.
            if (deltaTime <= 0f || Phase == ActionPhase.Complete) return;

            if (!awaitingArrival)
            {
                PlanNextLeg();
                if (Phase == ActionPhase.Complete) return;
            }

            PollCurrentLeg();
        }

        private void PlanNextLeg()
        {
            if (nextStopIndex >= stops.Count)
            {
                ArrivedAtDestination = true;
                Finish(ActionOutcome.Succeeded);
                return;
            }

            var currentPosition = positions.GetPosition(Source);
            var targetStop = stops[nextStopIndex];
            var remainingBudget = movementResolution.GetEffectiveRemainingBudget(Source, movementResourceId);
            var distanceToStop = Vector3.Distance(currentPosition, targetStop.Position);

            if (distanceToStop <= remainingBudget)
            {
                ranOutOfBudgetThisLeg = false;
                IssueLeg(currentPosition, targetStop.Position);
            }
            else
            {
                // Correctness fix (review): clip to exactly what's affordable rather than issuing the full
                // segment and overspending. remainingBudget is re-queried fresh on every leg (not
                // precomputed once), so a condition applied by a reaction resolved earlier in this same move
                // (e.g. one that reduces speed) is reflected immediately, not stale.
                ranOutOfBudgetThisLeg = true;
                var clipped = remainingBudget > 0f
                    ? Vector3.MoveTowards(currentPosition, targetStop.Position, remainingBudget)
                    : currentPosition;
                IssueLeg(currentPosition, clipped);
            }
        }

        private void IssueLeg(Vector3 currentPosition, Vector3 destination)
        {
            legStartPosition = currentPosition;

            var result = motor.SetDestination(destination);
            if (result.Status == MoveStatus.Invalid)
            {
                SettleOutstandingDistance(currentPosition);
                Finish(ActionOutcome.Interrupted);
                return;
            }

            ownedRequestId = result.RequestId;
            awaitingArrival = true;
        }

        private void PollCurrentLeg()
        {
            var snapshot = motor.Snapshot;

            // Correctness fix (review): request identity, mirroring InteractionController's own established
            // pattern -- a stale signal from a since-superseded request (something else re-ordered this
            // mover) must never be misread as this action's own arrival/block.
            if (snapshot.RequestId != ownedRequestId)
            {
                SettleOutstandingDistance(positions.GetPosition(Source));
                Finish(ActionOutcome.Interrupted);
                return;
            }

            switch (snapshot.Status)
            {
                case MoveStatus.Arrived:
                    OnLegArrived();
                    break;

                case MoveStatus.Blocked:
                case MoveStatus.Invalid:
                case MoveStatus.Cancelled:
                    SettleOutstandingDistance(positions.GetPosition(Source));
                    Finish(ActionOutcome.Interrupted);
                    break;

                // Pending/Moving: keep waiting.
            }
        }

        private void OnLegArrived()
        {
            awaitingArrival = false;
            var arrivalPosition = positions.GetPosition(Source);
            SettleOutstandingDistance(arrivalPosition);

            if (ranOutOfBudgetThisLeg)
            {
                // Clipped short of the planned stop -- never reached it, so its (if any) reaction never
                // triggers, and this is a distinct, final ending regardless of how many stops remained.
                ArrivedAtDestination = false;
                Finish(ActionOutcome.Succeeded);
                return;
            }

            var reachedStop = stops[nextStopIndex];
            if (reachedStop.IsCrossing)
            {
                movementResolution.ResolveReactionForCrossing(Source, reachedStop.Reactor, reachedStop.Entering);

                if (!characters.TryGetCharacter(Source, out _))
                {
                    // Death during reaction -- the mover no longer exists; nothing further to do.
                    Finish(ActionOutcome.Interrupted);
                    return;
                }
            }

            nextStopIndex++;
        }

        /// <summary>Correctness fix (review): settles the real distance covered since the last settle point
        /// -- called on EVERY terminal path (a reached stop, Cancel, Blocked/Invalid, a stale-request
        /// Interrupted signal), never only on a "clean" corner arrival, so a cancelled/blocked partial leg's
        /// movement is never left unspent. The cumulative-rounding math (round the running total, spend only
        /// the delta from what's already been spent) telescopes to exactly Ceil(total real distance moved) by
        /// the time this action ends, regardless of how many intermediate settle points there were --
        /// equivalent to (not more generous than) MoveToward's own single whole-move CeilToInt spend.</summary>
        private void SettleOutstandingDistance(Vector3 currentPosition)
        {
            distanceTraveled += Vector3.Distance(legStartPosition, currentPosition);
            legStartPosition = currentPosition;

            var roundedTotal = Mathf.CeilToInt(distanceTraveled);
            var spendNow = roundedTotal - alreadySpent;
            if (spendNow > 0)
            {
                movementResolution.SpendMovement(Source, movementResourceId, spendNow);
                alreadySpent = roundedTotal;
            }
        }

        /// <summary>Settles whatever distance was actually covered (kept, not refunded -- matches
        /// MoveToward's existing "partial progress is kept" behavior), stops the motor only if this action
        /// still owns the current request (never stops a motion it no longer owns), and finishes Cancelled.
        /// Safe to call after natural completion (a no-op, per <see cref="ActionExecution"/>'s own
        /// single-fire `Finish` guard) or during Preparing (nothing was ever spent).</summary>
        public override void Cancel()
        {
            if (Outcome.HasValue) return;

            if (awaitingArrival)
            {
                SettleOutstandingDistance(positions.GetPosition(Source));
                if (motor.Snapshot.RequestId == ownedRequestId) motor.Stop();
            }

            ArrivedAtDestination = false;
            Finish(ActionOutcome.Cancelled);
        }
    }
}
