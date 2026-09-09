using System.Linq;
using CRPG.Actors;
using CRPG.Core;
using CRPG.Rules;
using UnityEngine;

namespace CRPG.Combat
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

        public DefaultMovementSpeedRule(ICharacterRegistry characters, IRulesDatabase rulesDatabase, DefinitionId exhaustionConditionId = default)
        {
            this.characters = characters;
            this.rulesDatabase = rulesDatabase;
            this.exhaustionConditionId = exhaustionConditionId;
        }

        public float ComputeEffectiveSpeed(RuntimeEntityId entityId, float baseSpeed)
        {
            if (!characters.TryGetCharacter(entityId, out var character)) return baseSpeed;

            var query = new NumericRuleQuery(character, baseSpeed);
            foreach (var (source, modifier) in FeatureModifierCollector.Collect(character, rulesDatabase, SpeedQueryKey))
                query.AddModifier(modifier.ToRuleModifier(source));
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
    }
}
