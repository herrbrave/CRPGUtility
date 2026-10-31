using System.Linq;
using CRPG.Actors;
using CRPG.Core;
using CRPG.Rules;
using UnityEngine;

namespace CRPG.Rules.SRD5
{
    /// <summary>Gathers "Speed"-tagged FeatureModifiers (e.g. Grappled's Override 0) the same
    /// NumericRuleQuery + FeatureModifierCollector way as every other Default*Rule, seeded with whatever base
    /// speed the caller passes in (there's no single universal base the way ArmorClass has 10 -- a race's own
    /// granted feature is expected to contribute its base Speed as an Add modifier). Exhaustion is special-cased
    /// directly rather than authored as a flat modifier, since SRD5's own rules hardcode its effects by numeric
    /// level (2+: half speed, 5+: speed 0) rather than treating it as a composable flat bonus.</summary>
    public sealed class DefaultMovementSpeedRule : IMovementSpeedRule
    {
        public const string SpeedQueryKey = "Speed";

        private readonly ICharacterRegistry characters;
        private readonly IRulesDatabase rulesDatabase;
        private readonly DefinitionId exhaustionConditionId;
        private readonly bool baseIncludesFeatures;
        private readonly float distanceScale;

        public DefaultMovementSpeedRule(ICharacterRegistry characters, IRulesDatabase rulesDatabase, DefinitionId exhaustionConditionId = default,
            bool baseIncludesFeatures = false, float distanceScale = 1f)
        {
            this.characters = characters;
            this.rulesDatabase = rulesDatabase;
            this.exhaustionConditionId = exhaustionConditionId;
            this.baseIncludesFeatures = baseIncludesFeatures;
            this.distanceScale = distanceScale;
        }

        public float ComputeEffectiveSpeed(RuntimeEntityId entityId, float baseSpeed)
        {
            if (!characters.TryGetCharacter(entityId, out var character)) return baseSpeed;

            var query = new NumericRuleQuery(character, baseSpeed);
            if (!this.baseIncludesFeatures)
            {
                foreach (DefinitionId featureId in character.Features)
                {
                    if (!this.rulesDatabase.TryResolve<FeatureDefinition>(featureId, out FeatureDefinition feature)) continue;
                    foreach (FeatureModifier modifier in feature.Modifiers) AddSpeedModifier(query, modifier, feature.DisplayName);
                }
            }
            foreach (ConditionInstance condition in character.Conditions)
            {
                if (!this.rulesDatabase.TryResolve<ConditionDefinition>(condition.ConditionId, out ConditionDefinition definition)) continue;
                foreach (FeatureModifier modifier in definition.Modifiers) AddSpeedModifier(query, modifier, definition.DisplayName);
            }
            var speed = (float)query.Evaluate();

            if (exhaustionConditionId.IsValid)
            {
                var exhaustion = character.Conditions.FirstOrDefault(c => c.ConditionId == exhaustionConditionId);
                if (exhaustion != null)
                {
                    if (exhaustion.StackCount >= 5) speed = 0f;
                    else if (exhaustion.StackCount >= 2) speed *= 0.5f;
                }
            }

            return Mathf.Max(0f, speed);
        }

        private void AddSpeedModifier(NumericRuleQuery query, FeatureModifier modifier, string source)
        {
            if (modifier.QueryKey != SpeedQueryKey) return;
            RuleModifier rule = modifier.ToRuleModifier(source);
            double value = rule.Operation == RuleModifierOperation.Multiply ? rule.Value : rule.Value * this.distanceScale;
            query.AddModifier(new RuleModifier(rule.Operation, value, rule.Source, rule.Priority));
        }
    }
}
