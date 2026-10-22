using System.Reflection;
using CRPG.Actors;
using CRPG.Core;
using CRPG.Dialogue;
using CRPG.Inventory;
using CRPG.Party;
using CRPG.Rules.SRD5;
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
                diceService ?? new DiceService(new SeededRandomSource(1)), new DefaultSkillCheckRule(characters, rulesDatabase));
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

        // Reflection keeps these regressions compilable before the retained-prompt API is implemented.
        private static T ReadPresenterProperty<T>(DialoguePresenter presenter, string propertyName)
        {
            var property = typeof(DialoguePresenter).GetProperty(propertyName, BindingFlags.Public | BindingFlags.Instance);
            Assert.NotNull(property, $"DialoguePresenter must expose public read-only {typeof(T).Name} {propertyName} for retained dialogue context.");
            Assert.AreEqual(typeof(T), property.PropertyType, $"DialoguePresenter.{propertyName} has the wrong type.");
            Assert.NotNull(property.GetGetMethod(), $"DialoguePresenter.{propertyName} must have a public getter.");
            Assert.IsNull(property.GetSetMethod(), $"DialoguePresenter.{propertyName} must not have a public setter.");
            return (T)property.GetValue(presenter);
        }

        [Test]
        public void AdvancingFromALineToChoicesRetainsTheLastNpcPrompt()
        {
            var end = DialogueTestFactory.CreateEnd("end");
            var choice = DialogueTestFactory.CreateChoice("reply", DialogueTestFactory.CreateOption("Leave", "end"));
            var line = DialogueTestFactory.CreateLine("start", "Eldor", "What brings you here?", "reply");
            var dialogue = DialogueTestFactory.CreateDialogue("dlg", "start", line, choice, end);
            var presenter = new DialoguePresenter(dialogue, CreateContext());

            Assert.AreEqual(SerializableGuid.FromString("start"), ReadPresenterProperty<SerializableGuid>(presenter, "CurrentNodeId"));
            Assert.AreEqual("What brings you here?", ReadPresenterProperty<string>(presenter, "LastLineText"));
            Assert.AreEqual("Eldor", ReadPresenterProperty<string>(presenter, "LastSpeakerName"));
            Assert.AreEqual(SerializableGuid.FromString("start"), ReadPresenterProperty<SerializableGuid>(presenter, "LastLineNodeId"));

            presenter.Advance();

            Assert.IsTrue(presenter.IsAtChoice);
            Assert.AreEqual(SerializableGuid.FromString("reply"), ReadPresenterProperty<SerializableGuid>(presenter, "CurrentNodeId"));
            Assert.AreEqual(string.Empty, presenter.LineText, "LineText still describes only the current node.");
            Assert.AreEqual(string.Empty, presenter.SpeakerName, "SpeakerName still describes only the current node.");
            Assert.AreEqual("What brings you here?", ReadPresenterProperty<string>(presenter, "LastLineText"));
            Assert.AreEqual("Eldor", ReadPresenterProperty<string>(presenter, "LastSpeakerName"));
            Assert.AreEqual(SerializableGuid.FromString("start"), ReadPresenterProperty<SerializableGuid>(presenter, "LastLineNodeId"));
            Assert.AreEqual("Leave", presenter.GetChoices()[0].Text);
        }

        [Test]
        public void ReachingASecondLineReplacesTheRetainedNpcPrompt()
        {
            var end = DialogueTestFactory.CreateEnd("end");
            var finalChoice = DialogueTestFactory.CreateChoice("final-reply", DialogueTestFactory.CreateOption("Goodbye", "end"));
            var secondLine = DialogueTestFactory.CreateLine("second", "Mira", "The road is clear now.", "final-reply");
            var firstChoice = DialogueTestFactory.CreateChoice("first-reply", DialogueTestFactory.CreateOption("Ask about the road", "second"));
            var firstLine = DialogueTestFactory.CreateLine("start", "Eldor", "What do you need?", "first-reply");
            var dialogue = DialogueTestFactory.CreateDialogue("dlg", "start", firstLine, firstChoice, secondLine, finalChoice, end);
            var presenter = new DialoguePresenter(dialogue, CreateContext());

            presenter.Advance();
            Assert.AreEqual("What do you need?", ReadPresenterProperty<string>(presenter, "LastLineText"));
            Assert.AreEqual("Eldor", ReadPresenterProperty<string>(presenter, "LastSpeakerName"));
            Assert.AreEqual(SerializableGuid.FromString("start"), ReadPresenterProperty<SerializableGuid>(presenter, "LastLineNodeId"));

            presenter.ChooseOption(0);

            Assert.IsTrue(presenter.IsAtLine);
            Assert.AreEqual(SerializableGuid.FromString("second"), ReadPresenterProperty<SerializableGuid>(presenter, "CurrentNodeId"));
            Assert.AreEqual("The road is clear now.", presenter.LineText);
            Assert.AreEqual("Mira", presenter.SpeakerName);
            Assert.AreEqual("The road is clear now.", ReadPresenterProperty<string>(presenter, "LastLineText"));
            Assert.AreEqual("Mira", ReadPresenterProperty<string>(presenter, "LastSpeakerName"));
            Assert.AreEqual(SerializableGuid.FromString("second"), ReadPresenterProperty<SerializableGuid>(presenter, "LastLineNodeId"));

            presenter.Advance();

            Assert.IsTrue(presenter.IsAtChoice);
            Assert.AreEqual(SerializableGuid.FromString("final-reply"), ReadPresenterProperty<SerializableGuid>(presenter, "CurrentNodeId"));
            Assert.AreEqual(string.Empty, presenter.LineText);
            Assert.AreEqual(string.Empty, presenter.SpeakerName);
            Assert.AreEqual("The road is clear now.", ReadPresenterProperty<string>(presenter, "LastLineText"));
            Assert.AreEqual("Mira", ReadPresenterProperty<string>(presenter, "LastSpeakerName"));
            Assert.AreEqual(SerializableGuid.FromString("second"), ReadPresenterProperty<SerializableGuid>(presenter, "LastLineNodeId"));
        }

        [Test]
        public void AChoiceOnlyEntryDoesNotInventRetainedNpcSpeech()
        {
            var end = DialogueTestFactory.CreateEnd("end");
            var choice = DialogueTestFactory.CreateChoice("start", DialogueTestFactory.CreateOption("Leave", "end"));
            var dialogue = DialogueTestFactory.CreateDialogue("dlg", "start", choice, end);
            var presenter = new DialoguePresenter(dialogue, CreateContext());

            Assert.IsTrue(presenter.IsAtChoice);
            Assert.AreEqual(SerializableGuid.FromString("start"), ReadPresenterProperty<SerializableGuid>(presenter, "CurrentNodeId"));
            Assert.AreEqual(string.Empty, presenter.LineText);
            Assert.AreEqual(string.Empty, presenter.SpeakerName);
            Assert.AreEqual(string.Empty, ReadPresenterProperty<string>(presenter, "LastLineText"));
            Assert.AreEqual(string.Empty, ReadPresenterProperty<string>(presenter, "LastSpeakerName"));
            Assert.AreEqual(SerializableGuid.Empty, ReadPresenterProperty<SerializableGuid>(presenter, "LastLineNodeId"));
            Assert.AreEqual("Leave", presenter.GetChoices()[0].Text);
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
            var end = DialogueTestFactory.CreateEnd("end");
            var success = DialogueTestFactory.CreateLine("success", "Mira", "You may pass.", "end");
            var failure = DialogueTestFactory.CreateEnd("failure");
            var check = DialogueTestFactory.CreateCheck("start", "Skill:Persuasion", 15, CheckPerformer.ActiveCharacter, "success", "failure");
            var dialogue = DialogueTestFactory.CreateDialogue("dlg", "start", check, success, failure, end);
            var random = new TestRandomSource();
            random.EnqueueResult(18);
            var presenter = new DialoguePresenter(dialogue, CreateContext(new DiceService(random)));

            var outcome = presenter.PerformCheck();

            Assert.IsTrue(outcome.Success);
            Assert.IsTrue(presenter.IsAtLine);
            Assert.AreEqual(SerializableGuid.FromString("success"), presenter.CurrentNodeId);
            Assert.AreEqual("You may pass.", presenter.LastLineText);
            Assert.AreEqual("Mira", presenter.LastSpeakerName);
            Assert.AreEqual(SerializableGuid.FromString("success"), presenter.LastLineNodeId);

            presenter.Advance();

            Assert.IsTrue(presenter.IsComplete);
            Assert.AreEqual(SerializableGuid.FromString("end"), presenter.CurrentNodeId);
            Assert.AreEqual("You may pass.", presenter.LastLineText);
            Assert.AreEqual(string.Empty, presenter.LineText);
        }
    }
}
