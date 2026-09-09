using System.Collections.Generic;
using System.Linq;
using CRPG.Actors;
using CRPG.Core;
using CRPG.Rules;

namespace CRPG.Combat
{
    /// <summary>Ability modifier + proficiency bonus (by character level) + whatever's gathered under the
    /// "AttackBonus" query key, same NumericRuleQuery + FeatureModifierCollector shape as
    /// <see cref="DefaultArmorClassRule"/>/<see cref="DefaultInitiativeRule"/>. Every attack is currently
    /// treated as proficient (see plan's scope note) -- there's no weapon-proficiency-gating step here yet.</summary>
    public sealed class DefaultAttackBonusRule : IAttackBonusRule
    {
        public const string AttackBonusQueryKey = "AttackBonus";
        public const string CriticalRangeQueryKey = "CriticalRange";
        public const string AttacksAgainstQueryKey = "AttacksAgainst";
        public const string AttackCountQueryKey = "AttackCount";
        private const int DefaultCriticalThreshold = 20;

        private readonly ICharacterRegistry characters;
        private readonly IRulesDatabase rulesDatabase;

        public DefaultAttackBonusRule(ICharacterRegistry characters, IRulesDatabase rulesDatabase)
        {
            this.characters = characters;
            this.rulesDatabase = rulesDatabase;
        }

        public int ComputeAttackBonus(RuntimeEntityId attacker, DefinitionId governingStatId)
        {
            if (!characters.TryGetCharacter(attacker, out var character)) return 0;

            var abilityModifier = AbilityModifier.From(character.Stats.GetScore(governingStatId));
            var proficiencyBonus = ProficiencyBonus.From(character.Level);
            var query = new NumericRuleQuery(character, abilityModifier + proficiencyBonus);
            foreach (var (source, modifier) in FeatureModifierCollector.Collect(character, rulesDatabase, AttackBonusQueryKey))
                query.AddModifier(modifier.ToRuleModifier(source));

            return (int)query.Evaluate();
        }

        /// <summary>Not routed through NumericRuleQuery -- CriticalRange isn't one of the operations it
        /// resolves (see RuleModifierOperation's own doc comment: it's "consumed by more specialized systems").
        /// A lower threshold (e.g. Improved Critical's 19) means crits happen more often, so the *minimum*
        /// gathered value wins.</summary>
        public int ComputeCriticalThreshold(RuntimeEntityId attacker)
        {
            if (!characters.TryGetCharacter(attacker, out var character)) return DefaultCriticalThreshold;

            var gathered = FeatureModifierCollector.Collect(character, rulesDatabase, CriticalRangeQueryKey)
                .Select(m => (int)m.Modifier.ToRuleModifier(m.Source).Value)
                .ToList();

            return gathered.Count == 0 ? DefaultCriticalThreshold : gathered.Min();
        }

        /// <summary>Combines the attacker's own "AttackBonus"-tagged advantage/disadvantage (e.g. Reckless
        /// Attack) with the target's "AttacksAgainst"-tagged advantage/disadvantage (e.g. a Prone target) --
        /// the same "never stacks, opposite cancels" rule (AdvantageStateExtensions.Combine) applies across
        /// both sources at once.</summary>
        public AdvantageState ComputeAdvantage(RuntimeEntityId attacker, RuntimeEntityId target)
        {
            var states = new List<AdvantageState>();

            if (characters.TryGetCharacter(attacker, out var attackerCharacter))
                states.Add(AdvantageCollector.Collect(attackerCharacter, rulesDatabase, AttackBonusQueryKey));

            if (characters.TryGetCharacter(target, out var targetCharacter))
                states.Add(AdvantageCollector.Collect(targetCharacter, rulesDatabase, AttacksAgainstQueryKey));

            return AdvantageStateExtensions.Combine(states);
        }

        /// <summary>1 + whatever's gathered under "AttackCount" (Extra Attack, etc.), via NumericRuleQuery like
        /// every other numeric derived stat here -- so a homebrew "Extra Attack (2)" granting +2 composes
        /// correctly with the base Extra Attack's +1 rather than needing special-case stacking logic.</summary>
        public int ComputeAttackCount(RuntimeEntityId attacker)
        {
            if (!characters.TryGetCharacter(attacker, out var character)) return 1;

            var query = new NumericRuleQuery(character, 1);
            foreach (var (source, modifier) in FeatureModifierCollector.Collect(character, rulesDatabase, AttackCountQueryKey))
                query.AddModifier(modifier.ToRuleModifier(source));

            return System.Math.Max(1, (int)query.Evaluate());
        }
    }
}
