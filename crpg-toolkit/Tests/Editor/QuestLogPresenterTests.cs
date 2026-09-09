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
    }
}
