using System.Linq;
using CRPG.Actors;
using CRPG.Core;
using CRPG.Rules;
using NUnit.Framework;

namespace CRPG.Tests.EditMode
{
    public class FeatureModifierCollectorTests
    {
        [Test]
        public void CollectReturnsModifiersFromActiveFeaturesMatchingTheQueryKey()
        {
            var rulesDatabase = new RulesDatabase();
            var shieldBonus = TestDefinitionFactory.CreateFeatureModifier("ArmorClass", RuleModifierOperation.Add, 2f);
            var unrelated = TestDefinitionFactory.CreateFeatureModifier("AttackBonus", RuleModifierOperation.Add, 1f);
            var feature = TestDefinitionFactory.CreateFeatureDefinition("feature-shield", "Shield Proficiency", shieldBonus, unrelated);
            rulesDatabase.Register(feature);

            var character = new CharacterState(RuntimeEntityId.NewId());
            character.GrantFeature(feature.Id);

            var results = FeatureModifierCollector.Collect(character, rulesDatabase, "ArmorClass").ToList();

            Assert.AreEqual(1, results.Count);
            Assert.AreEqual("Shield Proficiency", results[0].Source);
            Assert.AreEqual(RuleModifierOperation.Add, results[0].Modifier.ToRuleModifier("x").Operation);
        }

        [Test]
        public void CollectReturnsModifiersFromActiveConditions()
        {
            var rulesDatabase = new RulesDatabase();
            var penalty = TestDefinitionFactory.CreateFeatureModifier("ArmorClass", RuleModifierOperation.Subtract, 2f);
            var condition = TestDefinitionFactory.CreateConditionDefinition("condition-prone", "Prone", penalty);
            rulesDatabase.Register(condition);

            var character = new CharacterState(RuntimeEntityId.NewId());
            character.ApplyCondition(new ConditionInstance(condition.Id, RuntimeEntityId.NewId(), character.Id));

            var results = FeatureModifierCollector.Collect(character, rulesDatabase, "ArmorClass").ToList();

            Assert.AreEqual(1, results.Count);
            Assert.AreEqual("Prone", results[0].Source);
        }

        [Test]
        public void CollectIgnoresFeaturesThatDoNotResolve()
        {
            var rulesDatabase = new RulesDatabase();
            var character = new CharacterState(RuntimeEntityId.NewId());
            character.GrantFeature(new DefinitionId("feature-does-not-exist"));

            var results = FeatureModifierCollector.Collect(character, rulesDatabase, "ArmorClass").ToList();

            Assert.IsEmpty(results);
        }
    }
}
