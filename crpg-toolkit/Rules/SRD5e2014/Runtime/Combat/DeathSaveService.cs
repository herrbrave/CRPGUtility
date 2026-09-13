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

        public DeathSaveService(
            ICharacterRegistry characters,
            IDiceService diceService,
            IGameEventBus eventBus,
            DefinitionId hitPointResourceId,
            DefinitionId unconsciousConditionId = default,
            ICombatLog combatLog = null)
        {
            this.characters = characters;
            this.diceService = diceService;
            this.eventBus = eventBus;
            this.hitPointResourceId = hitPointResourceId;
            this.unconsciousConditionId = unconsciousConditionId;
            this.combatLog = combatLog;

            eventBus.Subscribe(GameEvent.BeforeDamage, OnBeforeDamage);
            eventBus.Subscribe(GameEvent.CreatureDroppedToZeroHP, OnCreatureDroppedToZeroHP);
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
                character.DeathSaves = null;
                if (character.TryGetResource(hitPointResourceId, out var hp)) hp.Restore(1);
                RemoveUnconsciousCondition(character);
                combatLog?.Log(new DeathSaveLogEntry(characterId, natural, success: true, 0, 0));
                eventBus.Publish(new GameEventContext(GameEvent.CreatureStabilized, characterId, characterId));
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

            var overkill = context.Data.TryGetValue("Overkill", out var overkillValue) && overkillValue is int amount ? amount : 0;
            var maxHitPoints = character.TryGetResource(hitPointResourceId, out var hp) ? hp.Max : 0;

            if (maxHitPoints > 0 && overkill >= maxHitPoints)
            {
                eventBus.Publish(new GameEventContext(GameEvent.CreatureDied, context.Source, context.Target));
                return;
            }

            if (unconsciousConditionId.IsValid)
                character.ApplyCondition(new ConditionInstance(unconsciousConditionId, context.Source, context.Target));
            character.DeathSaves = new DeathSaveState();
        }

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
