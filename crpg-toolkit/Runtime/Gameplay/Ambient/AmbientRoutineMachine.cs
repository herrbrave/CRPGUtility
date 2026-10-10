using System;
using System.Collections.Generic;
using CRPG.Core;
using CRPG.Navigation;
using UnityEngine;

namespace CRPG.Gameplay.Ambient
{
    /// <summary>Plays a cue for a routine step. <see cref="Begin"/> returns how long the cue naturally lasts (the
    /// clip length of a one-shot) or 0 for a held pose / a cue that does not exist; the machine ends the step on
    /// its own timer either way, so a missing or never-completing animation can never stall a routine.</summary>
    public interface IRoutineAnimation
    {
        float Begin(DefinitionId cue);

        /// <summary>Releases whatever <see cref="Begin"/> took (a held stance). Safe to call when nothing is held.</summary>
        void End();
    }

    public interface IRoutineFacing
    {
        void Face(Vector3 direction);
        void Clear();
    }

    public interface IRoutineProp
    {
        /// <summary>The single source of truth for "is something in the hands": the world object, not a flag, so a
        /// destroyed model or prop cannot leave the machine believing it still carries it.</summary>
        bool Holding { get; }

        bool Attach(CarriedPropDefinition prop);
        void Detach();
    }

    /// <summary>
    /// The ambient-routine state machine (wander in an area, walk a loop of work stops), with every collaborator
    /// behind a port so it runs against fakes without a NavMesh, a model or a clock: <see cref="IMovable"/> for
    /// movement, <see cref="INavMeshService"/> for picking and checking points, and the three small ports above.
    /// <para>
    /// It owns no scene state. Phases: Idle (waiting, then planning the next step) -> Moving (own move request in
    /// flight) -> Acting (a work stop) -> Idle. Nothing here can stall a village: arrival is only trusted for the
    /// machine's own request id, a leg that makes no progress for <see cref="StallSeconds"/> fails, a failed wander
    /// leg just picks another point, a work stop is retried <see cref="MaxStopRetries"/> times then skipped, and every
    /// action ends on a timer.
    /// </para>
    /// </summary>
    public sealed class AmbientRoutineMachine
    {
        public enum Phase { Idle, Moving, Acting }

        public const float StallSeconds = 4f;
        public const float MinProgressMetres = 0.1f;

        /// <summary>Having moved this far (horizontally) since the last check counts as progress even when the motor cannot report a remaining distance.</summary>
        public const float StallDistanceMetres = 0.25f;
        public const float RetryDelaySeconds = 1f;
        public const float BackOffSeconds = 2f;
        public const float LongBackOffSeconds = 5f;
        public const int MaxStopRetries = 3;
        public const int MaxWanderTries = 8;
        public const int FailuresBeforeLongBackOff = 5;
        public const float MinWanderLegMetres = 1f;
        public const float MinActionSeconds = 0.25f;
        public const float SampleDistance = 3f;

        /// <summary>How long the NPC waits after a suspension ends (a conversation, an interaction) before it plans its next step, so it does not walk off the instant the box closes.</summary>
        public const float ResumeDelaySeconds = 1.5f;

        private readonly RoutinePlan plan;
        private readonly IMovable motor;
        private readonly INavMeshService nav;
        private readonly IRoutineAnimation animation;
        private readonly IRoutineFacing facing;
        private readonly IRoutineProp prop;
        private readonly Func<Vector3> position;
        private readonly Action<string> log;
        private readonly System.Random rng;

        private readonly List<RoutineStopPlan> stops = new();
        private readonly List<Vector3> stopPositions = new();

        private bool initialised;
        private bool suspended;
        private MoveRequestId myRequest = MoveRequestId.None;
        private float timer;
        private float stallTimer;
        private float bestRemaining;
        private Vector3 progressAnchor;
        private int consecutiveFailures;
        private int stopRetries;
        private float actionElapsed;
        private float actionDuration;
        private bool propStepDone;

        public AmbientRoutineMachine(
            RoutinePlan plan, IMovable motor, INavMeshService nav, IRoutineAnimation animation, IRoutineFacing facing,
            IRoutineProp prop, Func<Vector3> position, int seed, Action<string> log = null)
        {
            this.plan = plan ?? throw new ArgumentNullException(nameof(plan));
            this.motor = motor ?? throw new ArgumentNullException(nameof(motor));
            this.nav = nav ?? throw new ArgumentNullException(nameof(nav));
            this.animation = animation;
            this.facing = facing;
            this.prop = prop;
            this.position = position ?? throw new ArgumentNullException(nameof(position));
            this.log = log;
            rng = new System.Random(seed);
        }

        public Phase Current { get; private set; } = Phase.Idle;

        /// <summary>The work stop the machine is heading for or working at (index into the REACHABLE stops).</summary>
        public int StopIndex { get; private set; }

        /// <summary>The stops that survived the reachability check at start, in loop order.</summary>
        public IReadOnlyList<RoutineStopPlan> ReachableStops => stops;

        /// <summary>True when there is nothing to do (Stand, or a work loop with no reachable stop).</summary>
        public bool IsInert { get; private set; }

        public bool IsSuspended => suspended;

        /// <summary>Ref-counting lives with the caller; this only needs the combined answer. Suspending stops the
        /// machine's own movement, releases the pose and facing, and drops back to Idle so the current step is
        /// planned again on resume (a held prop stays in the hands).</summary>
        public void SetSuspended(bool value)
        {
            if (value == suspended) return;
            suspended = value;
            if (value) Interrupt();
            else timer = ResumeDelaySeconds;
        }

        /// <summary>Stop what is in progress and go back to Idle. Also the clean-up when the agent is disabled.</summary>
        public void Interrupt()
        {
            if (Current == Phase.Moving && myRequest.IsValid && motor.Snapshot.RequestId == myRequest) motor.Stop();
            if (Current == Phase.Acting) animation?.End();
            facing?.Clear();
            myRequest = MoveRequestId.None;
            Current = Phase.Idle;
            timer = 0f;
        }

        /// <summary>Forget the current step WITHOUT touching the motor: for when the agent is being switched off or
        /// removed (a NavMeshAgent that is not active must not be asked to reset its path).</summary>
        public void Abandon()
        {
            if (Current == Phase.Acting) animation?.End();
            facing?.Clear();
            myRequest = MoveRequestId.None;
            Current = Phase.Idle;
            timer = 0f;
        }

        public void Tick(float deltaTime)
        {
            if (!initialised) Initialise();
            if (IsInert || suspended) return;

            switch (Current)
            {
                case Phase.Idle:
                    timer -= deltaTime;
                    if (timer <= 0f) PlanNext();
                    break;
                case Phase.Moving:
                    TickMoving(deltaTime);
                    break;
                case Phase.Acting:
                    TickActing(deltaTime);
                    break;
            }
        }

        private void Initialise()
        {
            initialised = true;
            switch (plan.Kind)
            {
                case NpcRoutineKind.Wander:
                    // Idle a little first so a village does not move in lockstep.
                    timer = (float)rng.NextDouble() * Mathf.Max(0.1f, plan.PauseMax);
                    break;
                case NpcRoutineKind.WorkLoop:
                    var here = position();
                    foreach (var stop in plan.Stops)
                    {
                        if (nav.TrySamplePosition(stop.Position, out var sampled, SampleDistance) &&
                            nav.TryCalculatePath(here, sampled, out _))
                        {
                            stops.Add(stop);
                            stopPositions.Add(sampled);
                        }
                        else
                        {
                            log?.Invoke($"Work stop '{stop.Label}' is not reachable on the NavMesh from the NPC's start; skipped.");
                        }
                    }

                    if (stops.Count == 0)
                    {
                        log?.Invoke("Work loop has no reachable stop; the NPC will stand.");
                        IsInert = true;
                    }

                    break;
                default:
                    IsInert = true;
                    break;
            }
        }

        private void PlanNext()
        {
            if (plan.Kind == NpcRoutineKind.Wander)
            {
                if (TryPickWanderPoint(out var point)) BeginMove(point);
                else Fail(countsAgainstStop: false);
            }
            else
            {
                BeginMove(stopPositions[StopIndex]);
            }
        }

        private bool TryPickWanderPoint(out Vector3 point)
        {
            var here = position();
            for (var attempt = 0; attempt < MaxWanderTries; attempt++)
            {
                var angle = (float)rng.NextDouble() * Mathf.PI * 2f;
                var distance = plan.Radius * Mathf.Sqrt((float)rng.NextDouble());
                var candidate = plan.Centre + new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)) * distance;

                if (!nav.TrySamplePosition(candidate, out var sampled, SampleDistance)) continue;
                if (Horizontal(sampled, plan.Centre) > plan.Radius + 0.5f) continue;
                if (Horizontal(sampled, here) < MinWanderLegMetres) continue;
                if (!nav.TryCalculatePath(here, sampled, out _)) continue;

                point = sampled;
                return true;
            }

            point = default;
            return false;
        }

        private void BeginMove(Vector3 destination)
        {
            var result = motor.SetDestination(destination);
            if (result.Status == MoveStatus.Invalid || !result.RequestId.IsValid)
            {
                Fail(countsAgainstStop: true);
                return;
            }

            myRequest = result.RequestId;
            Current = Phase.Moving;
            stallTimer = 0f;
            bestRemaining = float.PositiveInfinity;
            progressAnchor = position();
        }

        private void TickMoving(float deltaTime)
        {
            var snapshot = motor.Snapshot;
            if (snapshot.RequestId != myRequest)
            {
                // Someone else (combat, an interaction) now owns this NPC's motion: leave it alone and try later.
                myRequest = MoveRequestId.None;
                Current = Phase.Idle;
                timer = BackOffSeconds;
                return;
            }

            switch (snapshot.Status)
            {
                case MoveStatus.Arrived:
                    OnArrived();
                    return;
                case MoveStatus.Blocked:
                case MoveStatus.Invalid:
                case MoveStatus.Cancelled:
                    Fail(countsAgainstStop: true);
                    return;
            }

            // Progress is either a shorter remaining distance or simply having moved. NavMeshAgent.remainingDistance is
            // Infinity until the agent is on the last stretch of a path with several corners, so on its own it would
            // report every long walk as stalled.
            var remaining = snapshot.RemainingDistance;
            var here = position();
            if ((!float.IsInfinity(remaining) && remaining < bestRemaining - MinProgressMetres) || Horizontal(here, progressAnchor) >= StallDistanceMetres)
            {
                if (!float.IsInfinity(remaining)) bestRemaining = remaining;
                progressAnchor = here;
                stallTimer = 0f;
                return;
            }

            stallTimer += deltaTime;
            if (stallTimer > StallSeconds)
            {
                motor.Stop();
                Fail(countsAgainstStop: true);
            }
        }

        private void OnArrived()
        {
            consecutiveFailures = 0;
            stopRetries = 0;
            myRequest = MoveRequestId.None;

            if (plan.Kind == NpcRoutineKind.Wander)
            {
                Current = Phase.Idle;
                timer = Mathf.Lerp(plan.PauseMin, plan.PauseMax, (float)rng.NextDouble());
                return;
            }

            BeginAction(stops[StopIndex]);
        }

        private void Fail(bool countsAgainstStop)
        {
            myRequest = MoveRequestId.None;
            Current = Phase.Idle;
            consecutiveFailures++;

            if (plan.Kind == NpcRoutineKind.WorkLoop && countsAgainstStop)
            {
                stopRetries++;
                timer = RetryDelaySeconds;
                if (stopRetries > MaxStopRetries)
                {
                    log?.Invoke($"Work stop '{stops[StopIndex].Label}' could not be reached {MaxStopRetries + 1} times; skipping it.");
                    stopRetries = 0;
                    AdvanceStop();
                }

                return;
            }

            timer = consecutiveFailures >= FailuresBeforeLongBackOff ? LongBackOffSeconds : RetryDelaySeconds;
        }

        private void BeginAction(RoutineStopPlan stop)
        {
            facing?.Face(stop.Facing);

            var clipLength = stop.Cue.IsValid && animation != null ? animation.Begin(stop.Cue) : 0f;
            actionDuration = Mathf.Max(MinActionSeconds, Mathf.Max(stop.Seconds, clipLength));
            actionElapsed = 0f;
            propStepDone = false;
            Current = Phase.Acting;
        }

        private void TickActing(float deltaTime)
        {
            actionElapsed += deltaTime;
            var stop = stops[StopIndex];

            if (!propStepDone && actionElapsed >= actionDuration * 0.5f) DoPropStep(stop);
            if (actionElapsed < actionDuration) return;

            if (!propStepDone) DoPropStep(stop);
            animation?.End();
            facing?.Clear();
            AdvanceStop();
            Current = Phase.Idle;
            timer = 0f;
        }

        private void DoPropStep(RoutineStopPlan stop)
        {
            propStepDone = true;
            if (prop == null) return;

            if (stop.Action == WorkStopAction.PickUp)
            {
                if (!prop.Holding && stop.Prop != null) prop.Attach(stop.Prop);
            }
            else if (stop.Action == WorkStopAction.Drop)
            {
                if (prop.Holding) prop.Detach();
            }
        }

        private void AdvanceStop() => StopIndex = (StopIndex + 1) % Mathf.Max(1, stops.Count);

        private static float Horizontal(Vector3 a, Vector3 b)
        {
            a.y = 0f;
            b.y = 0f;
            return Vector3.Distance(a, b);
        }
    }
}
