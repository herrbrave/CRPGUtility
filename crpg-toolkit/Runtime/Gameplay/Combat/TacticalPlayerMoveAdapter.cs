using System;
using System.Collections.Generic;
using CRPG.Actors;
using CRPG.Core;
using CRPG.Gameplay.Actions;

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

        public TacticalPlayerMoveAdapter(TacticalEncounterController controller, DefinitionId movementResourceId, DefinitionId playerFaction)
        {
            this.controller = controller ?? throw new ArgumentNullException(nameof(controller));
            this.movementResourceId = movementResourceId;
            this.playerFaction = playerFaction;
        }

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

            return controller.Scheduler.StartMove(current.Id, agent.Motor, movementResourceId, command.Destination, OtherLiveCombatants(encounter, current.Id));
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
