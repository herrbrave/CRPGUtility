using System.Linq;
using CRPG.Core;

namespace CRPG.Actors
{
    /// <summary>SRD5's Variant: Encumbrance rule -- carrying capacity and the Encumbered/Heavily Encumbered
    /// tiers, keyed off a caller-supplied Strength-like stat id rather than a hardcoded "stat-strength" so this
    /// stays ruleset-agnostic (matches ClassDefinition.SpellcastingStatId's own "configurable id, not a
    /// hardcoded assumption" convention). Deliberately takes totalWeight as a plain float rather than reading
    /// inventory itself -- CRPG.Actors has no reason to depend on CRPG.Inventory's item/slot data structures,
    /// the caller (wherever equip/loot/drop already lives) is in a better position to sum it.</summary>
    public static class EncumbranceService
    {
        public static float ComputeCarryingCapacity(CharacterState character, DefinitionId strengthStatId) =>
            character.Stats.GetScore(strengthStatId) * 15f;

        public static EncumbranceTier ComputeTier(CharacterState character, DefinitionId strengthStatId, float totalWeight)
        {
            var strength = character.Stats.GetScore(strengthStatId);
            if (totalWeight > strength * 10f) return EncumbranceTier.HeavilyEncumbered;
            if (totalWeight > strength * 5f) return EncumbranceTier.Encumbered;
            return EncumbranceTier.Unencumbered;
        }

        /// <summary>Applies/removes condition-encumbered / condition-heavily-encumbered to match the character's
        /// current tier, reusing the exact same Speed-modifier-via-condition mechanism Grappled uses for its own
        /// speed reduction. Call this after anything that changes carried weight (equip/unequip/loot/drop).
        /// Either condition id can be DefinitionId.None to skip that tier (e.g. a ruleset not authoring one).</summary>
        public static void Refresh(
            CharacterState character, DefinitionId strengthStatId, float totalWeight,
            DefinitionId encumberedConditionId, DefinitionId heavilyEncumberedConditionId)
        {
            var tier = ComputeTier(character, strengthStatId, totalWeight);

            RemoveIfPresent(character, encumberedConditionId);
            RemoveIfPresent(character, heavilyEncumberedConditionId);

            if (tier == EncumbranceTier.Encumbered && encumberedConditionId.IsValid)
                character.ApplyCondition(new ConditionInstance(encumberedConditionId, RuntimeEntityId.None, character.Id));
            else if (tier == EncumbranceTier.HeavilyEncumbered && heavilyEncumberedConditionId.IsValid)
                character.ApplyCondition(new ConditionInstance(heavilyEncumberedConditionId, RuntimeEntityId.None, character.Id));
        }

        private static void RemoveIfPresent(CharacterState character, DefinitionId conditionId)
        {
            if (!conditionId.IsValid) return;
            var existing = character.Conditions.FirstOrDefault(c => c.ConditionId == conditionId);
            if (existing != null) character.RemoveCondition(existing);
        }
    }
}
