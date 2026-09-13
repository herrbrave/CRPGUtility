using System.Linq;
using CRPG.Actors;
using CRPG.Core;
using CRPG.Rules;

namespace CRPG.Rules.SRD5
{
    /// <summary>Gathers Resistance/Immunity/Vulnerability-tagged FeatureModifiers under the
    /// "DamageType:&lt;damageTypeId&gt;" query key -- same FeatureModifierCollector shape as
    /// DefaultAttackBonusRule/DefaultSavingThrowRule. SRD5's stacking rule: Immunity always wins (0 damage);
    /// Resistance and Vulnerability from different sources cancel out (no change) rather than both applying;
    /// otherwise Resistance halves (rounded down) and Vulnerability doubles.</summary>
    public sealed class DefaultDamageResistanceRule : IDamageResistanceRule
    {
        public const string QueryKeyPrefix = "DamageType:";

        private readonly ICharacterRegistry characters;
        private readonly IRulesDatabase rulesDatabase;

        public DefaultDamageResistanceRule(ICharacterRegistry characters, IRulesDatabase rulesDatabase)
        {
            this.characters = characters;
            this.rulesDatabase = rulesDatabase;
        }

        public int ApplyResistance(RuntimeEntityId target, DefinitionId damageTypeId, int rawAmount)
        {
            if (!damageTypeId.IsValid) return rawAmount;
            if (!characters.TryGetCharacter(target, out var character)) return rawAmount;

            var operations = FeatureModifierCollector.Collect(character, rulesDatabase, QueryKeyPrefix + damageTypeId.Value)
                .Select(m => m.Modifier.ToRuleModifier(m.Source).Operation)
                .ToList();

            if (operations.Contains(RuleModifierOperation.Immunity)) return 0;

            var hasResistance = operations.Contains(RuleModifierOperation.Resistance);
            var hasVulnerability = operations.Contains(RuleModifierOperation.Vulnerability);
            if (hasResistance == hasVulnerability) return rawAmount; // neither, or both cancelling out

            return hasResistance ? rawAmount / 2 : rawAmount * 2;
        }
    }
}
