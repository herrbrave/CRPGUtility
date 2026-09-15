using System;
using CRPG.AI;
using CRPG.Actors;
using CRPG.Combat;
using CRPG.Core;
using CRPG.Gameplay.Actions;
using CRPG.Navigation;

namespace CRPG.Gameplay.Combat
{
    /// <summary>
    /// Task 4.5: drives every turn belonging to one faction through the real decide -&gt; execute -&gt; reassess
    /// -&gt; decide-again loop Gate C actually requires -- replaces <c>AICombatDemoBootstrap.OnNextTurnClicked</c>'s
    /// AI branch (one decision, then always end turn) with something that keeps deciding for as long as the
    /// brain finds something legal and useful to do, routed through the SAME <see cref="ActionScheduler"/> the
    /// player-facing UI bridge uses -- never <c>MovementResolutionService.MoveToward</c>/
    /// <c>IAbilityResolutionService.Execute</c> directly.
    /// </summary>
    public sealed class TacticalAiTurnDriver
    {
        // A real turn here is at most "move, then act" plus a little headroom for content with more than one
        // meaningfully different legal option -- generous without risking an actual infinite loop if a brain
        // or its content is ever misconfigured to keep returning a decision that never stops being legal.
        private const int MaxDecisionsPerTurn = 8;
        private const int MaxConsecutiveRejectedStarts = 3;

        private readonly TacticalEncounterController controller;
        private readonly ICharacterRegistry characters;
        private readonly ITurnDecisionProvider brain;
        private readonly DefinitionId faction;
        private readonly Action<RuntimeEntityId> rollDeathSave;

        private RuntimeEntityId? trackedTurnFor;
        private int decisionsThisTurn;
        private int consecutiveRejectedStarts;

        /// <summary><paramref name="rollDeathSave"/> is a plain delegate, not a direct
        /// <c>CRPG.Rules.SRD5.DeathSaveService</c> reference -- that type is ruleset-specific and
        /// <c>CRPG.Gameplay</c> must never reference it directly (mirrors why
        /// <see cref="TacticalEncounterController"/> never takes one either). The composition root wires
        /// <c>deathSaveService.RollDeathSave</c> here. Null is a valid choice for a fixture with no incapacitated
        /// combatants on this faction -- the branch simply never fires.</summary>
        public TacticalAiTurnDriver(
            TacticalEncounterController controller, ICharacterRegistry characters, ITurnDecisionProvider brain,
            DefinitionId faction, Action<RuntimeEntityId> rollDeathSave = null)
        {
            this.controller = controller ?? throw new ArgumentNullException(nameof(controller));
            this.characters = characters ?? throw new ArgumentNullException(nameof(characters));
            this.brain = brain ?? throw new ArgumentNullException(nameof(brain));
            this.faction = faction;
            this.rollDeathSave = rollDeathSave;
        }

        public void Tick(float deltaTime)
        {
            var encounter = controller.EncounterService.Encounter;
            var current = encounter?.CurrentTurn;

            if (current == null || current.Faction != faction)
            {
                trackedTurnFor = null;
                return;
            }

            if (trackedTurnFor != current.Id)
            {
                trackedTurnFor = current.Id;
                decisionsThisTurn = 0;
                consecutiveRejectedStarts = 0;
            }

            // Wait for whatever's already in flight (this combatant's own previous decision, or -- since
            // ActionScheduler is shared -- in practice always this combatant's own, since only the current
            // turn can ever be authorized to start one) before reassessing.
            if (controller.Scheduler.IsBusy) return;

            if (!characters.TryGetCharacter(current.Id, out var character))
            {
                controller.EncounterService.EndTurn();
                return;
            }

            if (character.DeathSaves != null)
            {
                // Incapacitated -- no decision to make, just roll the death save and pass. Whether this
                // stabilizes, fails, or kills is entirely DeathSaveService's own concern.
                rollDeathSave?.Invoke(current.Id);
                controller.EncounterService.EndTurn();
                return;
            }

            decisionsThisTurn++;
            if (decisionsThisTurn > MaxDecisionsPerTurn)
            {
                controller.EncounterService.EndTurn();
                return;
            }

            var decision = brain.GetDecision(new TurnContext(current.Id, encounter.GetEnemiesOf(current.Id), encounter.GetAlliesOf(current.Id)));
            if (decision == null)
            {
                // Genuinely nothing left worth doing -- done for the turn, not merely done for this attempt.
                controller.EncounterService.EndTurn();
                return;
            }

            ActionExecution started = decision.Kind == BehaviorKind.Move
                ? controller.Scheduler.StartMove(current.Id, FindMotor(current.Id), decision.MovementResourceId, decision.Targets[0], decision.StopDistance)
                : controller.Scheduler.StartAbility(current.Id, decision.Ability, decision.Targets);

            if (started == null)
            {
                // A legal-looking decision was rejected at start time (e.g. commit-time revalidation inside a
                // synchronous execution, or a race with something else this same tick) -- bounded retry, never
                // spin forever on a decision that can never actually start.
                consecutiveRejectedStarts++;
                if (consecutiveRejectedStarts >= MaxConsecutiveRejectedStarts) controller.EncounterService.EndTurn();
                return;
            }

            consecutiveRejectedStarts = 0;
        }

        private static IMovable FindMotor(RuntimeEntityId id)
        {
            foreach (var agent in CharacterAgent.AllLive)
            {
                if (agent != null && agent.EntityId == id) return agent.Motor;
            }

            return null;
        }
    }
}
