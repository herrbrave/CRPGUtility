using CRPG.Actors;
using CRPG.Combat;
using CRPG.Core;
using CRPG.Rules;
using CRPG.Rules.SRD5;
using NUnit.Framework;

namespace CRPG.Tests.EditMode
{
    public class DefaultMovementSpeedRuleTests
    {
        private static readonly DefinitionId Exhaustion = new("condition-exhaustion");

        [Test]
        public void ReturnsBaseSpeedWhenNoModifiersOrExhaustionApply()
        {
            var registry = new CharacterRegistry();
            var character = new CharacterState(RuntimeEntityId.NewId());
            registry.Register(character);

            var rule = new DefaultMovementSpeedRule(registry, new RulesDatabase());

            Assert.AreEqual(30f, rule.ComputeEffectiveSpeed(character.Id, 30f));
        }

        [Test]
        public void GrappledOverridesSpeedToZero()
        {
            var rulesDatabase = new RulesDatabase();
            var grappledModifier = TestDefinitionFactory.CreateFeatureModifier(
                DefaultMovementSpeedRule.SpeedQueryKey, RuleModifierOperation.Override, 0f);
            var condition = TestDefinitionFactory.CreateConditionDefinition("condition-grappled", "Grappled", grappledModifier);
            rulesDatabase.Register(condition);

            var registry = new CharacterRegistry();
            var character = new CharacterState(RuntimeEntityId.NewId());
            character.ApplyCondition(new ConditionInstance(condition.Id, RuntimeEntityId.None, character.Id));
            registry.Register(character);

            var rule = new DefaultMovementSpeedRule(registry, rulesDatabase);

            Assert.AreEqual(0f, rule.ComputeEffectiveSpeed(character.Id, 30f));
        }

        [Test]
        public void ExhaustionLevelTwoHalvesSpeed()
        {
            var registry = new CharacterRegistry();
            var character = new CharacterState(RuntimeEntityId.NewId());
            ExhaustionService.IncreaseExhaustion(character, Exhaustion, amount: 2);
            registry.Register(character);

            var rule = new DefaultMovementSpeedRule(registry, new RulesDatabase(), Exhaustion);

            Assert.AreEqual(15f, rule.ComputeEffectiveSpeed(character.Id, 30f));
        }

        [Test]
        public void ExhaustionLevelFiveZeroesSpeed()
        {
            var registry = new CharacterRegistry();
            var character = new CharacterState(RuntimeEntityId.NewId());
            ExhaustionService.IncreaseExhaustion(character, Exhaustion, amount: 5);
            registry.Register(character);

            var rule = new DefaultMovementSpeedRule(registry, new RulesDatabase(), Exhaustion);

            Assert.AreEqual(0f, rule.ComputeEffectiveSpeed(character.Id, 30f));
        }

        [Test]
        public void ExhaustionLevelOneDoesNotAffectSpeed()
        {
            var registry = new CharacterRegistry();
            var character = new CharacterState(RuntimeEntityId.NewId());
            ExhaustionService.IncreaseExhaustion(character, Exhaustion, amount: 1);
            registry.Register(character);

            var rule = new DefaultMovementSpeedRule(registry, new RulesDatabase(), Exhaustion);

            Assert.AreEqual(30f, rule.ComputeEffectiveSpeed(character.Id, 30f));
        }

        [Test]
        public void IgnoresExhaustionWhenNoExhaustionConditionIdIsConfigured()
        {
            var registry = new CharacterRegistry();
            var character = new CharacterState(RuntimeEntityId.NewId());
            ExhaustionService.IncreaseExhaustion(character, Exhaustion, amount: 5);
            registry.Register(character);

            var rule = new DefaultMovementSpeedRule(registry, new RulesDatabase()); // no exhaustionConditionId passed

            Assert.AreEqual(30f, rule.ComputeEffectiveSpeed(character.Id, 30f));
        }
    }
}
