using System;
using System.Collections.Generic;
using CRPG.Abilities;
using CRPG.Actors;
using CRPG.Core;
using UnityEngine;

namespace CRPG.Gameplay.Actions
{
    /// <summary>Task 4.3: wraps <see cref="IAbilityResolutionService"/>'s single-call
    /// Validate/Execute contract (Task 4.2) in real Prepare -&gt; Commit -&gt; Recover -&gt; Complete timing
    /// (design sec.4.4), driven by <see cref="ability"/>'s own authored <see cref="AbilityDefinition.PrepareSeconds"/>/
    /// <see cref="AbilityDefinition.RecoverSeconds"/> (both default 0, so unauthored content collapses to
    /// today's effectively-instant behavior). <see cref="Execute"/> already re-validates internally at commit
    /// time, so "target disappearing before commit" is inherited from Task 4.2 rather than reimplemented here.</summary>
    public sealed class AbilityActionExecution : ActionExecution
    {
        private readonly IAbilityResolutionService abilityResolution;
        private readonly AbilityDefinition ability;
        private readonly RuntimeEntityId[] targets;
        private readonly bool asRitual;
        private readonly ResourceDefinition upcastWithSlot;
        private readonly FacingController facing;
        private readonly IPositionProvider positions;
        private readonly IActionPresentationHook presentationHook;

        private float elapsed;
        private bool facingClaimed;

        public AbilityActionExecution(
            RuntimeEntityId source,
            IAbilityResolutionService abilityResolution,
            AbilityDefinition ability,
            IReadOnlyList<RuntimeEntityId> targets,
            bool asRitual = false,
            ResourceDefinition upcastWithSlot = null,
            FacingController facing = null,
            IPositionProvider positions = null,
            IActionPresentationHook presentationHook = null)
            : base(source)
        {
            this.abilityResolution = abilityResolution ?? throw new ArgumentNullException(nameof(abilityResolution));
            this.ability = ability ?? throw new ArgumentNullException(nameof(ability));

            // Snapshot at construction -- the caller's own list must not be mutable out from under a
            // Preparing action.
            this.targets = new RuntimeEntityId[targets?.Count ?? 0];
            if (targets != null) for (var i = 0; i < targets.Count; i++) this.targets[i] = targets[i];

            this.asRitual = asRitual;
            this.upcastWithSlot = upcastWithSlot;
            this.facing = facing;
            this.positions = positions;
            this.presentationHook = presentationHook;

            Phase = ActionPhase.Preparing;
            ClaimFacing();
        }

        /// <summary>The real Execute() result, once Commit has run -- null while still Preparing.</summary>
        public AbilityExecutionResult ExecutionResult { get; private set; }

        private void ClaimFacing()
        {
            // Both facing and positions are optional -- a caller with neither (e.g. a non-visual demo) simply
            // gets no facing behavior, same as every other optional collaborator in this codebase.
            if (facing == null || positions == null || targets.Length == 0) return;

            var direction = positions.GetPosition(targets[0]) - facing.transform.position;
            facing.RequestFacing(FacingPriority.CommittedAction, direction);
            facingClaimed = true;
        }

        private void ReleaseFacing()
        {
            if (!facingClaimed) return;
            facingClaimed = false;
            facing?.ClearFacing(FacingPriority.CommittedAction);
        }

        /// <summary>Consumes only as much of <paramref name="deltaTime"/> as the current phase's remaining
        /// timer needs, carrying any remainder into the next phase within this SAME call -- one Tick(2.5f)
        /// therefore produces the identical end state as five Tick(0.5f) calls summing to the same total,
        /// rather than silently dropping a remainder at a phase boundary crossed mid-tick. Correctness fix: the
        /// loop advances through a phase whose remaining duration is already 0 regardless of how much
        /// deltaTime is left (even exactly 0) -- a zero-duration phase is instantaneous by definition, so it
        /// must never wait for a future nonzero tick to complete (this is what makes deltaTime == 0, a paused
        /// frame, a true no-op for a POSITIVE-remaining phase while still letting an already-due zero-duration
        /// phase resolve immediately).</summary>
        public override void Tick(float deltaTime)
        {
            if (deltaTime < 0f) deltaTime = 0f;

            while (Phase != ActionPhase.Complete)
            {
                float remaining;
                switch (Phase)
                {
                    case ActionPhase.Preparing:
                        remaining = Mathf.Max(0f, ability.PrepareSeconds - elapsed);
                        if (deltaTime < remaining)
                        {
                            elapsed += deltaTime;
                            return;
                        }

                        deltaTime -= remaining;
                        Commit();
                        break;

                    case ActionPhase.Recovering:
                        remaining = Mathf.Max(0f, ability.RecoverSeconds - elapsed);
                        if (deltaTime < remaining)
                        {
                            elapsed += deltaTime;
                            return;
                        }

                        deltaTime -= remaining;
                        Finish(ActionOutcome.Succeeded);
                        break;

                    default:
                        // Committing is transient (collapses into Recovering/Complete within Commit() itself,
                        // same tick) -- this default only guards against ever spinning here.
                        return;
                }
            }
        }

        private void Commit()
        {
            Phase = ActionPhase.Committing;

            // Facing is only meaningful while deciding/winding up -- release it here unconditionally
            // (regardless of the outcome below) rather than waiting for Complete.
            ReleaseFacing();

            ExecutionResult = abilityResolution.Execute(Source, ability, targets, asRitual, upcastWithSlot);

            if (!ExecutionResult.Succeeded)
            {
                Finish(MapRejectionOutcome(ExecutionResult));
                return;
            }

            RequestPresentationCue();
            elapsed = 0f;
            Phase = ActionPhase.Recovering;
        }

        private void RequestPresentationCue()
        {
            if (presentationHook == null) return;

            try
            {
                presentationHook.RequestActionCue(Source, ability);
            }
            catch (Exception ex)
            {
                // A broken/throwing presentation hook must never strand an already-committed action --
                // gameplay already succeeded by this point.
                Debug.LogException(ex);
            }
        }

        private static ActionOutcome MapRejectionOutcome(AbilityExecutionResult result) => result.Outcome switch
        {
            AbilityExecutionOutcome.Faulted => ActionOutcome.Faulted,
            _ => ActionOutcome.Rejected,
        };

        /// <summary>Only <see cref="ActionPhase.Preparing"/> can be cancelled outright -- nothing has been
        /// spent or applied yet. From <see cref="ActionPhase.Recovering"/> onward this is a documented no-op:
        /// the ability's effects may already be applied, and letting Cancel shorten Recover would let a caller
        /// bypass authored recovery entirely, defeating "simulation time and authoritative commit schedule
        /// belong to action orchestration" (design sec.4.4) -- recovery constrains cadence, it is not
        /// cosmetic. The action will still reach Complete on its own timer regardless.</summary>
        public override void Cancel()
        {
            if (Phase != ActionPhase.Preparing) return;
            ReleaseFacing();
            Finish(ActionOutcome.Cancelled);
        }
    }
}
