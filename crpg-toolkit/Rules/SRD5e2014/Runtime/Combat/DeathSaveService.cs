using System;
using CRPG.Actors;
using CRPG.Core;

namespace CRPG.Rules.SRD5
{
    /// <summary>
    /// SRD5.1's death-and-dying loop, layered entirely on top of DamageEffect's generic zero-HP events rather
    /// than DamageEffect knowing anything about it -- subscribes itself to BeforeDamage/CreatureDroppedToZeroHP
    /// on construction, since its whole job is reacting to those. Doesn't touch CombatEncounter membership at
    /// all (a dying character stays a full combatant, since SRD5.1's own rule is "unconscious, not removed");
    /// whoever owns the encounter is expected to subscribe to CreatureDied themselves and call
    /// CombatEncounter.RemoveCombatant, exactly like CreatureDroppedToZeroHP already worked before this.
    /// </summary>
    public sealed class DeathSaveService
    {
        private readonly ICharacterRegistry characters;
        private readonly IDiceService diceService;
        private readonly IGameEventBus eventBus;
        private readonly ICombatLog combatLog;
        private readonly DefinitionId hitPointResourceId;
        private readonly DefinitionId unconsciousConditionId;
        private readonly Func<RuntimeEntityId, bool> usesDeathSaves;
        private readonly Func<RuntimeEntityId, bool> canDie;

        /// <param name="reviveOnHeal">False (the default) leaves the original behaviour: a heal restores hit points and nothing else, so a dying or unconscious
        /// character stays down until something else clears it. True subscribes to <see cref="GameEvent.AfterHeal"/>: a heal that brings a character from 0 hit points
        /// above 0 ends its death saves, removes every Unconscious condition and publishes <see cref="GameEvent.CreatureStabilized"/> (the "recovered" cue the sprite and
        /// model bindings already listen to). SRD5.1: any healing at 0 hit points ends dying. Only characters at 0 before the heal are revived, so a healthy creature
        /// that is unconscious for another reason (a sleep effect) is not woken; and only characters that <paramref name="usesDeathSaves"/> covers.</param>
        /// <param name="canDie">Null (the default) means everyone who fails three death saves, or takes lethal overkill, dies -- the
        /// SRD behavior. When supplied, a character it returns false for NEVER dies: failures stop at two and lethal overkill leaves them
        /// downed instead, until something stabilises them (a won fight, a natural 20, three successes).</param>
        /// <param name="usesDeathSaves">SRD5.1's death-save loop is a player-character rule -- a monster just
        /// dies at 0 HP (Task 6.1: found live when a real hostile-vs-party encounter first existed in BarrowsEnd
        /// -- a giant rat dropped to 0 HP was going Unconscious and rolling death saves like a PC instead of
        /// dying). Null (the default) preserves this service's original behavior of applying death saves to
        /// EVERY character regardless of side, matching every existing caller (e.g. the Gate scene, where
        /// everyone who can reach 0 HP is a party member anyway). When supplied, a character this predicate
        /// returns false for dies outright on dropping to 0 HP, the same as an overkill hit.</param>
        public DeathSaveService(
            ICharacterRegistry characters,
            IDiceService diceService,
            IGameEventBus eventBus,
            DefinitionId hitPointResourceId,
            DefinitionId unconsciousConditionId = default,
            ICombatLog combatLog = null,
            Func<RuntimeEntityId, bool> usesDeathSaves = null,
            Func<RuntimeEntityId, bool> canDie = null,
            bool reviveOnHeal = false)
        {
            this.canDie = canDie;
            this.characters = characters;
            this.diceService = diceService;
            this.eventBus = eventBus;
            this.hitPointResourceId = hitPointResourceId;
            this.unconsciousConditionId = unconsciousConditionId;
            this.combatLog = combatLog;
            this.usesDeathSaves = usesDeathSaves;

            eventBus.Subscribe(GameEvent.BeforeDamage, OnBeforeDamage);
            eventBus.Subscribe(GameEvent.CreatureDroppedToZeroHP, OnCreatureDroppedToZeroHP);
            if (reviveOnHeal) eventBus.Subscribe(GameEvent.AfterHeal, OnAfterHeal);
        }

        /// <summary>Call once at the start of a dying character's turn. A flat 1d20, no modifiers or
        /// advantage/disadvantage of any kind ever apply to a death save (SRD5.1). No-ops if the character
        /// isn't currently dying.</summary>
        public void RollDeathSave(RuntimeEntityId characterId)
        {
            if (!characters.TryGetCharacter(characterId, out var character) || character.DeathSaves == null) return;

            var roll = diceService.Roll(new RollRequest(new DiceExpression(1, 20), RollPurpose.DeathSavingThrow, source: characterId, target: characterId));
            var natural = roll.Dice[0].Value;

            if (natural == 20)
            {
                Stabilize(characterId);
                combatLog?.Log(new DeathSaveLogEntry(characterId, natural, success: true, 0, 0));
                return;
            }

            if (natural == 1)
            {
                RecordFailure(characterId, character, 2, natural);
                return;
            }

            if (natural >= 10) RecordSuccess(characterId, character, natural);
            else RecordFailure(characterId, character, 1, natural);
        }

        /// <summary>Ends a dying character's death saves at 1 hit point, conscious again -- the same outcome a
        /// natural 20 produces, callable directly (e.g. a composition root stabilising the downed once a fight is
        /// won). No-ops if the character isn't currently dying.</summary>
        public void Stabilize(RuntimeEntityId characterId)
        {
            if (!characters.TryGetCharacter(characterId, out var character) || character.DeathSaves == null) return;

            character.DeathSaves = null;
            if (character.TryGetResource(hitPointResourceId, out var hp)) hp.Restore(1);
            RemoveUnconsciousCondition(character);
            eventBus.Publish(new GameEventContext(GameEvent.CreatureStabilized, characterId, characterId));
        }

        private void OnAfterHeal(GameEventContext context)
        {
            if (!characters.TryGetCharacter(context.Target, out var character)) return;
            if (usesDeathSaves != null && !usesDeathSaves(context.Target)) return;
            if (!character.TryGetResource(hitPointResourceId, out var hp) || hp.Current <= 0) return;

            var restored = context.Data.TryGetValue("Amount", out var amount) && amount is int restoredAmount ? restoredAmount : 0;
            var wasAtZero = hp.Current - restored <= 0;
            var dying = character.DeathSaves != null;
            var stableAtZero = wasAtZero && unconsciousConditionId.IsValid && character.HasCondition(unconsciousConditionId);
            if (!dying && !stableAtZero) return;

            character.DeathSaves = null;
            if (unconsciousConditionId.IsValid) character.RemoveConditions(c => c.ConditionId == unconsciousConditionId);
            eventBus.Publish(new GameEventContext(GameEvent.CreatureStabilized, context.Source, context.Target));
        }

        private void OnBeforeDamage(GameEventContext context)
        {
            if (!characters.TryGetCharacter(context.Target, out var character) || character.DeathSaves == null) return;

            // Already down -- any further damage is a death-save failure instead of more HP loss (SRD5.1
            // "Damage at 0 Hit Points"), so there's nothing left for DamageEffect itself to spend.
            context.Data["Amount"] = 0;
            var isCritical = context.Data.TryGetValue("IsCritical", out var critical) && critical is bool crit && crit;
            RecordFailure(context.Target, character, isCritical ? 2 : 1, naturalRoll: null);
        }

        private void OnCreatureDroppedToZeroHP(GameEventContext context)
        {
            if (!characters.TryGetCharacter(context.Target, out var character)) return;

            if (usesDeathSaves != null && !usesDeathSaves(context.Target))
            {
                eventBus.Publish(new GameEventContext(GameEvent.CreatureDied, context.Source, context.Target));
                return;
            }

            var overkill = context.Data.TryGetValue("Overkill", out var overkillValue) && overkillValue is int amount ? amount : 0;
            var maxHitPoints = character.TryGetResource(hitPointResourceId, out var hp) ? hp.Max : 0;

            if (maxHitPoints > 0 && overkill >= maxHitPoints && CanDie(context.Target))
            {
                eventBus.Publish(new GameEventContext(GameEvent.CreatureDied, context.Source, context.Target));
                return;
            }

            if (unconsciousConditionId.IsValid)
                character.ApplyCondition(new ConditionInstance(unconsciousConditionId, context.Source, context.Target));
            character.DeathSaves = new DeathSaveState();
        }

        private bool CanDie(RuntimeEntityId id) => canDie == null || canDie(id);

        private void RecordSuccess(RuntimeEntityId characterId, CharacterState character, int? naturalRoll = null)
        {
            character.DeathSaves.Successes++;
            var deathSaves = character.DeathSaves;
            combatLog?.Log(new DeathSaveLogEntry(characterId, naturalRoll ?? 0, success: true, deathSaves.Successes, deathSaves.Failures));

            if (deathSaves.Successes >= 3)
            {
                character.DeathSaves = null;
                eventBus.Publish(new GameEventContext(GameEvent.CreatureStabilized, characterId, characterId));
            }
        }

        private void RecordFailure(RuntimeEntityId characterId, CharacterState character, int count, int? naturalRoll)
        {
            character.DeathSaves.Failures += count;
            var deathSaves = character.DeathSaves;
            combatLog?.Log(new DeathSaveLogEntry(characterId, naturalRoll ?? 0, success: false, deathSaves.Successes, deathSaves.Failures));

            if (deathSaves.Failures >= 3 && !CanDie(characterId))
            {
                // A character that cannot die (a campaign with no permadeath for its party) stays downed at the brink: two failures
                // is as far as the count goes, so the fight can still be lost but nobody is ever permanently gone.
                deathSaves.Failures = 2;
                return;
            }

            if (deathSaves.Failures >= 3)
            {
                character.DeathSaves = null;
                RemoveUnconsciousCondition(character);
                eventBus.Publish(new GameEventContext(GameEvent.CreatureDied, characterId, characterId));
            }
        }

        private void RemoveUnconsciousCondition(CharacterState character)
        {
            if (!unconsciousConditionId.IsValid) return;

            foreach (var condition in character.Conditions)
            {
                if (condition.ConditionId != unconsciousConditionId) continue;
                character.RemoveCondition(condition);
                return;
            }
        }
    }
}
