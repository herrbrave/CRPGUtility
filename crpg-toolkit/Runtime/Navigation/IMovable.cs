using CRPG.Core;
using UnityEngine;

namespace CRPG.Navigation
{
    /// <summary>Whatever executes movement for one entity toward a destination -- currently backed by a
    /// NavMeshAgent (<see cref="NavMeshCharacterMotor"/>), but higher-level code (Exploration, Combat) depends
    /// only on this interface so a different movement implementation could replace it without touching them.</summary>
    /// <remarks>
    /// Task 2.1: SetDestination's return type changed from void to <see cref="MoveResult"/> and HasArrived/
    /// RemainingDistance were folded into <see cref="MotionSnapshot"/> (a bare bool/float couldn't distinguish
    /// "reached the destination" from "stuck on a partial path" from "never got a valid request at all").
    /// Existing callers that discard SetDestination's return value (e.g. PartyExplorationController's
    /// `motor.SetDestination(x);`) keep compiling unchanged -- C# allows ignoring a non-void return.
    /// </remarks>
    public interface IMovable
    {
        /// <summary>Issues a new move request toward destination. Validates the request before superseding
        /// any request currently in flight -- an invalid destination (off-mesh/disabled mover, unreachable
        /// target) leaves whatever was previously in flight untouched and returns Status.Invalid with
        /// RequestId.None. A valid new request cancels only the request it replaces; a stale completion check
        /// against an older RequestId can never affect the new one (compare against Snapshot.RequestId).</summary>
        MoveResult SetDestination(Vector3 destination);

        /// <summary>Cancels the current move request, if any (Snapshot.Status becomes Cancelled). A no-op if
        /// nothing is in flight.</summary>
        void Stop();

        /// <summary>Instant relocation, bypassing pathing entirely. Returns false, leaving the mover exactly
        /// where it was and any in-flight request untouched, if the position is invalid for this backend
        /// (off-mesh, out of bounds, unwalkable) -- callers branch on this (scene-travel reposition-or-rollback,
        /// teleport success/fail). Implementations must validate BEFORE touching the mover: raw
        /// NavMeshAgent.Warp does not (it moves the transform even when it fails), which is why
        /// NavMeshCharacterMotor samples first. A position near but not on the walkable surface may be snapped
        /// onto it. On success, cancels any in-flight move request (Snapshot.Status becomes Cancelled, same
        /// as <see cref="Stop"/>).</summary>
        bool Warp(Vector3 position);

        /// <summary>Current motion state -- what animation and other systems should read instead of reaching
        /// into the underlying movement backend (e.g. NavMeshAgent) directly.</summary>
        MotionSnapshot Snapshot { get; }
    }
}
