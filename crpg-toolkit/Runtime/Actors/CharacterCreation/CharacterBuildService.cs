using System;
using System.Collections.Generic;
using System.Linq;
using CRPG.Core;
using CRPG.Rules;

namespace CRPG.Actors
{
    /// <summary>
    /// Walks a CharacterBuildPlan's race/class/subclass/background (and any already-resolved SelectFeature
    /// choices) to surface pending ChoiceRequirements, and turns a completed plan into a real CharacterState.
    /// </summary>
    public sealed class CharacterBuildService
    {
        private readonly IRulesDatabase rulesDatabase;

        public CharacterBuildService(IRulesDatabase rulesDatabase)
        {
            this.rulesDatabase = rulesDatabase;
        }

        /// <summary>Call after setting Race/Class/Background/Subclass, and again after resolving any choice
        /// (a newly granted feature -- e.g. a chosen subrace -- may introduce further choices of its own).</summary>
        public void RefreshPendingChoices(CharacterBuildPlan plan)
        {
            foreach (var featureId in CollectGrantedFeatureIds(plan))
            {
                if (!rulesDatabase.TryResolve<FeatureDefinition>(featureId, out var feature)) continue;
                foreach (var choice in feature.Choices) plan.AddPendingChoice(choice);
            }
        }

        public CharacterState Build(CharacterBuildPlan plan, RuntimeEntityId id)
        {
            if (!plan.IsComplete)
                throw new InvalidOperationException($"Cannot build: {plan.PendingChoices.Count} choice(s) still unresolved.");

            var character = new CharacterState(id) { Level = plan.Level };

            foreach (var (statId, score) in plan.AbilityScores.AllScores) character.Stats.SetScore(statId, score);
            foreach (var pair in plan.Appearance) character.SetAppearance(pair.Key, pair.Value);

            if (plan.Class != null)
            {
                foreach (var statId in plan.Class.SavingThrowProficiencyIds) character.GrantProficiency(statId);
                character.SpellcastingStatId = plan.Class.SpellcastingStatId;
            }

            foreach (var featureId in CollectGrantedFeatureIds(plan).Distinct())
            {
                character.GrantFeature(featureId);
                ApplyFeatureGrants(character, featureId);
            }

            // Ability-score bonuses (racial +1s, etc.) are just FeatureModifiers whose QueryKey is the stat's
            // own DefinitionId.Value -- reusing the same generic collection mechanism as any other rule query
            // (see FeatureModifierCollector) rather than a special-cased "apply racial bonus" step.
            foreach (var stat in rulesDatabase.All<StatDefinition>())
            {
                var baseScore = character.Stats.GetScore(stat.Id);
                var query = new NumericRuleQuery(character, baseScore);
                foreach (var (source, modifier) in FeatureModifierCollector.Collect(character, rulesDatabase, stat.Id.Value))
                    query.AddModifier(modifier.ToRuleModifier(source));

                var finalScore = (int)query.Evaluate();
                if (finalScore != baseScore) character.Stats.SetScore(stat.Id, finalScore);
            }

            foreach (var resolved in plan.ResolvedChoices)
            {
                if (resolved.Requirement.Type is ChoiceType.SelectSkill or ChoiceType.SelectProficiency or ChoiceType.SelectLanguage)
                {
                    foreach (var proficiencyId in resolved.Selections) character.GrantProficiency(proficiencyId);
                }

                // SelectSpell covers both cantrips (every caster) and a Known-caster's leveled spells -- both
                // are learned permanently. A Prepared-caster's leveled spells are NOT learned this way; they're
                // chosen daily from ClassDefinition.SpellListAbilityIds via SpellPreparationService instead.
                if (resolved.Requirement.Type == ChoiceType.SelectSpell)
                {
                    foreach (var abilityId in resolved.Selections) character.LearnAbility(abilityId);
                }

                // SelectEquipment/SelectAbilityScore/SelectDamageType/SelectCreatureType/SelectTerrain/
                // SelectNumber/SelectDefinition are recorded on the plan but not auto-applied here -- they need
                // starting-kit wiring or ability-score-improvement application that belong to Inventory/this
                // class's own level-up logic once those integration points exist.
            }

            return character;
        }

        private void ApplyFeatureGrants(CharacterState character, DefinitionId featureId)
        {
            if (!rulesDatabase.TryResolve<FeatureDefinition>(featureId, out var feature)) return;

            foreach (var grant in feature.GrantedResources)
                character.AddResource(new ResourceState(grant.Resource.Id, grant.MaxAmount));

            foreach (var proficiencyId in feature.GrantedProficiencies)
                character.GrantProficiency(proficiencyId);
        }

        private static IEnumerable<DefinitionId> CollectGrantedFeatureIds(CharacterBuildPlan plan)
        {
            if (plan.Race != null)
                foreach (var id in plan.Race.GrantedFeatureIds) yield return id;

            if (plan.Background != null)
                foreach (var id in plan.Background.GrantedFeatureIds) yield return id;

            if (plan.Class != null)
                foreach (var id in plan.Class.FeaturesGrantedThroughLevel(plan.Level)) yield return id;

            if (plan.Subclass != null)
                foreach (var id in plan.Subclass.FeaturesGrantedThroughLevel(plan.Level)) yield return id;

            foreach (var resolved in plan.ResolvedChoices)
            {
                if (resolved.Requirement.Type != ChoiceType.SelectFeature) continue;
                foreach (var id in resolved.Selections) yield return id;
            }
        }
    }
}
