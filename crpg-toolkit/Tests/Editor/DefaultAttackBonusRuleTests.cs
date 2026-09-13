using CRPG.Actors;
using CRPG.Combat;
using CRPG.Core;
using CRPG.Rules;
using CRPG.Rules.SRD5;
using NUnit.Framework;

namespace CRPG.Tests.EditMode
{
    public class DefaultAttackBonusRuleTests
    {
        private static readonly DefinitionId Strength = new("stat-strength");

        [Test]
        public void ComputesAbilityModifierPlusProficiencyBonusByLevel()
        {
            var registry = new CharacterRegistry();
            var character = new CharacterState(RuntimeEntityId.NewId());
            character.Stats.SetScore(Strength, 16); // +3 modifier
            character.Level = 5; // +3 proficiency bonus
            registry.Register(character);

            var rule = new DefaultAttackBonusRule(registry, new RulesDatabase());

            Assert.AreEqual(6, rule.ComputeAttackBonus(character.Id, Strength)); // 3 + 3
        }

        [Test]
        public void AddsGatheredAttackBonusModifiersOnTopOfAbilityAndProficiency()
        {
            var rulesDatabase = new RulesDatabase();
            var archeryBonus = TestDefinitionFactory.CreateFeatureModifier(DefaultAttackBonusRule.AttackBonusQueryKey, RuleModifierOperation.Add, 2f);
            var feature = TestDefinitionFactory.CreateFeatureDefinition("feature-archery", "Fighting Style: Archery", archeryBonus);
            rulesDatabase.Register(feature);

            var registry = new CharacterRegistry();
            var character = new CharacterState(RuntimeEntityId.NewId());
            character.Stats.SetScore(Strength, 14); // +2 modifier
            character.Level = 1; // +2 proficiency bonus
            character.GrantFeature(feature.Id);
            registry.Register(character);

            var rule = new DefaultAttackBonusRule(registry, rulesDatabase);

            Assert.AreEqual(6, rule.ComputeAttackBonus(character.Id, Strength)); // 2 + 2 + 2
        }

        [Test]
        public void CriticalThresholdDefaultsToTwentyWithNoGatheredModifier()
        {
            var registry = new CharacterRegistry();
            var character = new CharacterState(RuntimeEntityId.NewId());
            registry.Register(character);

            var rule = new DefaultAttackBonusRule(registry, new RulesDatabase());

            Assert.AreEqual(20, rule.ComputeCriticalThreshold(character.Id));
        }

        [Test]
        public void CriticalThresholdIsLoweredByAGatheredCriticalRangeModifier()
        {
            var rulesDatabase = new RulesDatabase();
            var improvedCritical = TestDefinitionFactory.CreateFeatureModifier(
                DefaultAttackBonusRule.CriticalRangeQueryKey, RuleModifierOperation.CriticalRange, 19f);
            var feature = TestDefinitionFactory.CreateFeatureDefinition("feature-improved-critical", "Improved Critical", improvedCritical);
            rulesDatabase.Register(feature);

            var registry = new CharacterRegistry();
            var character = new CharacterState(RuntimeEntityId.NewId());
            character.GrantFeature(feature.Id);
            registry.Register(character);

            var rule = new DefaultAttackBonusRule(registry, rulesDatabase);

            Assert.AreEqual(19, rule.ComputeCriticalThreshold(character.Id));
        }

        [Test]
        public void ComputeAttackCountIsOneWithNoExtraAttackModifier()
        {
            var registry = new CharacterRegistry();
            var character = new CharacterState(RuntimeEntityId.NewId());
            registry.Register(character);

            var rule = new DefaultAttackBonusRule(registry, new RulesDatabase());

            Assert.AreEqual(1, rule.ComputeAttackCount(character.Id));
        }

        [Test]
        public void ComputeAttackCountAddsExtraAttackModifiers()
        {
            var rulesDatabase = new RulesDatabase();
            var extraAttack = TestDefinitionFactory.CreateFeatureModifier(
                DefaultAttackBonusRule.AttackCountQueryKey, RuleModifierOperation.Add, 1f);
            var feature = TestDefinitionFactory.CreateFeatureDefinition("feature-extra-attack", "Extra Attack", extraAttack);
            rulesDatabase.Register(feature);

            var registry = new CharacterRegistry();
            var character = new CharacterState(RuntimeEntityId.NewId());
            character.GrantFeature(feature.Id);
            registry.Register(character);

            var rule = new DefaultAttackBonusRule(registry, rulesDatabase);

            Assert.AreEqual(2, rule.ComputeAttackCount(character.Id));
        }
    }
}
