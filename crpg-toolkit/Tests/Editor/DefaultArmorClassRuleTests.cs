using CRPG.Actors;
using CRPG.Combat;
using CRPG.Core;
using CRPG.Rules;
using NUnit.Framework;

namespace CRPG.Tests.EditMode
{
    public class DefaultArmorClassRuleTests
    {
        private static readonly DefinitionId Dexterity = new("stat-dexterity");

        [Test]
        public void ComputesTenPlusDexterityModifierWithNoGatheredBonuses()
        {
            var registry = new CharacterRegistry();
            var character = new CharacterState(RuntimeEntityId.NewId());
            character.Stats.SetScore(Dexterity, 14); // +2 modifier
            registry.Register(character);

            var rule = new DefaultArmorClassRule(registry, new RulesDatabase(), Dexterity);

            Assert.AreEqual(12, rule.ComputeArmorClass(character.Id)); // 10 + 2
        }

        [Test]
        public void AddsGatheredArmorClassModifiersOnTopOfTheBase()
        {
            var rulesDatabase = new RulesDatabase();
            var shieldBonus = TestDefinitionFactory.CreateFeatureModifier(DefaultArmorClassRule.QueryKey, RuleModifierOperation.Add, 3f);
            var feature = TestDefinitionFactory.CreateFeatureDefinition("feature-chain-shirt", "Chain Shirt", shieldBonus);
            rulesDatabase.Register(feature);

            var registry = new CharacterRegistry();
            var character = new CharacterState(RuntimeEntityId.NewId());
            character.Stats.SetScore(Dexterity, 12); // +1 modifier
            character.GrantFeature(feature.Id);
            registry.Register(character);

            var rule = new DefaultArmorClassRule(registry, rulesDatabase, Dexterity);

            Assert.AreEqual(14, rule.ComputeArmorClass(character.Id)); // 10 + 1 + 3
        }

        [Test]
        public void ReturnsTenForAnUnknownCombatant()
        {
            var rule = new DefaultArmorClassRule(new CharacterRegistry(), new RulesDatabase(), Dexterity);

            Assert.AreEqual(10, rule.ComputeArmorClass(RuntimeEntityId.NewId()));
        }
    }
}
