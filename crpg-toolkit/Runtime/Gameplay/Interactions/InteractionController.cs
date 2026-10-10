using System;
using System.Linq;
using CRPG.Core;
using CRPG.Navigation;
using CRPG.Party;
using UnityEngine;

namespace CRPG.Gameplay.Interactions
{
    /// <summary>The shared interaction lifecycle state machine (Docs/Design/Production-Foundation.md,
    /// "Interaction": query available actions -> select performer -> approach -> revalidate -> execute ->
    /// release facing/input lease). Plain C#, one request at a time, for PartyState.ActiveCharacter (matching
    /// PartyState's own "only the active character drives the action bar" doc comment). Ticked externally
    /// (<see cref="Tick"/> takes an explicit deltaTime rather than reading Time.deltaTime itself) so most of
    /// this class's own test suite can drive it synchronously without waiting on real frames -- only scenarios
    /// that need a real NavMeshAgent actually resolving a path need a live PlayMode frame.</summary>
    public sealed class InteractionController : IDisposable
    {
        private const float DefaultApproachTimeoutSeconds = 15f;

        private readonly IPlayerCommandSource commandSource;
        private readonly PartyState party;
        private readonly GameSession session;
        private readonly float approachTimeoutSeconds;
        private bool disposed;

        public InteractionRequest CurrentRequest { get; private set; }

        /// <summary>Diagnostic event, fired exactly once per request as it ends -- added specifically so tests
        /// (and, eventually, UI) can assert against it directly rather than reflecting into private state.</summary>
        public event Action<InteractionOutcome> RequestEnded;

        public InteractionController(IPlayerCommandSource commandSource, PartyState party, GameSession session, float approachTimeoutSeconds = DefaultApproachTimeoutSeconds)
        {
            this.commandSource = commandSource ?? throw new ArgumentNullException(nameof(commandSource));
            this.party = party ?? throw new ArgumentNullException(nameof(party));
            this.session = session ?? throw new ArgumentNullException(nameof(session));
            this.approachTimeoutSeconds = approachTimeoutSeconds;

            commandSource.Interact += HandleInteract;
            commandSource.Cancel += HandleCancel;
        }

        private void HandleInteract(InteractCommand command)
        {
            if (!session.AcceptsCommands) return;

            var performer = party.ActiveCharacter;
            if (!performer.IsValid) return;

            var target = FindTarget(command.Target);
            if (target == null || !target.isActiveAndEnabled || !target.IsConfigured) return;
            if (!target.Adapter.IsAvailable(performer)) return;

            var performerAgent = FindAgent(performer);
            if (!IsEligiblePerformer(performerAgent) || performerAgent.Motor == null) return;

            // A fresh Interact command always replaces whatever was previously in flight -- for this or a
            // different target -- rather than layering a second request on top of the first.
            EndCurrentRequest(InteractionOutcome.Superseded);

            var request = new InteractionRequest(performer, target);
            CurrentRequest = request;
            SuspendAmbient(request);

            if (HorizontalDistance(performerAgent.transform.position, target.InteractionPoint) <= target.InteractionRange)
            {
                ArriveAndBeginTurning(request, performerAgent, target);
                return;
            }

            var moveResult = performerAgent.Motor.SetDestination(target.InteractionPoint);
            if (moveResult.Status != MoveStatus.Pending && moveResult.Status != MoveStatus.Moving)
            {
                CurrentRequest = null; // rejected outright -- nothing was ever claimed
                ResumeAmbient(request);
                return;
            }

            request.Phase = InteractionPhase.Approaching;
            request.OwnedMotionRequestId = moveResult.RequestId;
        }

        private void HandleCancel() => Cancel();

        public void Cancel() => EndCurrentRequest(InteractionOutcome.Cancelled);

        public void Tick(float deltaTime)
        {
            var request = CurrentRequest;
            if (request == null) return;

            if (!session.AcceptsCommands) { EndCurrentRequest(InteractionOutcome.SessionEnded); return; }

            var performerAgent = FindAgent(request.Performer);
            if (!IsEligiblePerformer(performerAgent)) { EndCurrentRequest(InteractionOutcome.PerformerLost); return; }

            var target = request.Target;
            if (target == null || !target.isActiveAndEnabled) { EndCurrentRequest(InteractionOutcome.TargetLost); return; }
            if (target.Adapter == null || !target.IsConfigured) { EndCurrentRequest(InteractionOutcome.Unavailable); return; }

            // Applies in every phase: an accepted new order (a genuinely different MoveRequestId) always means
            // this interaction no longer owns the performer's motion. Never misfires on an old-but-unchanged id
            // because OwnedMotionRequestId is recaptured every time this controller itself last touched motion.
            if (performerAgent.Motor.Snapshot.RequestId != request.OwnedMotionRequestId)
            {
                EndCurrentRequest(InteractionOutcome.Superseded);
                return;
            }

            switch (request.Phase)
            {
                case InteractionPhase.Approaching:
                    TickApproaching(request, performerAgent, target, deltaTime);
                    break;
                case InteractionPhase.TurningToFace:
                    TickTurningToFace(request, performerAgent, target);
                    break;
                case InteractionPhase.Executing:
                    break; // waiting on the adapter's own completion callback
            }
        }

        private void TickApproaching(InteractionRequest request, CharacterAgent performerAgent, WorldInteractable target, float deltaTime)
        {
            request.ElapsedApproachSeconds += deltaTime;
            if (request.ElapsedApproachSeconds > approachTimeoutSeconds)
            {
                EndCurrentRequest(InteractionOutcome.ApproachTimedOut);
                return;
            }

            // A target the performer can never reach exactly (a character) completes the approach in range, whatever the motor reports.
            if (target.CompleteApproachInRange && HorizontalDistance(performerAgent.transform.position, target.InteractionPoint) <= target.InteractionRange)
            {
                ArriveAndBeginTurning(request, performerAgent, target);
                return;
            }

            switch (performerAgent.Motor.Snapshot.Status)
            {
                case MoveStatus.Arrived:
                    ArriveAndBeginTurning(request, performerAgent, target);
                    break;

                case MoveStatus.Blocked:
                case MoveStatus.Invalid:
                case MoveStatus.Cancelled:
                    EndCurrentRequest(InteractionOutcome.Blocked);
                    break;

                // Pending/Moving: keep waiting.
            }
        }

        /// <summary>Shared by "already in range at Interact time" and "just arrived": revalidate range +
        /// availability, stop any residual motion (unconditional -- harmless if nothing was moving, and exactly
        /// what fixes "in range but still following an earlier order"), recapture motion ownership, and move to
        /// TurningToFace.</summary>
        private void ArriveAndBeginTurning(InteractionRequest request, CharacterAgent performerAgent, WorldInteractable target)
        {
            if (HorizontalDistance(performerAgent.transform.position, target.InteractionPoint) > target.InteractionRange)
            {
                EndCurrentRequest(InteractionOutcome.ApproachInsufficient);
                return;
            }

            if (!target.Adapter.IsAvailable(request.Performer))
            {
                EndCurrentRequest(InteractionOutcome.Unavailable);
                return;
            }

            performerAgent.Motor.Stop();
            request.OwnedMotionRequestId = performerAgent.Motor.Snapshot.RequestId;
            request.Phase = InteractionPhase.TurningToFace;

            if (performerAgent.Facing == null)
            {
                BeginExecute(request, performerAgent, target);
                return;
            }

            var direction = target.InteractionPoint - performerAgent.transform.position;
            performerAgent.Facing.RequestFacing(FacingPriority.Interaction, direction);
            request.FacingClaimed = true;
            // Deliberately do NOT check IsFacingComplete on this same tick -- FacingController.Update() hasn't
            // run against this new direction yet this frame (the same "do-while, not while" gotcha as
            // FacingControllerTests.WaitForFacingComplete: IsFacingComplete can read a stale true from before
            // the new request was ever applied). The check happens starting next Tick.
        }

        private void TickTurningToFace(InteractionRequest request, CharacterAgent performerAgent, WorldInteractable target)
        {
            if (performerAgent.Facing != null && !performerAgent.Facing.IsFacingComplete) return; // keep waiting
            BeginExecute(request, performerAgent, target);
        }

        private void BeginExecute(InteractionRequest request, CharacterAgent performerAgent, WorldInteractable target)
        {
            // Second checkpoint: could have become unavailable during the turn (first checkpoint is on arrival,
            // via ArriveAndBeginTurning).
            if (!target.Adapter.IsAvailable(request.Performer))
            {
                EndCurrentRequest(InteractionOutcome.Unavailable);
                return;
            }

            request.Phase = InteractionPhase.Executing;

            IInteractionExecution execution;
            try
            {
                execution = target.Adapter.BeginExecute(request.Performer, result => OnExecutionFinished(request, result));
            }
            catch (Exception ex)
            {
                Debug.LogException(ex);
                OnExecutionFinished(request, InteractionExecutionResult.Failed);
                return;
            }

            if (!ReferenceEquals(CurrentRequest, request)) return; // finished synchronously and already ended
            request.Execution = execution;
        }

        private void OnExecutionFinished(InteractionRequest request, InteractionExecutionResult result)
        {
            if (!ReferenceEquals(CurrentRequest, request)) return; // stale/duplicate -- request already ended
            EndCurrentRequest(result == InteractionExecutionResult.Succeeded ? InteractionOutcome.Completed : InteractionOutcome.Failed);
        }

        private void EndCurrentRequest(InteractionOutcome outcome)
        {
            var request = CurrentRequest;
            if (request == null) return;
            CurrentRequest = null;

            // Only cancel an execution that didn't already finish on its own -- Completed/Failed came from the
            // adapter's own callback, which must never be invoked a second time via Cancel().
            if (outcome != InteractionOutcome.Completed && outcome != InteractionOutcome.Failed)
            {
                request.Execution?.Cancel();
            }

            var performerAgent = FindAgent(request.Performer);
            if (performerAgent != null)
            {
                // Stop movement only if this interaction still owns it -- if the RequestId no longer matches, a
                // replacement order is already in flight and must be left completely alone.
                if (performerAgent.Motor != null && performerAgent.Motor.Snapshot.RequestId == request.OwnedMotionRequestId)
                {
                    performerAgent.Motor.Stop();
                }

                if (request.FacingClaimed) performerAgent.Facing?.ClearFacing(FacingPriority.Interaction);
            }

            ResumeAmbient(request);
            RequestEnded?.Invoke(outcome);
        }

        // A wandering NPC must stand still from the moment the party starts walking to it (the approach point is
        // fixed when the order is given), not only once the conversation opens.
        private void SuspendAmbient(InteractionRequest request)
        {
            var npc = request.Target != null ? request.Target.GetComponentInParent<CharacterAgent>() : null;
            if (npc == null || !npc.IsBound) return;

            request.SuspendedAmbientNpc = npc.EntityId;
            session.Ambient.Suspend(npc.EntityId, request);
        }

        private void ResumeAmbient(InteractionRequest request)
        {
            if (!request.SuspendedAmbientNpc.IsValid) return;

            session.Ambient.Resume(request.SuspendedAmbientNpc, request);
            request.SuspendedAmbientNpc = RuntimeEntityId.None;
        }

        private bool IsEligiblePerformer(CharacterAgent agent) =>
            agent != null && agent.isActiveAndEnabled && agent.IsBound && ReferenceEquals(agent.Session, session) && agent.Motor != null;

        private static WorldInteractable FindTarget(RuntimeEntityId id) =>
            WorldInteractable.AllLive.FirstOrDefault(w => w != null && w.EntityId == id);

        private static CharacterAgent FindAgent(RuntimeEntityId id) =>
            CharacterAgent.AllLive.FirstOrDefault(a => a != null && a.EntityId == id);

        private static float HorizontalDistance(Vector3 a, Vector3 b)
        {
            a.y = 0f;
            b.y = 0f;
            return Vector3.Distance(a, b);
        }

        public void Dispose()
        {
            if (disposed) return;
            disposed = true;

            commandSource.Interact -= HandleInteract;
            commandSource.Cancel -= HandleCancel;

            EndCurrentRequest(InteractionOutcome.Cancelled);
        }
    }
}
