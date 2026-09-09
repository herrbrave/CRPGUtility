using System.Linq;
using CRPG.Core;
using CRPG.Rules;

namespace CRPG.Actors
{
    /// <summary>
    /// Turns an NpcDefinition into a real CharacterState -- the NPC counterpart to CharacterBuildService.Build,
    /// minus ChoiceRequirement resolution (there's nothing to resolve; every NpcDefinition field is already a
    /// flat, designer-authored value). Reuses the exact same "gather FeatureModifiers keyed by stat id" ability-
    /// score-bonus pass CharacterBuildService.Build does, so a feature granting +2 Strength works identically
    /// whether it reached the character via a player's Race choice or an NPC's authored FeatureIds.
    /// <para>Deliberately stops at CharacterState -- Faction (Combat.Combatant's concern, not CharacterState's),
    /// equipping StartingItemIds into real inventory slots, wiring DialogueId into an actual conversation
    /// trigger, and instantiating VisualPrefab are all left to whoever spawns this NPC into a scene. Same
    /// "authoring stops at data" boundary CharacterBuildService.Build already draws around a player's
    /// SelectEquipment choice.</para>
    /// </summary>
    public static class NpcSpawnService
    {
        public static CharacterState Spawn(NpcDefinition definition, IRulesDatabase rulesDatabase, RuntimeEntityId id)
        {
            var character = new CharacterState(id) { Level = definition.Level, SpellcastingStatId = definition.SpellcastingStatId };

            foreach (var entry in definition.AbilityScores) character.Stats.SetScore(entry.StatId, entry.Score);
            foreach (var proficiencyId in definition.Proficiencies) character.GrantProficiency(proficiencyId);
            foreach (var grant in definition.Resources) character.AddResource(new ResourceState(grant.Resource.Id, grant.MaxAmount));
            foreach (var abilityId in definition.AbilityIds) character.LearnAbility(abilityId);

            foreach (var featureId in definition.FeatureIds.Distinct())
            {
                character.GrantFeature(featureId);
                ApplyFeatureGrants(character, rulesDatabase, featureId);
            }

            // Same generic "ability-score bonuses are just FeatureModifiers keyed by the stat's own id" pass
            // CharacterBuildService.Build uses -- a race/monster-trait feature's +2 Strength applies the same
            // way here as it does for a player.
            foreach (var stat in rulesDatabase.All<StatDefinition>())
            {
                var baseScore = character.Stats.GetScore(stat.Id);
                var query = new NumericRuleQuery(character, baseScore);
                foreach (var (source, modifier) in FeatureModifierCollector.Collect(character, rulesDatabase, stat.Id.Value))
                    query.AddModifier(modifier.ToRuleModifier(source));

                var finalScore = (int)query.Evaluate();
                if (finalScore != baseScore) character.Stats.SetScore(stat.Id, finalScore);
            }

            return character;
        }

        private static void ApplyFeatureGrants(CharacterState character, IRulesDatabase rulesDatabase, DefinitionId featureId)
        {
            if (!rulesDatabase.TryResolve<FeatureDefinition>(featureId, out var feature)) return;

            foreach (var grant in feature.GrantedResources)
                character.AddResource(new ResourceState(grant.Resource.Id, grant.MaxAmount));

            foreach (var proficiencyId in feature.GrantedProficiencies)
                character.GrantProficiency(proficiencyId);
        }
    }
}
