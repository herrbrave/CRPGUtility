using CRPG.Actors;
using CRPG.Combat;
using CRPG.Core;
using CRPG.Rules;
using NUnit.Framework;

namespace CRPG.Tests.EditMode
{
    public class DefaultDamageResistanceRuleTests
    {
        private static readonly DefinitionId Fire = new("damage-fire");

        [Test]
        public void ReturnsRawAmountWhenTargetHasNoRelevantModifiers()
        {
            var registry = new CharacterRegistry();
            var character = new CharacterState(RuntimeEntityId.NewId());
            registry.Register(character);

            var rule = new DefaultDamageResistanceRule(registry, new RulesDatabase());

            Assert.AreEqual(10, rule.ApplyResistance(character.Id, Fire, 10));
        }

        [Test]
        public void HalvesDamageRoundedDownWhenResistant()
        {
            var rulesDatabase = new RulesDatabase();
            var modifier = TestDefinitionFactory.CreateFeatureModifier(
                DefaultDamageResistanceRule.QueryKeyPrefix + Fire.Value, RuleModifierOperation.Resistance, 0f);
            var feature = TestDefinitionFactory.CreateFeatureDefinition("feature-fire-resistant", "Fire Resistant", modifier);
            rulesDatabase.Register(feature);

            var registry = new CharacterRegistry();
            var character = new CharacterState(RuntimeEntityId.NewId());
            character.GrantFeature(feature.Id);
            registry.Register(character);

            var rule = new DefaultDamageResistanceRule(registry, rulesDatabase);

            Assert.AreEqual(5, rule.ApplyResistance(character.Id, Fire, 11)); // 11 / 2 rounded down
        }

        [Test]
        public void DoublesDamageWhenVulnerable()
        {
            var rulesDatabase = new RulesDatabase();
            var modifier = TestDefinitionFactory.CreateFeatureModifier(
                DefaultDamageResistanceRule.QueryKeyPrefix + Fire.Value, RuleModifierOperation.Vulnerability, 0f);
            var feature = TestDefinitionFactory.CreateFeatureDefinition("feature-fire-vulnerable", "Fire Vulnerable", modifier);
            rulesDatabase.Register(feature);

            var registry = new CharacterRegistry();
            var character = new CharacterState(RuntimeEntityId.NewId());
            character.GrantFeature(feature.Id);
            registry.Register(character);

            var rule = new DefaultDamageResistanceRule(registry, rulesDatabase);

            Assert.AreEqual(20, rule.ApplyResistance(character.Id, Fire, 10));
        }

        [Test]
        public void ZeroesDamageWhenImmuneEvenIfAlsoVulnerable()
        {
            var rulesDatabase = new RulesDatabase();
            var immunity = TestDefinitionFactory.CreateFeatureModifier(
                DefaultDamageResistanceRule.QueryKeyPrefix + Fire.Value, RuleModifierOperation.Immunity, 0f);
            var vulnerability = TestDefinitionFactory.CreateFeatureModifier(
                DefaultDamageResistanceRule.QueryKeyPrefix + Fire.Value, RuleModifierOperation.Vulnerability, 0f);
            var feature = TestDefinitionFactory.CreateFeatureDefinition("feature-fire-immune", "Fire Immune", immunity, vulnerability);
            rulesDatabase.Register(feature);

            var registry = new CharacterRegistry();
            var character = new CharacterState(RuntimeEntityId.NewId());
            character.GrantFeature(feature.Id);
            registry.Register(character);

            var rule = new DefaultDamageResistanceRule(registry, rulesDatabase);

            Assert.AreEqual(0, rule.ApplyResistance(character.Id, Fire, 999));
        }

        [Test]
        public void ResistanceAndVulnerabilityFromDifferentSourcesCancelOut()
        {
            var rulesDatabase = new RulesDatabase();
            var resistance = TestDefinitionFactory.CreateFeatureModifier(
                DefaultDamageResistanceRule.QueryKeyPrefix + Fire.Value, RuleModifierOperation.Resistance, 0f);
            var resistantFeature = TestDefinitionFactory.CreateFeatureDefinition("feature-fire-resistant", "Fire Resistant", resistance);
            rulesDatabase.Register(resistantFeature);

            var vulnerability = TestDefinitionFactory.CreateFeatureModifier(
                DefaultDamageResistanceRule.QueryKeyPrefix + Fire.Value, RuleModifierOperation.Vulnerability, 0f);
            var vulnerableCondition = TestDefinitionFactory.CreateConditionDefinition("condition-exposed", "Exposed", vulnerability);
            rulesDatabase.Register(vulnerableCondition);

            var registry = new CharacterRegistry();
            var character = new CharacterState(RuntimeEntityId.NewId());
            character.GrantFeature(resistantFeature.Id);
            character.ApplyCondition(new ConditionInstance(vulnerableCondition.Id, RuntimeEntityId.None, character.Id));
            registry.Register(character);

            var rule = new DefaultDamageResistanceRule(registry, rulesDatabase);

            Assert.AreEqual(10, rule.ApplyResistance(character.Id, Fire, 10)); // cancels out -- no change
        }

        [Test]
        public void ReturnsRawAmountForAnUnknownCharacter()
        {
            var rule = new DefaultDamageResistanceRule(new CharacterRegistry(), new RulesDatabase());

            Assert.AreEqual(10, rule.ApplyResistance(RuntimeEntityId.NewId(), Fire, 10));
        }
    }
}
