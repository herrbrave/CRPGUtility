using System.Collections.Generic;
using System.Linq;
using CRPG.Actors;
using CRPG.Core;
using CRPG.Rules;

namespace CRPG.Rules.SRD5
{
    /// <summary>Ability modifier + proficiency bonus (by character level) + whatever's gathered under the
    /// "AttackBonus" query key, same NumericRuleQuery + FeatureModifierCollector shape as
    /// <see cref="DefaultArmorClassRule"/>/<see cref="DefaultInitiativeRule"/>. Every attack is currently
    /// treated as proficient (see plan's scope note) -- there's no weapon-proficiency-gating step here yet.</summary>
    public sealed class DefaultAttackBonusRule : IAttackBonusRule, IFlankingConfigurable, IAttackRollObserver
    {
        /// <summary>The game's answer to "is this attacker flanking that target" (the optional 5e rule), set while a fight is on; null (the default) means no flanking.</summary>
        public IFlankingQuery Flanking { get; set; }
        public CommonActionRules CommonActions { get; set; }
        /// <summary>Who can see whom, when the game wires it: an attacker the target cannot see has advantage, one who cannot see its target has disadvantage (the SRD's unseen attacker and unseen target; darkness, blindness, invisibility and hiding all come through here).</summary>
        public IPerceptionQuery Perception { get; set; }

        /// <summary>Hiding, when the game has stealth: making an attack (hit or miss) reveals the attacker.</summary>
        public StealthRules Stealth { get; set; }

        public void OnAttackRolled(RuntimeEntityId attacker, RuntimeEntityId target)
        {
            CommonActions?.OnAttackRolled(attacker, target);
            Stealth?.OnAttackRolled(attacker);
        }

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

            // flanking is advantage from the battlefield itself: it combines like every other source (never stacks, opposite cancels)
            if (Flanking != null && Flanking.IsFlanking(attacker, target)) states.Add(AdvantageState.Advantage);

            if (Perception != null && attacker != target)
            {
                if (!Perception.CanSee(target, attacker)) states.Add(AdvantageState.Advantage);
                if (!Perception.CanSee(attacker, target)) states.Add(AdvantageState.Disadvantage);
            }

            if (CommonActions != null)
            {
                states.Add(CommonActions.HelpAdvantage(attacker, target));
                states.Add(CommonActions.DodgeAgainst(attacker, target));
            }

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
