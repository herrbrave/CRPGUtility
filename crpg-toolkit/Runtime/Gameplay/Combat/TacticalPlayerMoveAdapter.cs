using System;
using System.Collections.Generic;
using CRPG.Actors;
using CRPG.Core;
using CRPG.Gameplay.Actions;
using UnityEngine;

namespace CRPG.Gameplay.Combat
{
    /// <summary>
    /// Task 4.5: routes ordinary ground clicks (<see cref="IPlayerCommandSource.Move"/>) into
    /// <see cref="ActionScheduler"/> during a tactical encounter -- real player movement must go through the
    /// same scheduler AI uses, never a raw motor call. Only ever moves whichever combatant currently has the
    /// turn AND belongs to <see cref="playerFaction"/> -- a click while it's a different combatant's turn (or
    /// while the scheduler is already busy) is silently ignored, never redirected to someone else or queued.
    /// </summary>
    /// <remarks>
    /// This fixture never constructs <c>CRPG.Exploration.PartyExplorationController</c> -- there is exactly
    /// one movement mode during a tactical encounter, so there's no dual-ownership of the Move event to
    /// arbitrate between an exploration controller and this adapter.
    /// </remarks>
    public sealed class TacticalPlayerMoveAdapter
    {
        private readonly TacticalEncounterController controller;
        private readonly DefinitionId movementResourceId;
        private readonly DefinitionId playerFaction;
        private readonly Func<RuntimeEntityId, bool> canMove;
        private readonly Func<RuntimeEntityId, Vector3, (Vector3 Destination, string Refusal)> adjustDestination;
        private readonly bool provokesReactions;

        /// <param name="canMove">Optional: false for the current-turn combatant refuses the click silently (a downed hero). Null: everyone can.</param>
        /// <param name="adjustDestination">Optional: may change the clicked destination or refuse it (a non-null Refusal is raised on <see cref="MoveRefused"/> and nothing starts).
        /// See <see cref="NavMeshDestinationAdjuster"/>. Runs after the agent/motor check, so no refusal message is ever shown when there is nobody to move.</param>
        /// <param name="provokesReactions">True (the default) hands every other live combatant to the scheduler as a potential reactor, so walking through an enemy's threatened area
        /// can provoke an opportunity attack (the move stops at each crossing). False hands none: the move never stops for, or provokes, a reaction.</param>
        public TacticalPlayerMoveAdapter(
            TacticalEncounterController controller,
            DefinitionId movementResourceId,
            DefinitionId playerFaction,
            Func<RuntimeEntityId, bool> canMove = null,
            Func<RuntimeEntityId, Vector3, (Vector3 Destination, string Refusal)> adjustDestination = null,
            bool provokesReactions = true)
        {
            this.controller = controller ?? throw new ArgumentNullException(nameof(controller));
            this.movementResourceId = movementResourceId;
            this.playerFaction = playerFaction;
            this.canMove = canMove;
            this.adjustDestination = adjustDestination;
            this.provokesReactions = provokesReactions;
        }

        /// <summary>Raised with the refusal text when <c>adjustDestination</c> turned a click down ("You cannot go there.").</summary>
        public event Action<string> MoveRefused;

        /// <summary>Void-signature wrapper so this can be subscribed directly as
        /// <c>inputSource.Move += adapter.HandleMove</c> -- see <see cref="TryHandleMove"/> for the inspectable
        /// result.</summary>
        public void HandleMove(MoveCommand command) => TryHandleMove(command);

        /// <returns>The real <see cref="MoveActionExecution"/> if a move was actually started through
        /// <see cref="ActionScheduler"/> for the current turn's own combatant; null on every guarded no-op
        /// (wrong turn/faction, scheduler already busy, no live agent/motor found) -- never on a REJECTED
        /// path planning outcome, which still returns a real (non-null, already-Rejected) execution, exactly
        /// like every other <see cref="ActionScheduler.StartMove(RuntimeEntityId, CRPG.Navigation.IMovable, DefinitionId, UnityEngine.Vector3, IReadOnlyList{RuntimeEntityId})"/>
        /// caller sees.</returns>
        public MoveActionExecution TryHandleMove(MoveCommand command)
        {
            var encounter = controller.EncounterService.Encounter;
            var current = encounter?.CurrentTurn;
            if (current == null || current.Faction != playerFaction) return null;
            if (controller.Scheduler.IsBusy) return null;

            var agent = FindAgent(current.Id);
            if (agent?.Motor == null) return null;
            if (canMove != null && !canMove(current.Id)) return null;

            var destination = command.Destination;
            if (adjustDestination != null)
            {
                var adjusted = adjustDestination(current.Id, destination);
                if (adjusted.Refusal != null) { MoveRefused?.Invoke(adjusted.Refusal); return null; }
                destination = adjusted.Destination;
            }

            return controller.Scheduler.StartMove(current.Id, agent.Motor, movementResourceId, destination, provokesReactions ? OtherLiveCombatants(encounter, current.Id) : null);
        }

        private static IReadOnlyList<RuntimeEntityId> OtherLiveCombatants(CRPG.Combat.CombatEncounter encounter, RuntimeEntityId exclude)
        {
            var others = new List<RuntimeEntityId>();
            foreach (var combatant in encounter.Combatants)
            {
                if (combatant.Id != exclude) others.Add(combatant.Id);
            }

            return others;
        }

        private static CharacterAgent FindAgent(RuntimeEntityId id)
        {
            foreach (var agent in CharacterAgent.AllLive)
            {
                if (agent != null && agent.EntityId == id) return agent;
            }

            return null;
        }
    }
}
