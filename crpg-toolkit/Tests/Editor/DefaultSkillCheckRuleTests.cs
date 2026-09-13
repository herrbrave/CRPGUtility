using CRPG.Actors;
using CRPG.Core;
using CRPG.Rules;
using CRPG.Rules.SRD5;
using NUnit.Framework;

namespace CRPG.Tests.EditMode
{
    public class DefaultSkillCheckRuleTests
    {
        [Test]
        public void ComputesOnlyGatheredModifiersWithNoAbilityOrProficiencyBase()
        {
            var rulesDatabase = new RulesDatabase();
            var persuasionBonus = TestDefinitionFactory.CreateFeatureModifier("Skill:Persuasion", RuleModifierOperation.Add, 3f);
            var feature = TestDefinitionFactory.CreateFeatureDefinition("feature-silver-tongued", "Silver-Tongued", persuasionBonus);
            rulesDatabase.Register(feature);

            var registry = new CharacterRegistry();
            var character = new CharacterState(RuntimeEntityId.NewId());
            character.GrantFeature(feature.Id);
            registry.Register(character);

            var rule = new DefaultSkillCheckRule(registry, rulesDatabase);

            Assert.AreEqual(3, rule.ComputeSkillCheckBonus(character.Id, "Skill:Persuasion"));
        }

        [Test]
        public void IgnoresModifiersTaggedForADifferentSkill()
        {
            var rulesDatabase = new RulesDatabase();
            var stealthBonus = TestDefinitionFactory.CreateFeatureModifier("Skill:Stealth", RuleModifierOperation.Add, 5f);
            var feature = TestDefinitionFactory.CreateFeatureDefinition("feature-sneaky", "Sneaky", stealthBonus);
            rulesDatabase.Register(feature);

            var registry = new CharacterRegistry();
            var character = new CharacterState(RuntimeEntityId.NewId());
            character.GrantFeature(feature.Id);
            registry.Register(character);

            var rule = new DefaultSkillCheckRule(registry, rulesDatabase);

            Assert.AreEqual(0, rule.ComputeSkillCheckBonus(character.Id, "Skill:Persuasion"));
        }

        [Test]
        public void ComputeAdvantageGathersModifiersUnderTheSameKey()
        {
            var rulesDatabase = new RulesDatabase();
            var guidance = TestDefinitionFactory.CreateFeatureModifier("Skill:Perception", RuleModifierOperation.Advantage, 0f);
            var feature = TestDefinitionFactory.CreateFeatureDefinition("feature-guidance", "Guidance", guidance);
            rulesDatabase.Register(feature);

            var registry = new CharacterRegistry();
            var character = new CharacterState(RuntimeEntityId.NewId());
            character.GrantFeature(feature.Id);
            registry.Register(character);

            var rule = new DefaultSkillCheckRule(registry, rulesDatabase);

            Assert.AreEqual(AdvantageState.Advantage, rule.ComputeAdvantage(character.Id, "Skill:Perception"));
        }

        [Test]
        public void ReturnsZeroBonusAndNormalAdvantageForAnUnknownCharacter()
        {
            var rule = new DefaultSkillCheckRule(new CharacterRegistry(), new RulesDatabase());
            var unknown = RuntimeEntityId.NewId();

            Assert.AreEqual(0, rule.ComputeSkillCheckBonus(unknown, "Skill:Persuasion"));
            Assert.AreEqual(AdvantageState.Normal, rule.ComputeAdvantage(unknown, "Skill:Persuasion"));
        }
    }
}
