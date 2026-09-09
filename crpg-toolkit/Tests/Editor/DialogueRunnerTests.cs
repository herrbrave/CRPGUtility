using CRPG.Actors;
using CRPG.Core;
using CRPG.Dialogue;
using CRPG.Inventory;
using CRPG.Party;
using CRPG.Rules;
using NUnit.Framework;

namespace CRPG.Tests.EditMode
{
    public class DialogueRunnerTests
    {
        private RulesDatabase rulesDatabase;
        private CharacterRegistry characters;
        private PartyInventoryState inventory;
        private Timeline timeline;
        private PartyState party;
        private RuntimeEntityId npc;

        [SetUp]
        public void SetUp()
        {
            rulesDatabase = new RulesDatabase();
            characters = new CharacterRegistry();
            inventory = new PartyInventoryState();
            timeline = new Timeline();
            npc = RuntimeEntityId.NewId();

            var fighter = new CharacterState(RuntimeEntityId.NewId());
            characters.Register(fighter);
            party = new PartyState(new[] { fighter.Id });
        }

        private DialogueContext CreateContext(IDiceService diceService = null, StoreInventoryState store = null) =>
            new(npc, party, characters, rulesDatabase, timeline, inventory, diceService ?? new DiceService(new SeededRandomSource(1)), store: store);

        [Test]
        public void RunnerStartsAtTheDefinitionsStartNode()
        {
            var end = DialogueTestFactory.CreateEnd("end");
            var line = DialogueTestFactory.CreateLine("start", "Grimjaw", "Halt.", "end");
            var dialogue = DialogueTestFactory.CreateDialogue("dlg-test", "start", line, end);

            var runner = new DialogueRunner(dialogue, CreateContext());

            Assert.AreSame(line, runner.CurrentNode);
        }

        [Test]
        public void AdvanceMovesFromALineToItsNextNode()
        {
            var end = DialogueTestFactory.CreateEnd("end");
            var line = DialogueTestFactory.CreateLine("start", "Grimjaw", "Halt.", "end");
            var dialogue = DialogueTestFactory.CreateDialogue("dlg-test", "start", line, end);
            var runner = new DialogueRunner(dialogue, CreateContext());

            runner.Advance();

            Assert.IsTrue(runner.IsComplete);
        }

        [Test]
        public void AdvanceThrowsWhenCurrentNodeIsNotALine()
        {
            var end = DialogueTestFactory.CreateEnd("end");
            var choice = DialogueTestFactory.CreateChoice("start", DialogueTestFactory.CreateOption("Bye", "end"));
            var dialogue = DialogueTestFactory.CreateDialogue("dlg-test", "start", choice, end);
            var runner = new DialogueRunner(dialogue, CreateContext());

            Assert.Throws<System.InvalidOperationException>(() => runner.Advance());
        }

        [Test]
        public void ChooseOptionMovesToTheSelectedOptionsNextNode()
        {
            var endA = DialogueTestFactory.CreateEnd("end-a");
            var endB = DialogueTestFactory.CreateEnd("end-b");
            var choice = DialogueTestFactory.CreateChoice(
                "start",
                DialogueTestFactory.CreateOption("Fight", "end-a"),
                DialogueTestFactory.CreateOption("Flee", "end-b"));
            var dialogue = DialogueTestFactory.CreateDialogue("dlg-test", "start", choice, endA, endB);
            var runner = new DialogueRunner(dialogue, CreateContext());

            runner.ChooseOption(1);

            Assert.AreSame(endB, runner.CurrentNode);
        }

        [Test]
        public void BranchAutoSettlesBasedOnHasOccurred()
        {
            timeline.Record("met-grimjaw", TimelineValue.Flag);

            var trueEnd = DialogueTestFactory.CreateEnd("true-end");
            var falseEnd = DialogueTestFactory.CreateEnd("false-end");
            var branch = DialogueTestFactory.CreateBranch("start", "met-grimjaw", BranchConditionType.HasOccurred, 0, "true-end", "false-end");
            var dialogue = DialogueTestFactory.CreateDialogue("dlg-test", "start", branch, trueEnd, falseEnd);

            var runner = new DialogueRunner(dialogue, CreateContext());

            Assert.AreSame(trueEnd, runner.CurrentNode);
        }

        [Test]
        public void BranchAutoSettlesBasedOnSumAtLeast()
        {
            timeline.Record("guild-reputation", TimelineValue.OfNumber(3), amount: 3);
            timeline.Record("guild-reputation", TimelineValue.OfNumber(4), amount: 4);

            var trueEnd = DialogueTestFactory.CreateEnd("true-end");
            var falseEnd = DialogueTestFactory.CreateEnd("false-end");
            var branch = DialogueTestFactory.CreateBranch("start", "guild-reputation", BranchConditionType.SumAtLeast, 5, "true-end", "false-end");
            var dialogue = DialogueTestFactory.CreateDialogue("dlg-test", "start", branch, trueEnd, falseEnd);

            var runner = new DialogueRunner(dialogue, CreateContext());

            Assert.AreSame(trueEnd, runner.CurrentNode); // 3 + 4 = 7 >= 5
        }

        [Test]
        public void BranchAutoSettlesBasedOnOccurredBefore()
        {
            timeline.Record("betrayal", TimelineValue.Flag);
            timeline.Record("coronation", TimelineValue.Flag);

            var trueEnd = DialogueTestFactory.CreateEnd("true-end");
            var falseEnd = DialogueTestFactory.CreateEnd("false-end");
            var branch = DialogueTestFactory.CreateBranch(
                "start", "betrayal", BranchConditionType.OccurredBefore, 0, "true-end", "false-end", secondTimelineKey: "coronation");
            var dialogue = DialogueTestFactory.CreateDialogue("dlg-test", "start", branch, trueEnd, falseEnd);

            var runner = new DialogueRunner(dialogue, CreateContext());

            Assert.AreSame(trueEnd, runner.CurrentNode);
        }

        [Test]
        public void ActionNodeExecutesRecordEventThenAutoAdvances()
        {
            var end = DialogueTestFactory.CreateEnd("end");
            var recordAction = DialogueTestFactory.CreateRecordEventAction("agreed-to-help");
            var actionNode = DialogueTestFactory.CreateAction("start", recordAction, "end");
            var dialogue = DialogueTestFactory.CreateDialogue("dlg-test", "start", actionNode, end);

            var runner = new DialogueRunner(dialogue, CreateContext());

            Assert.IsTrue(runner.IsComplete);
            Assert.IsTrue(timeline.HasOccurred("agreed-to-help"));
        }

        [Test]
        public void ActionNodeExecutesGiveItemThenAutoAdvances()
        {
            var potion = TestDefinitionFactory.CreateItemDefinition("item-potion", "Healing Potion");
            rulesDatabase.Register(potion);

            var end = DialogueTestFactory.CreateEnd("end");
            var giveAction = DialogueTestFactory.CreateGiveItemAction(potion, quantity: 2);
            var actionNode = DialogueTestFactory.CreateAction("start", giveAction, "end");
            var dialogue = DialogueTestFactory.CreateDialogue("dlg-test", "start", actionNode, end);

            new DialogueRunner(dialogue, CreateContext());

            var given = System.Linq.Enumerable.Single(inventory.ItemsInSharedInventory);
            Assert.AreEqual(potion.Id, given.DefinitionId);
            Assert.AreEqual(2, given.Quantity);
        }

        [Test]
        public void PerformCheckSucceedsAndBranchesToSuccessNode()
        {
            var success = DialogueTestFactory.CreateEnd("success");
            var failure = DialogueTestFactory.CreateEnd("failure");
            var check = DialogueTestFactory.CreateCheck("start", "Skill:Persuasion", 15, CheckPerformer.ActiveCharacter, "success", "failure");
            var dialogue = DialogueTestFactory.CreateDialogue("dlg-test", "start", check, success, failure);

            party.Select(System.Linq.Enumerable.First(party.Members)); // ensure ActiveCharacter is set

            var random = new TestRandomSource();
            random.EnqueueResult(18);
            var runner = new DialogueRunner(dialogue, CreateContext(new DiceService(random)));

            var outcome = runner.PerformCheck();

            Assert.IsTrue(outcome.Success);
            Assert.AreEqual(18, outcome.Rolls[0].Total);
            Assert.AreSame(success, runner.CurrentNode);
        }

        [Test]
        public void PerformCheckFailsAndBranchesToFailureNode()
        {
            var success = DialogueTestFactory.CreateEnd("success");
            var failure = DialogueTestFactory.CreateEnd("failure");
            var check = DialogueTestFactory.CreateCheck("start", "Skill:Persuasion", 15, CheckPerformer.ActiveCharacter, "success", "failure");
            var dialogue = DialogueTestFactory.CreateDialogue("dlg-test", "start", check, success, failure);

            party.Select(System.Linq.Enumerable.First(party.Members));

            var random = new TestRandomSource();
            random.EnqueueResult(4);
            var runner = new DialogueRunner(dialogue, CreateContext(new DiceService(random)));

            var outcome = runner.PerformCheck();

            Assert.IsFalse(outcome.Success);
            Assert.AreSame(failure, runner.CurrentNode);
        }

        [Test]
        public void PerformCheckWithSpecificSpeakerRollsUsingTheNpcsOwnModifiers()
        {
            var silverTongue = TestDefinitionFactory.CreateFeatureModifier("Skill:Persuasion", RuleModifierOperation.Add, 10f);
            var feature = TestDefinitionFactory.CreateFeatureDefinition("feature-npc-silver-tongue", "Silver Tongue", silverTongue);
            rulesDatabase.Register(feature);

            var guard = new CharacterState(npc);
            guard.GrantFeature(feature.Id);
            characters.Register(guard);

            var success = DialogueTestFactory.CreateEnd("success");
            var failure = DialogueTestFactory.CreateEnd("failure");
            var check = DialogueTestFactory.CreateCheck("start", "Skill:Persuasion", 15, CheckPerformer.SpecificSpeaker, "success", "failure");
            var dialogue = DialogueTestFactory.CreateDialogue("dlg-test", "start", check, success, failure);

            var random = new TestRandomSource();
            random.EnqueueResult(8); // 8 + 10 modifier = 18, beats DC 15
            var runner = new DialogueRunner(dialogue, CreateContext(new DiceService(random)));

            var outcome = runner.PerformCheck();

            Assert.IsTrue(outcome.Success);
            Assert.AreEqual(18, outcome.Rolls[0].Total);
            Assert.AreSame(success, runner.CurrentNode);
        }

        [Test]
        public void PerformCheckWithEntirePartySucceedsWhenAtLeastHalfOfThePartySucceeds()
        {
            var second = new CharacterState(RuntimeEntityId.NewId());
            characters.Register(second);
            party = new PartyState(new[] { System.Linq.Enumerable.First(party.Members), second.Id });

            var success = DialogueTestFactory.CreateEnd("success");
            var failure = DialogueTestFactory.CreateEnd("failure");
            var check = DialogueTestFactory.CreateCheck("start", "Skill:Persuasion", 15, CheckPerformer.EntireParty, "success", "failure");
            var dialogue = DialogueTestFactory.CreateDialogue("dlg-test", "start", check, success, failure);

            var random = new TestRandomSource();
            random.EnqueueResult(18); // first member succeeds
            random.EnqueueResult(2);  // second member fails -- 1 of 2 still meets "at least half"
            var runner = new DialogueRunner(dialogue, CreateContext(new DiceService(random)));

            var outcome = runner.PerformCheck();

            Assert.IsTrue(outcome.Success);
            Assert.AreEqual(2, outcome.Rolls.Count);
            Assert.AreSame(success, runner.CurrentNode);
        }

        [Test]
        public void PerformCheckWithEntirePartyFailsWhenFewerThanHalfSucceed()
        {
            var second = new CharacterState(RuntimeEntityId.NewId());
            var third = new CharacterState(RuntimeEntityId.NewId());
            characters.Register(second);
            characters.Register(third);
            party = new PartyState(new[] { System.Linq.Enumerable.First(party.Members), second.Id, third.Id });

            var success = DialogueTestFactory.CreateEnd("success");
            var failure = DialogueTestFactory.CreateEnd("failure");
            var check = DialogueTestFactory.CreateCheck("start", "Skill:Persuasion", 15, CheckPerformer.EntireParty, "success", "failure");
            var dialogue = DialogueTestFactory.CreateDialogue("dlg-test", "start", check, success, failure);

            var random = new TestRandomSource();
            random.EnqueueResult(18); // succeeds
            random.EnqueueResult(2);  // fails
            random.EnqueueResult(3);  // fails -- only 1 of 3 succeeds
            var runner = new DialogueRunner(dialogue, CreateContext(new DiceService(random)));

            var outcome = runner.PerformCheck();

            Assert.IsFalse(outcome.Success);
            Assert.AreEqual(3, outcome.Rolls.Count);
            Assert.AreSame(failure, runner.CurrentNode);
        }

        [Test]
        public void GetVisibleOptionsExcludesAGatedOptionUntilItsConditionIsMet()
        {
            var endA = DialogueTestFactory.CreateEnd("end-a");
            var endB = DialogueTestFactory.CreateEnd("end-b");
            var openOption = DialogueTestFactory.CreateOption("Leave", "end-a");
            var gatedOption = DialogueTestFactory.CreateConditionalOption(
                "Mention the letter", "end-b", "read-letter", BranchConditionType.HasOccurred, 0);
            var choice = DialogueTestFactory.CreateChoice("start", openOption, gatedOption);
            var dialogue = DialogueTestFactory.CreateDialogue("dlg-test", "start", choice, endA, endB);
            var runner = new DialogueRunner(dialogue, CreateContext());

            var visibleBefore = runner.GetVisibleOptions();
            timeline.Record("read-letter", TimelineValue.Flag);
            var visibleAfter = runner.GetVisibleOptions();

            Assert.AreEqual(1, visibleBefore.Count);
            Assert.AreSame(openOption, visibleBefore[0]);
            Assert.AreEqual(2, visibleAfter.Count);
        }

        [Test]
        public void ChooseOptionThrowsWhenTheSelectedOptionIsGatedAndItsConditionIsUnmet()
        {
            var endA = DialogueTestFactory.CreateEnd("end-a");
            var gatedOption = DialogueTestFactory.CreateConditionalOption(
                "Mention the letter", "end-a", "read-letter", BranchConditionType.HasOccurred, 0);
            var choice = DialogueTestFactory.CreateChoice("start", gatedOption);
            var dialogue = DialogueTestFactory.CreateDialogue("dlg-test", "start", choice, endA);
            var runner = new DialogueRunner(dialogue, CreateContext());

            Assert.Throws<System.InvalidOperationException>(() => runner.ChooseOption(0));
        }

        [Test]
        public void OpenStoreActionRequestsTheContextsStoreThenAutoAdvances()
        {
            var shop = new StoreInventoryState();
            var end = DialogueTestFactory.CreateEnd("end");
            var actionNode = DialogueTestFactory.CreateAction("start", DialogueTestFactory.CreateOpenStoreAction(), "end");
            var dialogue = DialogueTestFactory.CreateDialogue("dlg-test", "start", actionNode, end);

            var context = CreateContext(store: shop);
            var runner = new DialogueRunner(dialogue, context);

            Assert.IsTrue(runner.IsComplete);
            Assert.IsTrue(context.StoreOpenRequested);
            Assert.AreSame(shop, context.Store);
        }

        [Test]
        public void SetStorePriceMultiplierActionChangesTheContextsStorePricesThenAutoAdvances()
        {
            var shop = new StoreInventoryState(buyPriceMultiplier: 1.5f, sellPriceMultiplier: 0.4f);
            var end = DialogueTestFactory.CreateEnd("end");
            var actionNode = DialogueTestFactory.CreateAction(
                "start", DialogueTestFactory.CreateSetStorePriceMultiplierAction(1f, 0.5f), "end");
            var dialogue = DialogueTestFactory.CreateDialogue("dlg-test", "start", actionNode, end);

            new DialogueRunner(dialogue, CreateContext(store: shop));

            Assert.AreEqual(1f, shop.BuyPriceMultiplier);
            Assert.AreEqual(0.5f, shop.SellPriceMultiplier);
        }

        [Test]
        public void SetStorePriceMultiplierActionIsANoOpWhenTheContextHasNoStore()
        {
            var end = DialogueTestFactory.CreateEnd("end");
            var actionNode = DialogueTestFactory.CreateAction(
                "start", DialogueTestFactory.CreateSetStorePriceMultiplierAction(1f, 0.5f), "end");
            var dialogue = DialogueTestFactory.CreateDialogue("dlg-test", "start", actionNode, end);

            Assert.DoesNotThrow(() => new DialogueRunner(dialogue, CreateContext()));
        }

        [Test]
        public void PerformCheckThrowsWhenCurrentNodeIsNotACheck()
        {
            var end = DialogueTestFactory.CreateEnd("end");
            var line = DialogueTestFactory.CreateLine("start", "Grimjaw", "Halt.", "end");
            var dialogue = DialogueTestFactory.CreateDialogue("dlg-test", "start", line, end);
            var runner = new DialogueRunner(dialogue, CreateContext());

            Assert.Throws<System.InvalidOperationException>(() => runner.PerformCheck());
        }

        [Test]
        public void FullConversationLineChoiceCheckAndRecordEventFlowTogether()
        {
            // "Let us pass." -> choice to bribe or persuade -> persuade is a DC 15 check ->
            // success records an event and ends; failure just ends.
            var end = DialogueTestFactory.CreateEnd("end");
            var recordSuccess = DialogueTestFactory.CreateAction(
                "record-success", DialogueTestFactory.CreateRecordEventAction("persuaded-guard"), "end");
            var check = DialogueTestFactory.CreateCheck("persuade-check", "Skill:Persuasion", 15, CheckPerformer.ActiveCharacter, "record-success", "end");
            var choice = DialogueTestFactory.CreateChoice(
                "choice",
                DialogueTestFactory.CreateOption("Persuade", "persuade-check"),
                DialogueTestFactory.CreateOption("Leave", "end"));
            var opening = DialogueTestFactory.CreateLine("start", "Guard", "Let us pass.", "choice");
            var dialogue = DialogueTestFactory.CreateDialogue("dlg-test", "start", opening, choice, check, recordSuccess, end);

            party.Select(System.Linq.Enumerable.First(party.Members));
            var random = new TestRandomSource();
            random.EnqueueResult(20);
            var runner = new DialogueRunner(dialogue, CreateContext(new DiceService(random)));

            runner.Advance(); // opening line -> choice
            runner.ChooseOption(0); // Persuade -> check
            runner.PerformCheck(); // rolls 20, succeeds -> record-success -> auto-settles to end

            Assert.IsTrue(runner.IsComplete);
            Assert.IsTrue(timeline.HasOccurred("persuaded-guard"));
        }
    }
}
