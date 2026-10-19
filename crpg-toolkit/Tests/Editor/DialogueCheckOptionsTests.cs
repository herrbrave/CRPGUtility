using System.Linq;
using CRPG.Actors;
using CRPG.Core;
using CRPG.Dialogue;
using CRPG.Inventory;
using CRPG.Party;
using CRPG.Rules;
using CRPG.Rules.SRD5;
using CRPG.UI;
using NUnit.Framework;
using UnityEngine;

namespace CRPG.Tests.EditMode
{
    /// <summary>A check that can be looked at before it is rolled (the prompt) and rolled with extras the player chose (flat bonuses, advantage): the seam a game's dice popup plugs into. Passing no options is exactly the old behaviour.</summary>
    public class DialogueCheckOptionsTests
    {
        private RulesDatabase rules; private CharacterRegistry characters; private Timeline timeline; private PartyState party;
        private RuntimeEntityId npc; private CharacterState hero;

        [SetUp]
        public void SetUp()
        {
            rules = new RulesDatabase(); characters = new CharacterRegistry(); timeline = new Timeline();
            var dex = TestDefinitionFactory.CreateStatDefinition("stat-dexterity", "Dexterity");
            var skill = ScriptableObject.CreateInstance<SkillDefinition>();
            TestDefinitionFactory.SetPrivateField(skill, "id", "skill-sleight-of-hand"); TestDefinitionFactory.SetPrivateField(skill, "displayName", "Sleight of Hand");
            TestDefinitionFactory.SetPrivateField(skill, "governingStat", dex);
            rules.Register(dex); rules.Register(skill);
            hero = new CharacterState(RuntimeEntityId.NewId()); hero.Stats.SetScore(dex.Id, 14); hero.GrantProficiency(skill.Id);   // +2 +2 = +4
            characters.Register(hero); party = new PartyState(new[] { hero.Id }); party.Select(hero.Id);
            npc = RuntimeEntityId.NewId();
        }

        private DialogueRunner Runner(TestRandomSource random, ISkillCheckRule rule, ICombatLog log = null, CheckPerformer performer = CheckPerformer.ActiveCharacter)
        {
            var check = DialogueTestFactory.CreateCheck("start", "Skill:SleightOfHand", 12, performer, "success", "failure");
            var dialogue = DialogueTestFactory.CreateDialogue("dlg", "start", check, DialogueTestFactory.CreateEnd("success"), DialogueTestFactory.CreateEnd("failure"));
            var context = new DialogueContext(npc, party, characters, rules, timeline, new PartyInventoryState(), new DiceService(random), rule, log);
            return new DialogueRunner(dialogue, context);
        }

        [Test]
        public void PreparingACheckRollsNothingAndStaysOnTheCheck()
        {
            var random = new TestRandomSource();   // nothing queued: any roll would throw
            var runner = Runner(random, new SkillAwareCheckRule(characters, rules));
            var prompt = runner.PrepareCheck();
            Assert.IsInstanceOf<CheckNode>(runner.CurrentNode);
            Assert.AreEqual("Skill:SleightOfHand", prompt.QueryKey); Assert.AreEqual(12, prompt.DifficultyClass);
            Assert.AreEqual(1, prompt.Checkers.Count);
            var checker = prompt.Checkers[0];
            Assert.AreEqual(hero.Id, checker.Id); Assert.AreEqual(4, checker.Bonus); Assert.AreEqual(AdvantageState.Normal, checker.Advantage);
            Assert.AreEqual(new DiceExpression(1, 20), checker.Die);
            Assert.AreEqual(4, checker.Breakdown.Sum(p => p.Value)); Assert.GreaterOrEqual(checker.Breakdown.Count, 2, "the detailed rule names the parts");
        }

        [Test]
        public void ARuleWithoutADetailedBreakdownGivesOneBonusPart()
        {
            var runner = Runner(new TestRandomSource(), new DefaultSkillCheckRule(characters, rules));
            var checker = runner.PrepareCheck().Checkers[0];
            Assert.AreEqual(1, checker.Breakdown.Count); Assert.AreEqual(checker.Bonus, checker.Breakdown[0].Value);
        }

        [Test]
        public void TheSpecificSpeakerIsTheChecker()
        {
            var other = new CharacterState(RuntimeEntityId.NewId()); characters.Register(other); npc = other.Id;
            var runner = Runner(new TestRandomSource(), new SkillAwareCheckRule(characters, rules), performer: CheckPerformer.SpecificSpeaker);
            Assert.AreEqual(other.Id, runner.PrepareCheck().Checkers[0].Id);
        }

        [Test]
        public void ExtraModifiersJoinTheSameRollAndTheLog()
        {
            var random = new TestRandomSource(); random.EnqueueResult(9);   // 9 + 4 = 13 passes DC 12; without the extra +3 it would be 13 anyway, so use DC to tell: see below
            var log = new CombatLog();
            var runner = Runner(random, new SkillAwareCheckRule(characters, rules), log);
            var outcome = runner.PerformCheck(new CheckRollOptions(new[] { new RollModifier("Guidance", 3) }));
            Assert.AreEqual(9 + 4 + 3, outcome.Rolls[0].Total);
            Assert.IsTrue(outcome.Rolls[0].Request.Modifiers.Any(m => m.Source == "Guidance" && m.Value == 3));
            Assert.AreEqual(1, log.Entries.Count); Assert.IsInstanceOf<RollLogEntry>(log.Entries[0]);
        }

        [Test]
        public void AnExtraBonusCanTurnAFailureIntoASuccess()
        {
            var random = new TestRandomSource(); random.EnqueueResult(6);   // 6 + 4 = 10 < 12; with Guidance +3 = 13
            var runner = Runner(random, new SkillAwareCheckRule(characters, rules));
            Assert.IsTrue(runner.PerformCheck(new CheckRollOptions(new[] { new RollModifier("Guidance", 3) })).Success);
            var random2 = new TestRandomSource(); random2.EnqueueResult(6);
            Assert.IsFalse(Runner(random2, new SkillAwareCheckRule(characters, rules)).PerformCheck().Success, "without it the same roll fails");
        }

        [Test]
        public void ExtraAdvantageRollsTwoDiceAndKeepsTheBetter()
        {
            var random = new TestRandomSource(); random.EnqueueResults(3, 17);
            var runner = Runner(random, new SkillAwareCheckRule(characters, rules));
            var outcome = runner.PerformCheck(new CheckRollOptions(extraAdvantage: AdvantageState.Advantage));
            Assert.AreEqual(2, outcome.Rolls[0].Dice.Count); Assert.AreEqual(17 + 4, outcome.Rolls[0].Total);
        }

        [Test]
        public void ExtraAdvantageCancelsADisadvantageFromTheRule()
        {
            var dis = TestDefinitionFactory.CreateFeatureModifier("Skill:SleightOfHand", RuleModifierOperation.Disadvantage, 0f);
            var feature = TestDefinitionFactory.CreateFeatureDefinition("feature-shackled", "Shackled", dis); rules.Register(feature); hero.GrantFeature(feature.Id);
            var random = new TestRandomSource(); random.EnqueueResult(10);
            var runner = Runner(random, new SkillAwareCheckRule(characters, rules));
            Assert.AreEqual(AdvantageState.Disadvantage, runner.PrepareCheck().Checkers[0].Advantage);
            var outcome = runner.PerformCheck(new CheckRollOptions(extraAdvantage: AdvantageState.Advantage));
            Assert.AreEqual(1, outcome.Rolls[0].Dice.Count, "advantage and disadvantage cancel: one die");
        }

        [Test]
        public void NoOptionsIsExactlyTheOldBehaviour()
        {
            var random = new TestRandomSource(); random.EnqueueResult(10);
            var runner = Runner(random, new SkillAwareCheckRule(characters, rules));
            var outcome = runner.PerformCheck();
            Assert.AreEqual(14, outcome.Rolls[0].Total); Assert.IsTrue(outcome.Success);
            Assert.IsTrue(runner.IsComplete);
        }

        [Test]
        public void ThePresenterForwardsBothSteps()
        {
            var random = new TestRandomSource(); random.EnqueueResult(10);
            var check = DialogueTestFactory.CreateCheck("start", "Skill:SleightOfHand", 12, CheckPerformer.ActiveCharacter, "success", "failure");
            var dialogue = DialogueTestFactory.CreateDialogue("dlg", "start", check, DialogueTestFactory.CreateEnd("success"), DialogueTestFactory.CreateEnd("failure"));
            var context = new DialogueContext(npc, party, characters, rules, timeline, new PartyInventoryState(), new DiceService(random), new SkillAwareCheckRule(characters, rules));
            var presenter = new DialoguePresenter(dialogue, context);
            Assert.IsTrue(presenter.IsAtCheck);
            Assert.AreEqual(12, presenter.PrepareCheck().DifficultyClass);
            var outcome = presenter.PerformCheck(new CheckRollOptions(new[] { new RollModifier("Guidance", 2) }));
            Assert.AreEqual(16, outcome.Rolls[0].Total); Assert.IsTrue(presenter.IsComplete);
        }

        [Test]
        public void PreparingOffACheckNodeThrows()
        {
            var line = DialogueTestFactory.CreateLine("start", "X", "hi", "end");
            var dialogue = DialogueTestFactory.CreateDialogue("dlg", "start", line, DialogueTestFactory.CreateEnd("end"));
            var context = new DialogueContext(npc, party, characters, rules, timeline, new PartyInventoryState(), new DiceService(new TestRandomSource()), new SkillAwareCheckRule(characters, rules));
            Assert.Throws<System.InvalidOperationException>(() => new DialogueRunner(dialogue, context).PrepareCheck());
        }
    }
}
