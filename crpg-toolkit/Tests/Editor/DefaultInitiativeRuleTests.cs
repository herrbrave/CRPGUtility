using CRPG.Actors;
using CRPG.Combat;
using CRPG.Core;
using CRPG.Rules;
using CRPG.Rules.SRD5;
using NUnit.Framework;

namespace CRPG.Tests.EditMode
{
    public class DefaultInitiativeRuleTests
    {
        [Test]
        public void RollInitiativeAddsGatheredModifierToTheD20Roll()
        {
            var rulesDatabase = new RulesDatabase();
            var dexBonus = TestDefinitionFactory.CreateFeatureModifier(DefaultInitiativeRule.QueryKey, RuleModifierOperation.Add, 3f);
            var feature = TestDefinitionFactory.CreateFeatureDefinition("feature-quick", "Quick", dexBonus);
            rulesDatabase.Register(feature);

            var registry = new CharacterRegistry();
            var character = new CharacterState(RuntimeEntityId.NewId());
            character.GrantFeature(feature.Id);
            registry.Register(character);

            var random = new TestRandomSource();
            random.EnqueueResult(14);
            var dice = new DiceService(random);
            var rule = new DefaultInitiativeRule(registry, rulesDatabase, dice);

            var result = rule.RollInitiative(character.Id);

            Assert.AreEqual(17, result.Total); // 14 + 3
        }

        [Test]
        public void RollInitiativeWorksWithNoModifiersForAnUnknownCombatant()
        {
            var registry = new CharacterRegistry();
            var random = new TestRandomSource();
            random.EnqueueResult(9);
            var dice = new DiceService(random);
            var rule = new DefaultInitiativeRule(registry, new RulesDatabase(), dice);

            var result = rule.RollInitiative(RuntimeEntityId.NewId());

            Assert.AreEqual(9, result.Total);
        }
    }
}
