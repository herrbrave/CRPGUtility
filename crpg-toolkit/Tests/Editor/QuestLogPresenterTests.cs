using CRPG.Core;
using CRPG.Quests;
using CRPG.Rules;
using CRPG.UI;
using NUnit.Framework;

namespace CRPG.Tests.EditMode
{
    public class QuestLogPresenterTests
    {
        [Test]
        public void GetQuestsReportsDisplayNameStatusAndObjectiveProgress()
        {
            var rulesDatabase = new RulesDatabase();
            var timeline = new Timeline();
            var log = new QuestLog();
            var service = new QuestService(log, timeline);

            var quest = TestDefinitionFactory.CreateQuestDefinition(
                "quest-elder", "Speak With the Elder",
                TestDefinitionFactory.CreateQuestObjective("talk", "Talk to the Elder", QuestObjectiveType.TalkTo, "talked-to-elder", 1));
            rulesDatabase.Register(quest);
            service.StartQuest(quest);
            timeline.Record("talked-to-elder", TimelineValue.Flag);
            service.RefreshProgress(quest);

            var presenter = new QuestLogPresenter(log, rulesDatabase);
            var summaries = presenter.GetQuests();

            Assert.AreEqual(1, summaries.Count);
            Assert.AreEqual("Speak With the Elder", summaries[0].DisplayName);
            Assert.AreEqual(QuestStatus.Completed, summaries[0].Status);
            Assert.AreEqual(1, summaries[0].Objectives.Count);
            Assert.AreEqual("Talk to the Elder", summaries[0].Objectives[0].DisplayName);
            Assert.IsTrue(summaries[0].Objectives[0].IsComplete);
        }

        [Test]
        public void GetQuestsSurfacesItemRewards()
        {
            var rulesDatabase = new RulesDatabase();
            var timeline = new Timeline();
            var log = new QuestLog();
            var service = new QuestService(log, timeline);

            var potion = TestDefinitionFactory.CreateItemDefinition("item-potion", "Healing Potion");
            var objective = TestDefinitionFactory.CreateQuestObjective("talk", "Talk to the Elder", QuestObjectiveType.TalkTo, "talked-to-elder", 1);
            var quest = TestDefinitionFactory.CreateQuestDefinitionWithRewards(
                "quest-elder", "Speak With the Elder", new[] { objective }, TestDefinitionFactory.CreateQuestReward(potion, 3));
            rulesDatabase.Register(quest);
            service.StartQuest(quest);

            var presenter = new QuestLogPresenter(log, rulesDatabase);
            var summaries = presenter.GetQuests();

            Assert.AreEqual(1, summaries[0].Rewards.Count);
            Assert.AreEqual("Healing Potion", summaries[0].Rewards[0].ItemName.Fallback);
            Assert.AreEqual(3, summaries[0].Rewards[0].Quantity);
        }

        [Test]
        public void GetActiveAndGetCompletedFilterByStatus()
        {
            var rulesDatabase = new RulesDatabase();
            var timeline = new Timeline();
            var log = new QuestLog();
            var service = new QuestService(log, timeline);

            var activeObjective = TestDefinitionFactory.CreateQuestObjective("obj-a", "Do A", QuestObjectiveType.TalkTo, "flag-a", 1);
            var activeQuest = TestDefinitionFactory.CreateQuestDefinition("quest-active", "Active Quest", activeObjective);
            rulesDatabase.Register(activeQuest);
            service.StartQuest(activeQuest);

            var doneObjective = TestDefinitionFactory.CreateQuestObjective("obj-b", "Do B", QuestObjectiveType.TalkTo, "flag-b", 1);
            var doneQuest = TestDefinitionFactory.CreateQuestDefinition("quest-done", "Done Quest", doneObjective);
            rulesDatabase.Register(doneQuest);
            service.StartQuest(doneQuest);
            timeline.Record("flag-b", TimelineValue.Flag);
            service.RefreshProgress(doneQuest);

            var presenter = new QuestLogPresenter(log, rulesDatabase);

            var active = presenter.GetActiveQuests();
            var completed = presenter.GetCompletedQuests();

            Assert.AreEqual(1, active.Count);
            Assert.AreEqual("Active Quest", active[0].DisplayName);
            Assert.AreEqual(1, completed.Count);
            Assert.AreEqual("Done Quest", completed[0].DisplayName);
        }

        [Test]
        public void RebindSwapsInANewLogAndRulesDatabase()
        {
            var rulesDatabase = new RulesDatabase();
            var log = new QuestLog();
            var presenter = new QuestLogPresenter(log, rulesDatabase);

            var newRulesDatabase = new RulesDatabase();
            var newLog = new QuestLog();
            var objective = TestDefinitionFactory.CreateQuestObjective("obj", "Do It", QuestObjectiveType.TalkTo, "flag", 1);
            var quest = TestDefinitionFactory.CreateQuestDefinition("quest-new", "New Quest", objective);
            newRulesDatabase.Register(quest);
            new QuestService(newLog, new Timeline()).StartQuest(quest);

            presenter.Rebind(newLog, newRulesDatabase);
            var summaries = presenter.GetQuests();

            Assert.AreEqual(1, summaries.Count);
            Assert.AreEqual("New Quest", summaries[0].DisplayName);
        }
    }
}
