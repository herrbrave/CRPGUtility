using System.Collections.Generic;
using System.Linq;
using CRPG.Actors;
using CRPG.Core;

namespace CRPG.Rules.SRD5
{
    /// <summary>Handles the daily "prepare spells" action for Prepared-casters (Wizard/Cleric/Druid/Paladin) --
    /// choosing a subset of a class's full SpellListAbilityIds to have castable until the next long rest. Bool-
    /// returning with no side effects on failure, same shape as EquipmentService/StoreService's Try* methods.</summary>
    public static class SpellPreparationService
    {
        /// <summary>SRD5's prepared-caster formula: (level, or half level for a half-caster) / divisor, rounded
        /// down, plus the spellcasting ability modifier -- never less than 1.</summary>
        public static int MaxPrepared(CharacterState character, ClassDefinition classDefinition)
        {
            var abilityModifier = AbilityModifier.From(character.Stats.GetScore(classDefinition.SpellcastingStatId));
            var levelComponent = character.Level / classDefinition.PreparedSpellsLevelDivisor;
            return System.Math.Max(1, levelComponent + abilityModifier);
        }

        public static bool TryPrepare(CharacterState character, ClassDefinition classDefinition, IReadOnlyCollection<DefinitionId> abilityIds)
        {
            if (classDefinition.SpellcastingType != SpellcastingType.Prepared) return false;
            if (abilityIds.Count > MaxPrepared(character, classDefinition)) return false;
            if (abilityIds.Any(id => !classDefinition.SpellListAbilityIds.Contains(id))) return false;

            character.PrepareSpells(abilityIds);
            return true;
        }
    }
}
