using CRPG.Actors;
using CRPG.Core;
using CRPG.Dialogue;
using CRPG.Inventory;
using CRPG.Party;
using CRPG.Quests;
using CRPG.Rules;
using NUnit.Framework;

namespace CRPG.Tests.EditMode
{
    /// <summary>Proves the cross-system design claim: a "Talk To" quest objective needs zero Quests-side
    /// knowledge of Dialogue (CRPG.Quests never references CRPG.Dialogue) -- a conversation's RecordEventAction
    /// and a quest objective's TrackingKey just need to agree on the same Timeline key.</summary>
    public class QuestDialogueIntegrationTests
    {
        [Test]
        public void TalkingThroughADialogueRecordEventActionCompletesAMatchingTalkToObjective()
        {
            var rulesDatabase = new RulesDatabase();
            var timeline = new Timeline();
            var characters = new CharacterRegistry();
            var inventory = new PartyInventoryState();
            var fighter = new CharacterState(RuntimeEntityId.NewId());
            characters.Register(fighter);
            var party = new PartyState(new[] { fighter.Id });
            var npc = RuntimeEntityId.NewId();

            var quest = TestDefinitionFactory.CreateQuestDefinition(
                "quest-elder", "Speak With the Elder",
                TestDefinitionFactory.CreateQuestObjective("talk", "Talk to the Elder", QuestObjectiveType.TalkTo, "talked-to-elder", 1));
            rulesDatabase.Register(quest);

            var log = new QuestLog();
            var questService = new QuestService(log, timeline);
            var questState = questService.StartQuest(quest);

            var end = DialogueTestFactory.CreateEnd("end");
            var recordTalk = DialogueTestFactory.CreateAction("start", DialogueTestFactory.CreateRecordEventAction("talked-to-elder"), "end");
            var dialogue = DialogueTestFactory.CreateDialogue("dlg-elder", "start", recordTalk, end);
            var context = new DialogueContext(npc, party, characters, rulesDatabase, timeline, inventory, new DiceService(new SeededRandomSource(1)));

            new DialogueRunner(dialogue, context); // auto-settles through the ActionNode on construction

            questService.RefreshProgress(quest);

            Assert.AreEqual(QuestStatus.Completed, questState.Status);
        }
    }
}
