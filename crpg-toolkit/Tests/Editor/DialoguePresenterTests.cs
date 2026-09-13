using CRPG.Actors;
using CRPG.Core;
using CRPG.Dialogue;
using CRPG.Inventory;
using CRPG.Party;
using CRPG.UI;
using NUnit.Framework;

namespace CRPG.Tests.EditMode
{
    public class DialoguePresenterTests
    {
        private DialogueContext CreateContext(IDiceService diceService = null)
        {
            var rulesDatabase = new RulesDatabase();
            var characters = new CharacterRegistry();
            var fighter = new CharacterState(RuntimeEntityId.NewId());
            characters.Register(fighter);
            var party = new PartyState(new[] { fighter.Id });
            party.Select(fighter.Id);

            return new DialogueContext(
                RuntimeEntityId.NewId(), party, characters, rulesDatabase, new Timeline(), new PartyInventoryState(),
                diceService ?? new DiceService(new SeededRandomSource(1)));
        }

        [Test]
        public void OnALineNodeExposesSpeakerAndText()
        {
            var end = DialogueTestFactory.CreateEnd("end");
            var line = DialogueTestFactory.CreateLine("start", "Eldor", "Greetings, traveler.", "end");
            var dialogue = DialogueTestFactory.CreateDialogue("dlg", "start", line, end);

            var presenter = new DialoguePresenter(dialogue, CreateContext());

            Assert.IsTrue(presenter.IsAtLine);
            Assert.AreEqual("Eldor", presenter.SpeakerName);
            Assert.AreEqual("Greetings, traveler.", presenter.LineText);
        }

        [Test]
        public void AdvanceMovesPastALine()
        {
            var end = DialogueTestFactory.CreateEnd("end");
            var line = DialogueTestFactory.CreateLine("start", "Eldor", "Greetings.", "end");
            var dialogue = DialogueTestFactory.CreateDialogue("dlg", "start", line, end);
            var presenter = new DialoguePresenter(dialogue, CreateContext());

            presenter.Advance();

            Assert.IsTrue(presenter.IsComplete);
        }

        [Test]
        public void GetChoicesOnlyReturnsCurrentlyVisibleOptionsWithTheirOriginalIndex()
        {
            var endA = DialogueTestFactory.CreateEnd("end-a");
            var endB = DialogueTestFactory.CreateEnd("end-b");
            var openOption = DialogueTestFactory.CreateOption("Leave", "end-a");
            var gatedOption = DialogueTestFactory.CreateConditionalOption(
                "Mention the letter", "end-b", "read-letter", TimelineConditionType.HasOccurred, 0);
            var choice = DialogueTestFactory.CreateChoice("start", gatedOption, openOption); // gated option is index 0
            var dialogue = DialogueTestFactory.CreateDialogue("dlg", "start", choice, endA, endB);
            var presenter = new DialoguePresenter(dialogue, CreateContext());

            var choices = presenter.GetChoices();

            Assert.AreEqual(1, choices.Count);
            Assert.AreEqual(1, choices[0].OptionIndex); // "Leave" is at index 1 in the full Options list
            Assert.AreEqual("Leave", choices[0].Text);
        }

        [Test]
        public void ChooseOptionUsesTheOriginalIndexFromGetChoices()
        {
            var endA = DialogueTestFactory.CreateEnd("end-a");
            var endB = DialogueTestFactory.CreateEnd("end-b");
            var choice = DialogueTestFactory.CreateChoice(
                "start", DialogueTestFactory.CreateOption("Fight", "end-a"), DialogueTestFactory.CreateOption("Flee", "end-b"));
            var dialogue = DialogueTestFactory.CreateDialogue("dlg", "start", choice, endA, endB);
            var presenter = new DialoguePresenter(dialogue, CreateContext());

            presenter.ChooseOption(1);

            Assert.IsTrue(presenter.IsComplete);
        }

        [Test]
        public void OnACheckNodeExposesTheQueryKeyAndDifficultyClass()
        {
            var success = DialogueTestFactory.CreateEnd("success");
            var failure = DialogueTestFactory.CreateEnd("failure");
            var check = DialogueTestFactory.CreateCheck("start", "Skill:Persuasion", 15, CheckPerformer.ActiveCharacter, "success", "failure");
            var dialogue = DialogueTestFactory.CreateDialogue("dlg", "start", check, success, failure);
            var presenter = new DialoguePresenter(dialogue, CreateContext());

            Assert.IsTrue(presenter.IsAtCheck);
            Assert.AreEqual("Skill:Persuasion", presenter.CheckLabel);
            Assert.AreEqual(15, presenter.CheckDifficultyClass);
        }

        [Test]
        public void PerformCheckAdvancesToTheOutcomeNodeAndReturnsTheRoll()
        {
            var success = DialogueTestFactory.CreateEnd("success");
            var failure = DialogueTestFactory.CreateEnd("failure");
            var check = DialogueTestFactory.CreateCheck("start", "Skill:Persuasion", 15, CheckPerformer.ActiveCharacter, "success", "failure");
            var dialogue = DialogueTestFactory.CreateDialogue("dlg", "start", check, success, failure);
            var random = new TestRandomSource();
            random.EnqueueResult(18);
            var presenter = new DialoguePresenter(dialogue, CreateContext(new DiceService(random)));

            var outcome = presenter.PerformCheck();

            Assert.IsTrue(outcome.Success);
            Assert.IsTrue(presenter.IsComplete);
        }
    }
}
