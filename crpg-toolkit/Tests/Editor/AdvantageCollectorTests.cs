using CRPG.Actors;
using CRPG.Core;
using CRPG.Rules;
using NUnit.Framework;

namespace CRPG.Tests.EditMode
{
    public class AdvantageCollectorTests
    {
        [Test]
        public void ReturnsNormalWhenNoModifiersAreGathered()
        {
            var rulesDatabase = new RulesDatabase();
            var character = new CharacterState(RuntimeEntityId.NewId());

            Assert.AreEqual(AdvantageState.Normal, AdvantageCollector.Collect(character, rulesDatabase, "AttackBonus"));
        }

        [Test]
        public void ReturnsAdvantageWhenOnlyAnAdvantageModifierIsGathered()
        {
            var rulesDatabase = new RulesDatabase();
            var modifier = TestDefinitionFactory.CreateFeatureModifier("AttackBonus", RuleModifierOperation.Advantage, 0f);
            var feature = TestDefinitionFactory.CreateFeatureDefinition("feature-reckless", "Reckless Attack", modifier);
            rulesDatabase.Register(feature);

            var character = new CharacterState(RuntimeEntityId.NewId());
            character.GrantFeature(feature.Id);

            Assert.AreEqual(AdvantageState.Advantage, AdvantageCollector.Collect(character, rulesDatabase, "AttackBonus"));
        }

        [Test]
        public void ReturnsDisadvantageWhenOnlyADisadvantageModifierIsGathered()
        {
            var rulesDatabase = new RulesDatabase();
            var modifier = TestDefinitionFactory.CreateFeatureModifier("AttackBonus", RuleModifierOperation.Disadvantage, 0f);
            var condition = TestDefinitionFactory.CreateConditionDefinition("condition-poisoned", "Poisoned", modifier);
            rulesDatabase.Register(condition);

            var character = new CharacterState(RuntimeEntityId.NewId());
            character.ApplyCondition(new ConditionInstance(condition.Id, RuntimeEntityId.None, character.Id));

            Assert.AreEqual(AdvantageState.Disadvantage, AdvantageCollector.Collect(character, rulesDatabase, "AttackBonus"));
        }

        [Test]
        public void CombinesToNormalWhenBothAdvantageAndDisadvantageAreGathered()
        {
            var rulesDatabase = new RulesDatabase();
            var advantageModifier = TestDefinitionFactory.CreateFeatureModifier("AttackBonus", RuleModifierOperation.Advantage, 0f);
            var disadvantageModifier = TestDefinitionFactory.CreateFeatureModifier("AttackBonus", RuleModifierOperation.Disadvantage, 0f);
            var recklessFeature = TestDefinitionFactory.CreateFeatureDefinition("feature-reckless", "Reckless Attack", advantageModifier);
            var poisonedCondition = TestDefinitionFactory.CreateConditionDefinition("condition-poisoned", "Poisoned", disadvantageModifier);
            rulesDatabase.Register(recklessFeature);
            rulesDatabase.Register(poisonedCondition);

            var character = new CharacterState(RuntimeEntityId.NewId());
            character.GrantFeature(recklessFeature.Id);
            character.ApplyCondition(new ConditionInstance(poisonedCondition.Id, RuntimeEntityId.None, character.Id));

            Assert.AreEqual(AdvantageState.Normal, AdvantageCollector.Collect(character, rulesDatabase, "AttackBonus"));
        }

        [Test]
        public void IgnoresModifiersTaggedWithADifferentQueryKey()
        {
            var rulesDatabase = new RulesDatabase();
            var modifier = TestDefinitionFactory.CreateFeatureModifier("SavingThrow:stat-dexterity", RuleModifierOperation.Advantage, 0f);
            var feature = TestDefinitionFactory.CreateFeatureDefinition("feature-unrelated", "Unrelated", modifier);
            rulesDatabase.Register(feature);

            var character = new CharacterState(RuntimeEntityId.NewId());
            character.GrantFeature(feature.Id);

            Assert.AreEqual(AdvantageState.Normal, AdvantageCollector.Collect(character, rulesDatabase, "AttackBonus"));
        }
    }
}
