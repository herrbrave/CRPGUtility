using System.Collections.Generic;
using System.Linq;
using CRPG.Abilities;
using CRPG.Actors;
using CRPG.Combat;
using CRPG.Core;
using CRPG.Navigation;
using UnityEngine;

namespace CRPG.Gameplay.Actions
{
    /// <summary>
    /// Task 4.3: the single entry point for time-based actions -- one active <see cref="ActionExecution"/> at a
    /// time (mirrors <c>CRPG.Gameplay.Interactions.InteractionController</c>'s own "one request at a time"
    /// shape, not a generic queue). Also the one place that constructs <see cref="AbilityActionExecution"/>/
    /// <see cref="MoveActionExecution"/>, calling down into <see cref="MovementResolutionService"/>'s small
    /// public primitives for the resource/reaction math those need -- CRPG.Combat never references
    /// CRPG.Gameplay back (see this task's own dependency-direction note), so construction has to happen here,
    /// not inside MovementResolutionService itself.
    /// </summary>
    public sealed class ActionScheduler
    {
        private readonly IAbilityResolutionService abilityResolution;
        private readonly MovementResolutionService movementResolution;
        private readonly INavMeshService navMeshService;
        private readonly ICharacterRegistry characters;
        private readonly IPositionProvider positions;
        private readonly CombatEncounterService encounterService;
        private readonly IActionPresentationHook presentationHook;

        public ActionScheduler(
            IAbilityResolutionService abilityResolution,
            MovementResolutionService movementResolution,
            INavMeshService navMeshService,
            ICharacterRegistry characters,
            IPositionProvider positions,
            CombatEncounterService encounterService = null,
            IActionPresentationHook presentationHook = null)
        {
            this.abilityResolution = abilityResolution;
            this.movementResolution = movementResolution;
            this.navMeshService = navMeshService;
            this.characters = characters;
            this.positions = positions;
            this.encounterService = encounterService;
            this.presentationHook = presentationHook;
        }

        public ActionExecution CurrentAction { get; private set; }
        public bool IsBusy => CurrentAction != null;

        /// <summary>Task 4.3 encounter-integration rule: no <see cref="CombatEncounterService"/> at all, or one
        /// with no combat in progress (<see cref="CombatEncounterService.Encounter"/> is null), means "not
        /// gated" -- exploration mode. An encounter that HAS started but currently has no
        /// <see cref="CombatEncounter.CurrentTurn"/> (every combatant removed, winding down) rejects every
        /// action outright -- there is no valid current turn to authorize anything against. Otherwise gates on
        /// exact turn ownership.</summary>
        private bool IsAuthorized(RuntimeEntityId source)
        {
            var encounter = encounterService?.Encounter;
            if (encounter == null) return true;

            var currentTurn = encounter.CurrentTurn;
            return currentTurn != null && currentTurn.Id == source;
        }

        /// <summary>Returns null (nothing started) if busy or not authorized for <paramref name="source"/>
        /// this turn. Otherwise always returns the constructed execution, even if it already finished
        /// synchronously (e.g. Cancel()able-facing-only Preparing never happens for an ability, but a caller
        /// can still inspect <see cref="ActionExecution.Outcome"/> immediately either way).</summary>
        public AbilityActionExecution StartAbility(
            RuntimeEntityId source, AbilityDefinition ability, IReadOnlyList<RuntimeEntityId> targets,
            bool asRitual = false, ResourceDefinition upcastWithSlot = null)
        {
            if (IsBusy || !IsAuthorized(source)) return null;

            var facing = FindAgent(source)?.Facing;
            var execution = new AbilityActionExecution(
                source, abilityResolution, ability, targets, asRitual, upcastWithSlot, facing, positions, presentationHook);
            Adopt(execution);
            return execution;
        }

        /// <summary>Starts an AIMED ability (a point, a shaped area): the targets are derived from the aim when it commits. Same rules as the creature-targeted overload: null when busy or not this actor's turn.</summary>
        public AbilityActionExecution StartAbility(
            RuntimeEntityId source, AbilityDefinition ability, AbilityAim aim,
            bool asRitual = false, ResourceDefinition upcastWithSlot = null)
        {
            if (IsBusy || !IsAuthorized(source)) return null;

            var facing = FindAgent(source)?.Facing;
            var execution = new AbilityActionExecution(
                source, abilityResolution, ability, System.Array.Empty<RuntimeEntityId>(), asRitual, upcastWithSlot, facing, positions, presentationHook, aim);
            Adopt(execution);
            return execution;
        }

        /// <summary>Returns null (nothing started) if busy or not authorized for <paramref name="source"/>
        /// this turn -- the real-navigation planning itself (which can finish synchronously as Rejected, e.g.
        /// an unreachable destination) always happens and is always returned to the caller.</summary>
        public MoveActionExecution StartMove(
            RuntimeEntityId source, IMovable motor, DefinitionId movementResourceId, RuntimeEntityId towardTarget,
            float stopDistance, IReadOnlyList<RuntimeEntityId> potentialReactors = null)
        {
            if (IsBusy || !IsAuthorized(source)) return null;

            var execution = new MoveActionExecution(
                source, motor, movementResolution, navMeshService, characters, positions, movementResourceId,
                towardTarget, stopDistance, potentialReactors);
            Adopt(execution);
            return execution;
        }

        /// <summary>Task 4.5: the position-based counterpart to <see cref="StartMove(RuntimeEntityId, IMovable, DefinitionId, RuntimeEntityId, float, IReadOnlyList{RuntimeEntityId})"/>
        /// -- for "walk to this exact ground point" (combat click-to-move), which has no target entity to
        /// resolve a live position from. Same busy/authorization gating as every other Start* method.</summary>
        public MoveActionExecution StartMove(
            RuntimeEntityId source, IMovable motor, DefinitionId movementResourceId, Vector3 destination,
            IReadOnlyList<RuntimeEntityId> potentialReactors = null)
        {
            if (IsBusy || !IsAuthorized(source)) return null;

            var execution = new MoveActionExecution(
                source, motor, movementResolution, navMeshService, characters, positions, movementResourceId,
                destination, potentialReactors);
            Adopt(execution);
            return execution;
        }

        private void Adopt(ActionExecution execution)
        {
            CurrentAction = execution;
            if (execution.Outcome.HasValue)
            {
                // Finished synchronously during construction -- never actually "busy" from an external
                // caller's point of view.
                CurrentAction = null;
                return;
            }

            execution.Ended += _ =>
            {
                if (ReferenceEquals(CurrentAction, execution)) CurrentAction = null;
            };
        }

        public void Tick(float deltaTime) => CurrentAction?.Tick(deltaTime);

        public void Cancel() => CurrentAction?.Cancel();

        private static CharacterAgent FindAgent(RuntimeEntityId id) =>
            CharacterAgent.AllLive.FirstOrDefault(a => a != null && a.EntityId == id);
    }
}
