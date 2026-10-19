using System.Linq;
using CRPG.Actors;
using CRPG.Core;
using CRPG.Rules;
using CRPG.Rules.SRD5;
using NUnit.Framework;
using UnityEngine;

namespace CRPG.Tests.EditMode
{
    /// <summary>The skill/ability check rule that actually adds the ability modifier and the proficiency bonus (the default rule adds only tagged feature modifiers), keyed by the existing "Skill:Name" / "Ability:Name" strings.</summary>
    public class SkillAwareCheckRuleTests
    {
        private RulesDatabase rules;
        private CharacterRegistry registry;
        private StatDefinition dex, cha, str;
        private SkillDefinition sleight, athletics;

        private static SkillDefinition Skill(string id, string name, StatDefinition stat)
        {
            var skill = ScriptableObject.CreateInstance<SkillDefinition>();
            TestDefinitionFactory.SetPrivateField(skill, "id", id);
            TestDefinitionFactory.SetPrivateField(skill, "displayName", name);
            TestDefinitionFactory.SetPrivateField(skill, "governingStat", stat);
            return skill;
        }

        [SetUp]
        public void SetUp()
        {
            rules = new RulesDatabase(); registry = new CharacterRegistry();
            dex = TestDefinitionFactory.CreateStatDefinition("stat-dexterity", "Dexterity");
            cha = TestDefinitionFactory.CreateStatDefinition("stat-charisma", "Charisma");
            str = TestDefinitionFactory.CreateStatDefinition("stat-strength", "Strength");
            sleight = Skill("skill-sleight-of-hand", "Sleight of Hand", dex);
            athletics = Skill("skill-athletics", "Athletics", str);
            foreach (var d in new IDefinition[] { dex, cha, str, sleight, athletics }) rules.Register(d);
        }

        private CharacterState Character(int level = 1, int dexScore = 16, int chaScore = 16, int strScore = 8)
        {
            var c = new CharacterState(RuntimeEntityId.NewId()) { Level = level };
            c.Stats.SetScore(dex.Id, dexScore); c.Stats.SetScore(cha.Id, chaScore); c.Stats.SetScore(str.Id, strScore);
            registry.Register(c);
            return c;
        }

        private SkillAwareCheckRule Rule() => new(registry, rules);

        [Test]
        public void AProficientSkillAddsTheAbilityModifierAndTheProficiencyBonus()
        {
            var c = Character(); c.GrantProficiency(sleight.Id);
            Assert.AreEqual(3 + 2, Rule().ComputeSkillCheckBonus(c.Id, "Skill:SleightOfHand"));
        }

        [Test]
        public void ANonProficientSkillAddsOnlyTheAbilityModifier()
        {
            var c = Character();
            Assert.AreEqual(3, Rule().ComputeSkillCheckBonus(c.Id, "Skill:SleightOfHand"));
            Assert.AreEqual(-1, Rule().ComputeSkillCheckBonus(c.Id, "Skill:Athletics"), "Str 8 is -1");
        }

        [Test]
        public void TheProficiencyBonusGrowsWithLevel()
        {
            var c = Character(level: 5); c.GrantProficiency(sleight.Id);
            Assert.AreEqual(3 + 3, Rule().ComputeSkillCheckBonus(c.Id, "Skill:SleightOfHand"));
        }

        [Test]
        public void AnAbilityCheckNeverAddsProficiencyEvenWhenTheSaveIsProficient()
        {
            var c = Character(); c.GrantProficiency(cha.Id);   // the SAVE proficiency lives in the same bag as stat-charisma
            Assert.AreEqual(3, Rule().ComputeSkillCheckBonus(c.Id, "Ability:Charisma"));
        }

        [Test]
        public void TheKeyIsMatchedIgnoringCaseSpacesAndHyphens()
        {
            var c = Character(); c.GrantProficiency(sleight.Id);
            foreach (var key in new[] { "Skill:SleightOfHand", "Skill:sleight of hand", "Skill:Sleight-Of-Hand", "skill:SLEIGHT_OF_HAND", "Skill:sleight-of-hand" })
                Assert.AreEqual(5, Rule().ComputeSkillCheckBonus(c.Id, key), key);
        }

        [Test]
        public void GatheredFeatureModifiersAreAddedOnTop()
        {
            var bonus = TestDefinitionFactory.CreateFeatureModifier("Skill:SleightOfHand", RuleModifierOperation.Add, 2f);
            var feature = TestDefinitionFactory.CreateFeatureDefinition("feature-nimble", "Nimble", bonus); rules.Register(feature);
            var c = Character(); c.GrantProficiency(sleight.Id); c.GrantFeature(feature.Id);
            Assert.AreEqual(3 + 2 + 2, Rule().ComputeSkillCheckBonus(c.Id, "Skill:SleightOfHand"));
        }

        [Test]
        public void TheBreakdownNamesEveryPartAndSumsToTheBonus()
        {
            var bonus = TestDefinitionFactory.CreateFeatureModifier("Skill:SleightOfHand", RuleModifierOperation.Add, 1f);
            var feature = TestDefinitionFactory.CreateFeatureDefinition("feature-nimble", "Nimble", bonus); rules.Register(feature);
            var c = Character(); c.GrantProficiency(sleight.Id); c.GrantFeature(feature.Id);
            var rule = Rule();
            var parts = rule.ComputeBreakdown(c.Id, "Skill:SleightOfHand");
            Assert.AreEqual(rule.ComputeSkillCheckBonus(c.Id, "Skill:SleightOfHand"), parts.Sum(p => p.Value));
            Assert.IsTrue(parts.All(p => !string.IsNullOrWhiteSpace(p.Label)));
            StringAssert.Contains("Dexterity", parts[0].Label);
            Assert.IsTrue(parts.Any(p => p.Label.Contains("proficien")), "the proficiency part is named");
            Assert.IsTrue(parts.Any(p => p.Label.Contains("Nimble")), "a feature part is named by its source");
        }

        [Test]
        public void ANonProficientBreakdownHasNoProficiencyPart()
        {
            var c = Character();
            Assert.IsFalse(Rule().ComputeBreakdown(c.Id, "Skill:SleightOfHand").Any(p => p.Label.Contains("proficien")));
        }

        [Test]
        public void AnUnknownSkillOrMalformedKeyIsZeroAndDoesNotThrow()
        {
            var c = Character();
            var rule = Rule();
            Assert.AreEqual(0, rule.ComputeSkillCheckBonus(c.Id, "Skill:Basketweaving"));
            Assert.AreEqual(0, rule.ComputeSkillCheckBonus(c.Id, "Persuasion"));
            Assert.AreEqual(0, rule.ComputeSkillCheckBonus(c.Id, ""));
            Assert.AreEqual(0, rule.ComputeSkillCheckBonus(RuntimeEntityId.NewId(), "Skill:Athletics"), "an unknown character");
        }

        [Test]
        public void AdvantageComesFromGatheredModifiersLikeTheDefaultRule()
        {
            var adv = TestDefinitionFactory.CreateFeatureModifier("Skill:Athletics", RuleModifierOperation.Advantage, 0f);
            var feature = TestDefinitionFactory.CreateFeatureDefinition("feature-strong", "Strong", adv); rules.Register(feature);
            var c = Character(); c.GrantFeature(feature.Id);
            Assert.AreEqual(AdvantageState.Advantage, Rule().ComputeAdvantage(c.Id, "Skill:Athletics"));
            Assert.AreEqual(AdvantageState.Normal, Rule().ComputeAdvantage(c.Id, "Skill:SleightOfHand"));
        }

        [Test]
        public void TheCheckDieIsAD20()
        {
            Assert.AreEqual(new DiceExpression(1, 20), Rule().CheckDie);
        }
    }
}
