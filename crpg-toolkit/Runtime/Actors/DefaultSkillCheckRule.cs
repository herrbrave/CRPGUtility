using CRPG.Core;
using CRPG.Rules;

namespace CRPG.Actors
{
    /// <summary>Whatever's gathered under the caller-supplied query key via NumericRuleQuery +
    /// FeatureModifierCollector -- the exact logic DialogueRunner.PerformCheck and
    /// DialogueCheckResolver.ResolveBestPartyMember used to each reimplement inline. No ability-modifier/
    /// proficiency-bonus base is added here (unlike DefaultAttackBonusRule/DefaultSavingThrowRule) -- a
    /// ruleset opts a stat/skill into being checkable purely by tagging a FeatureModifier with the matching
    /// key (see CheckNode's own doc comment), so the base stays 0 and everything comes from gathered
    /// modifiers, exactly matching the pre-existing dialogue-check behavior this replaces.</summary>
    public sealed class DefaultSkillCheckRule : ISkillCheckRule
    {
        private readonly ICharacterRegistry characters;
        private readonly IRulesDatabase rulesDatabase;

        public DefaultSkillCheckRule(ICharacterRegistry characters, IRulesDatabase rulesDatabase)
        {
            this.characters = characters;
            this.rulesDatabase = rulesDatabase;
        }

        public int ComputeSkillCheckBonus(RuntimeEntityId characterId, string queryKey)
        {
            if (!characters.TryGetCharacter(characterId, out var character)) return 0;

            var query = new NumericRuleQuery(character, 0);
            foreach (var (source, modifier) in FeatureModifierCollector.Collect(character, rulesDatabase, queryKey))
                query.AddModifier(modifier.ToRuleModifier(source));

            return (int)query.Evaluate();
        }

        public AdvantageState ComputeAdvantage(RuntimeEntityId characterId, string queryKey)
        {
            if (!characters.TryGetCharacter(characterId, out var character)) return AdvantageState.Normal;
            return AdvantageCollector.Collect(character, rulesDatabase, queryKey);
        }
    }
}
