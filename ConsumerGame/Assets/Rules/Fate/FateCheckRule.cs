using System.Collections.Generic;
using CRPG.Actors;
using CRPG.Core;

namespace CRPG.Consumer.Fate
{
    /// <summary>
    /// Gate D's real non-d20 check: rolls 4dF (four Fudge dice) instead of 1d20. Mechanically that's
    /// <c>new DiceExpression(4, 3, -8)</c> through the existing dice pipeline -- 4 dice each uniform 1..3 (a
    /// d3, the same uniform-thirds idea as Fate Condensed's own official "no Fudge dice? roll d6, 1-2=-,3-4=0,
    /// 5-6=+" substitute) sum to [4,12] with average-per-die 2 (average sum 8); the flat modifier -8 recenters
    /// that to exactly [-4,+4] with the same per-die uniform distribution real Fudge dice have. No engine
    /// change needed -- <c>RollResult.DiceTotal</c> already applies a dice expression's modifier exactly once
    /// to the whole sum.
    /// <para>
    /// Skill bonuses are looked up as <see cref="ResourceState.Max"/> on a per-skill <see cref="ResourceDefinition"/>
    /// (see <see cref="FateRulesetProvider"/>) rather than through <c>FeatureModifierCollector</c> -- that
    /// collector needs an <see cref="IRulesDatabase"/> to resolve a character's granted <c>FeatureDefinition</c>
    /// ids, and neither <c>ISkillCheckRule</c> nor <c>AbilityExecutionContext</c> carries one. Repurposing a
    /// resource's (Current, Max) pair as a (unused, rating) pair keeps this entirely inside the consumer
    /// project with zero package changes, at the cost of skills not being able to grant conditional/stacking
    /// bonuses the way a real FeatureModifier could -- a stated, deliberate simplification for this compact
    /// game, not a claim that Fate skills work identically to SRD5 ones.
    /// </para>
    /// </summary>
    public sealed class FateCheckRule : ISkillCheckRule
    {
        private readonly ICharacterRegistry characters;
        private readonly IReadOnlyDictionary<string, ResourceDefinition> skillsByQueryKey;

        public FateCheckRule(ICharacterRegistry characters, IReadOnlyDictionary<string, ResourceDefinition> skillsByQueryKey)
        {
            this.characters = characters;
            this.skillsByQueryKey = skillsByQueryKey;
        }

        /// <summary>Shared with <see cref="FateAttackEffect"/> so the "4dF is 4d3-8" arithmetic exists in
        /// exactly one place.</summary>
        public static readonly DiceExpression FourDF = new(4, 3, -8);

        public DiceExpression CheckDie => FourDF;

        public int ComputeSkillCheckBonus(RuntimeEntityId characterId, string queryKey)
        {
            if (!characters.TryGetCharacter(characterId, out var character)) return 0;
            if (!skillsByQueryKey.TryGetValue(queryKey, out var skillResource)) return 0;
            return character.TryGetResource(skillResource.Id, out var state) ? state.Max : 0;
        }

        public AdvantageState ComputeAdvantage(RuntimeEntityId characterId, string queryKey) => AdvantageState.Normal;
    }
}
