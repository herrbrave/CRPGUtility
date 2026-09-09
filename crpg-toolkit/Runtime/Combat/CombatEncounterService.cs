using System;
using System.Collections.Generic;
using System.Linq;
using CRPG.Actors;
using CRPG.Core;

namespace CRPG.Combat
{
    /// <summary>
    /// Orchestrates one combat encounter: rolls initiative, tracks turn order/round, restores each combatant's
    /// turn-budget resources when their turn starts, and publishes CombatStarted/TurnStarted/TurnEnded/
    /// CombatEnded so the log, UI, and AI can hook in. Deliberately does not decide what happens when a
    /// combatant reaches zero HP (unconscious vs. dead is a ruleset decision) -- <see cref="CombatEncounter.RemoveCombatant"/>
    /// stays a manual call for whoever is subscribed to CreatureDroppedToZeroHP to make that call.
    /// </summary>
    public sealed class CombatEncounterService
    {
        private readonly ICharacterRegistry characters;
        private readonly IInitiativeRule initiativeRule;
        private readonly TurnResourceSet turnResources;
        private readonly IGameEventBus eventBus;
        private readonly ICombatLog combatLog;

        public CombatEncounterService(
            ICharacterRegistry characters,
            IInitiativeRule initiativeRule,
            TurnResourceSet turnResources,
            IGameEventBus eventBus = null,
            ICombatLog combatLog = null)
        {
            this.characters = characters;
            this.initiativeRule = initiativeRule;
            this.turnResources = turnResources;
            this.eventBus = eventBus;
            this.combatLog = combatLog;
        }

        public CombatEncounter Encounter { get; private set; }

        public void StartCombat(IEnumerable<RuntimeEntityId> combatants) =>
            StartCombat(combatants.Select(id => (id, default(DefinitionId))));

        /// <summary>Same as <see cref="StartCombat(IEnumerable{RuntimeEntityId})"/>, but tags each combatant
        /// with a Faction so <see cref="CombatEncounter.GetEnemiesOf"/>/GetAlliesOf can answer "who's on which
        /// side" without whoever's driving the encounter tracking that separately.</summary>
        public void StartCombat(IEnumerable<(RuntimeEntityId Id, DefinitionId Faction)> combatants)
        {
            Encounter = new CombatEncounter();

            foreach (var combatant in combatants)
            {
                var roll = initiativeRule.RollInitiative(combatant.Id);
                combatLog?.Log(new RollLogEntry(roll));
                Encounter.AddCombatant(combatant.Id, (int)roll.Total, combatant.Faction);
            }

            Encounter.Start();
            eventBus?.Publish(new GameEventContext(GameEvent.CombatStarted));
            BeginTurn(Encounter.CurrentTurn);
        }

        public void EndTurn()
        {
            if (Encounter == null) throw new InvalidOperationException("No combat is in progress.");

            eventBus?.Publish(new GameEventContext(GameEvent.TurnEnded, Encounter.CurrentTurn.Id));
            BeginTurn(Encounter.AdvanceTurn());
        }

        public void EndCombat()
        {
            eventBus?.Publish(new GameEventContext(GameEvent.CombatEnded));
            Encounter = null;
        }

        private void BeginTurn(Combatant combatant)
        {
            if (combatant == null) return;

            if (characters.TryGetCharacter(combatant.Id, out var character)) turnResources.RestoreFull(character);
            combatLog?.Log(new TurnLogEntry(combatant.Id, Encounter.Round));
            eventBus?.Publish(new GameEventContext(GameEvent.TurnStarted, combatant.Id));
        }
    }
}
