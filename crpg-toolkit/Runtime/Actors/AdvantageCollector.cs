using System.Collections.Generic;
using CRPG.Core;
using CRPG.Rules;

namespace CRPG.Actors
{
    /// <summary>Gathers Advantage/Disadvantage-tagged FeatureModifiers under a query key, same shape as
    /// FeatureModifierCollector itself. A single query key can carry both numeric (Add/Subtract/...) and
    /// Advantage/Disadvantage modifiers at once -- FeatureModifierCollector.Collect returns everything tagged
    /// with the key regardless of Operation, and each caller (NumericRuleQuery vs. this) just picks out what
    /// it understands, exactly like DefaultAttackBonusRule already does for CriticalRange.</summary>
    public static class AdvantageCollector
    {
        public static AdvantageState Collect(CharacterState character, IRulesDatabase rulesDatabase, string queryKey)
        {
            var states = new List<AdvantageState>();
            foreach (var (source, modifier) in FeatureModifierCollector.Collect(character, rulesDatabase, queryKey))
            {
                var operation = modifier.ToRuleModifier(source).Operation;
                if (operation == RuleModifierOperation.Advantage) states.Add(AdvantageState.Advantage);
                else if (operation == RuleModifierOperation.Disadvantage) states.Add(AdvantageState.Disadvantage);
            }

            return AdvantageStateExtensions.Combine(states);
        }
    }
}
