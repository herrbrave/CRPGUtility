using CRPG.Actors;
using CRPG.Combat;
using CRPG.Core;
using CRPG.Rules;
using CRPG.Rules.SRD5;
using NUnit.Framework;

namespace CRPG.Tests.EditMode
{
    public class DefaultSavingThrowRuleTests
    {
        private static readonly DefinitionId Dexterity = new("stat-dexterity");

        [Test]
        public void ComputesAbilityModifierOnlyWhenNotProficient()
        {
            var registry = new CharacterRegistry();
            var character = new CharacterState(RuntimeEntityId.NewId());
            character.Stats.SetScore(Dexterity, 14); // +2 modifier
            character.Level = 5; // would be +3 proficiency bonus, but not proficient
            registry.Register(character);

            var rule = new DefaultSavingThrowRule(registry, new RulesDatabase());

            Assert.AreEqual(2, rule.ComputeSavingThrowBonus(character.Id, Dexterity));
        }

        [Test]
        public void AddsProficiencyBonusWhenProficientInThisSavingThrow()
        {
            var registry = new CharacterRegistry();
            var character = new CharacterState(RuntimeEntityId.NewId());
            character.Stats.SetScore(Dexterity, 14); // +2 modifier
            character.Level = 5; // +3 proficiency bonus
            character.GrantProficiency(Dexterity);
            registry.Register(character);

            var rule = new DefaultSavingThrowRule(registry, new RulesDatabase());

            Assert.AreEqual(5, rule.ComputeSavingThrowBonus(character.Id, Dexterity)); // 2 + 3
        }

        [Test]
        public void AddsGatheredSavingThrowModifiersOnTopOfAbilityAndProficiency()
        {
            var rulesDatabase = new RulesDatabase();
            var cloakBonus = TestDefinitionFactory.CreateFeatureModifier(
                DefaultSavingThrowRule.QueryKeyPrefix + Dexterity.Value, RuleModifierOperation.Add, 1f);
            var feature = TestDefinitionFactory.CreateFeatureDefinition("feature-cloak-of-protection", "Cloak of Protection", cloakBonus);
            rulesDatabase.Register(feature);

            var registry = new CharacterRegistry();
            var character = new CharacterState(RuntimeEntityId.NewId());
            character.Stats.SetScore(Dexterity, 12); // +1 modifier
            character.GrantFeature(feature.Id);
            registry.Register(character);

            var rule = new DefaultSavingThrowRule(registry, rulesDatabase);

            Assert.AreEqual(2, rule.ComputeSavingThrowBonus(character.Id, Dexterity)); // 1 + 0 (not proficient) + 1
        }

        [Test]
        public void ComputeAdvantageGathersModifiersUnderTheSavingThrowKey()
        {
            var rulesDatabase = new RulesDatabase();
            var restrainedModifier = TestDefinitionFactory.CreateFeatureModifier(
                DefaultSavingThrowRule.QueryKeyPrefix + Dexterity.Value, RuleModifierOperation.Disadvantage, 0f);
            var condition = TestDefinitionFactory.CreateConditionDefinition("condition-restrained", "Restrained", restrainedModifier);
            rulesDatabase.Register(condition);

            var registry = new CharacterRegistry();
            var character = new CharacterState(RuntimeEntityId.NewId());
            character.ApplyCondition(new ConditionInstance(condition.Id, RuntimeEntityId.None, character.Id));
            registry.Register(character);

            var rule = new DefaultSavingThrowRule(registry, rulesDatabase);

            Assert.AreEqual(AdvantageState.Disadvantage, rule.ComputeAdvantage(character.Id, Dexterity));
        }

        [Test]
        public void ReturnsZeroBonusAndNormalAdvantageForAnUnknownCharacter()
        {
            var rule = new DefaultSavingThrowRule(new CharacterRegistry(), new RulesDatabase());
            var unknown = RuntimeEntityId.NewId();

            Assert.AreEqual(0, rule.ComputeSavingThrowBonus(unknown, Dexterity));
            Assert.AreEqual(AdvantageState.Normal, rule.ComputeAdvantage(unknown, Dexterity));
        }

        [Test]
        public void ComputeSpellSaveDCIsEightPlusAbilityModifierPlusProficiencyBonus()
        {
            var registry = new CharacterRegistry();
            var caster = new CharacterState(RuntimeEntityId.NewId());
            caster.Stats.SetScore(Dexterity, 16); // +3 modifier
            caster.Level = 5; // +3 proficiency bonus
            registry.Register(caster);

            var rule = new DefaultSavingThrowRule(registry, new RulesDatabase());

            Assert.AreEqual(14, rule.ComputeSpellSaveDC(caster.Id, Dexterity)); // 8 + 3 + 3
        }

        [Test]
        public void ComputeSpellSaveDCAddsGatheredModifiersUnderTheSpellSaveDCKey()
        {
            var rulesDatabase = new RulesDatabase();
            var warCasterBonus = TestDefinitionFactory.CreateFeatureModifier(
                DefaultSavingThrowRule.SpellSaveDCQueryKey, RuleModifierOperation.Add, 1f);
            var feature = TestDefinitionFactory.CreateFeatureDefinition("feature-spell-focus", "Spell Focus", warCasterBonus);
            rulesDatabase.Register(feature);

            var registry = new CharacterRegistry();
            var caster = new CharacterState(RuntimeEntityId.NewId());
            caster.Stats.SetScore(Dexterity, 10); // +0 modifier
            caster.GrantFeature(feature.Id);
            registry.Register(caster);

            var rule = new DefaultSavingThrowRule(registry, rulesDatabase);

            Assert.AreEqual(11, rule.ComputeSpellSaveDC(caster.Id, Dexterity)); // 8 + 0 + 2 (level-1 proficiency bonus) + 1
        }

        [Test]
        public void AutoFailsWhenAnAutoFailSaveModifierIsGathered()
        {
            var rulesDatabase = new RulesDatabase();
            var paralysis = TestDefinitionFactory.CreateFeatureModifier(
                DefaultSavingThrowRule.QueryKeyPrefix + Dexterity.Value, RuleModifierOperation.AutoFailSave, 0f);
            var condition = TestDefinitionFactory.CreateConditionDefinition("condition-paralyzed", "Paralyzed", paralysis);
            rulesDatabase.Register(condition);

            var registry = new CharacterRegistry();
            var character = new CharacterState(RuntimeEntityId.NewId());
            character.ApplyCondition(new ConditionInstance(condition.Id, RuntimeEntityId.None, character.Id));
            registry.Register(character);

            var rule = new DefaultSavingThrowRule(registry, rulesDatabase);

            Assert.IsTrue(rule.AutoFails(character.Id, Dexterity));
            Assert.IsFalse(rule.AutoFails(character.Id, new DefinitionId("stat-wisdom"))); // different save, unaffected
        }

        [Test]
        public void ComputeSpellSaveDCReturnsZeroForAnUnknownCharacter()
        {
            var rule = new DefaultSavingThrowRule(new CharacterRegistry(), new RulesDatabase());

            Assert.AreEqual(0, rule.ComputeSpellSaveDC(RuntimeEntityId.NewId(), Dexterity));
        }
    }
}
