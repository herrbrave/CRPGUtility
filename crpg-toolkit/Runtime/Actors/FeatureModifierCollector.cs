using System.Collections.Generic;
using CRPG.Core;

namespace CRPG.Actors
{
    /// <summary>
    /// Gathers every FeatureModifier tagged with a given query key from a character's currently active
    /// features and conditions, ready to feed into a ruleset's NumericRuleQuery (spec "Derived-stat rule
    /// engine": "Base rules -&gt; Race -&gt; Class -&gt; Subclass -&gt; Features -&gt; Conditions -&gt; Equipment -&gt;
    /// Items -&gt; Spells -&gt; Environment"). Equipment/spell contributions join in once Inventory/Abilities have
    /// their own way to enumerate active effects -- this only covers the two sources CRPG.Actors itself owns.
    /// </summary>
    public static class FeatureModifierCollector
    {
        public static IEnumerable<(string Source, FeatureModifier Modifier)> Collect(
            CharacterState character, IRulesDatabase rulesDatabase, string queryKey)
        {
            foreach (var featureId in character.Features)
            {
                if (!rulesDatabase.TryResolve<FeatureDefinition>(featureId, out var feature)) continue;

                foreach (var modifier in feature.Modifiers)
                {
                    if (modifier.QueryKey == queryKey) yield return (feature.DisplayName, modifier);
                }
            }

            foreach (var condition in character.Conditions)
            {
                if (!rulesDatabase.TryResolve<ConditionDefinition>(condition.ConditionId, out var conditionDefinition)) continue;

                foreach (var modifier in conditionDefinition.Modifiers)
                {
                    if (modifier.QueryKey == queryKey) yield return (conditionDefinition.DisplayName, modifier);
                }
            }
        }
    }
}
