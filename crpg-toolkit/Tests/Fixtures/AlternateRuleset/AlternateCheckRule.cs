using CRPG.Actors;
using CRPG.Core;
using CRPG.Rules;

namespace CRPG.Tests.Fixtures.AlternateRuleset
{
    /// <summary>
    /// Task 5.1's real, non-d20 <see cref="ISkillCheckRule"/>: rolls 3d6 (ascending vs. a DC, same comparison
    /// direction as the SRD5 d20 baseline -- only the die shape/distribution differs) with a flat +2 base bonus.
    /// The nonzero base is the deliberate, testable difference from
    /// <c>CRPG.Rules.SRD5.DefaultSkillCheckRule</c>, whose base is always zero -- reusing
    /// <see cref="FeatureModifierCollector"/> on top of that base is legitimate shared infrastructure, not a
    /// copy of the SRD5 formula itself. No feature/modifier content is registered by this fixture, so in
    /// practice the collector never contributes anything today; this is stated explicitly rather than left
    /// silently unexercised.
    /// </summary>
    public sealed class AlternateCheckRule : ISkillCheckRule
    {
        private const int BaseBonus = 2;

        private readonly ICharacterRegistry characters;
        private readonly IRulesDatabase rulesDatabase;

        public AlternateCheckRule(ICharacterRegistry characters, IRulesDatabase rulesDatabase)
        {
            this.characters = characters;
            this.rulesDatabase = rulesDatabase;
        }

        public DiceExpression CheckDie => new(3, 6);

        public int ComputeSkillCheckBonus(RuntimeEntityId characterId, string queryKey)
        {
            if (!characters.TryGetCharacter(characterId, out var character)) return BaseBonus;

            var query = new NumericRuleQuery(character, BaseBonus);
            foreach (var (source, modifier) in FeatureModifierCollector.Collect(character, rulesDatabase, queryKey))
                query.AddModifier(modifier.ToRuleModifier(source));

            return (int)query.Evaluate();
        }

        public AdvantageState ComputeAdvantage(RuntimeEntityId characterId, string queryKey) => AdvantageState.Normal;
    }
}
