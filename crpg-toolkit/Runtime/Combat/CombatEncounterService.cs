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

        /// <summary>Task 5.2 gap fix: <paramref name="initiativeRule"/> is now optional (default null) --
        /// fully backward compatible, since every existing caller already supplies one. A null rule only works
        /// with <see cref="StartCombatWithExplicitOrder"/>; the rolled <see cref="StartCombat(IEnumerable{RuntimeEntityId})"/>
        /// overloads still require a real rule and throw a clear exception otherwise. Added because a ruleset
        /// can genuinely have no rolled initiative at all (e.g. Fate Condensed's own default is an elective,
        /// narrated turn order, not a die roll) -- there was previously no way to start an encounter without
        /// one.</summary>
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
            if (initiativeRule == null)
                throw new InvalidOperationException(
                    $"{nameof(StartCombat)} requires a non-null {nameof(IInitiativeRule)} -- use {nameof(StartCombatWithExplicitOrder)} for a ruleset with no rolled initiative.");

            BeginEncounter(combatants, rollInitiative: true);
        }

        /// <summary>Task 5.2 gap fix: starts combat with the given sequence used as the turn order directly --
        /// no <see cref="IInitiativeRule"/> call at all, works even when this service was constructed with a
        /// null one. Assigns each combatant a synthetic, strictly descending initiative value matching its
        /// position in <paramref name="orderedCombatants"/> so <see cref="CombatEncounter.Start"/>'s own
        /// highest-first sort reproduces the given order exactly -- <see cref="CombatEncounter"/> itself needed
        /// no change.</summary>
        public void StartCombatWithExplicitOrder(IEnumerable<(RuntimeEntityId Id, DefinitionId Faction)> orderedCombatants) =>
            BeginEncounter(orderedCombatants, rollInitiative: false);

        private void BeginEncounter(IEnumerable<(RuntimeEntityId Id, DefinitionId Faction)> combatants, bool rollInitiative)
        {
            Encounter = new CombatEncounter();

            var syntheticInitiative = 0;
            foreach (var combatant in combatants)
            {
                int initiative;
                if (rollInitiative)
                {
                    var roll = initiativeRule.RollInitiative(combatant.Id);
                    combatLog?.Log(new RollLogEntry(roll));
                    initiative = (int)roll.Total;
                }
                else
                {
                    initiative = int.MaxValue - syntheticInitiative;
                    syntheticInitiative++;
                }

                Encounter.AddCombatant(combatant.Id, initiative, combatant.Faction);
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
