using CRPG.Core;
using CRPG.Quests;
using CRPG.Rules;
using NUnit.Framework;

namespace CRPG.Tests.EditMode
{
    public class QuestServiceTests
    {
        private RulesDatabase rulesDatabase;
        private Timeline timeline;
        private GameEventBus eventBus;
        private QuestLog log;
        private QuestService service;

        [SetUp]
        public void SetUp()
        {
            rulesDatabase = new RulesDatabase();
            timeline = new Timeline();
            eventBus = new GameEventBus();
            log = new QuestLog();
            service = new QuestService(log, timeline, eventBus);
        }

        private QuestDefinition RegisterQuest(params QuestObjectiveDefinition[] objectives)
        {
            var quest = TestDefinitionFactory.CreateQuestDefinition("quest-test", "Test Quest", objectives);
            rulesDatabase.Register(quest);
            return quest;
        }

        [Test]
        public void StartQuestCreatesAnActiveQuestWithIncompleteObjectives()
        {
            var objective = TestDefinitionFactory.CreateQuestObjective("kill-wolves", "Kill 3 Wolves", QuestObjectiveType.Kill, "kill-wolf", 3);
            var quest = RegisterQuest(objective);

            var state = service.StartQuest(quest);

            Assert.AreEqual(QuestStatus.Active, state.Status);
            Assert.IsTrue(state.TryGetObjective("kill-wolves", out var objectiveState));
            Assert.AreEqual(0, objectiveState.CurrentCount);
            Assert.IsFalse(objectiveState.IsComplete);
        }

        [Test]
        public void StartQuestIsIdempotent()
        {
            var quest = RegisterQuest(TestDefinitionFactory.CreateQuestObjective("obj", "Objective", QuestObjectiveType.Custom, "custom-key"));

            var first = service.StartQuest(quest);
            var second = service.StartQuest(quest);

            Assert.AreSame(first, second);
        }

        [Test]
        public void StartQuestPublishesQuestStarted()
        {
            var quest = RegisterQuest(TestDefinitionFactory.CreateQuestObjective("obj", "Objective", QuestObjectiveType.Custom, "custom-key"));
            var fired = false;
            eventBus.Subscribe(GameEvent.QuestStarted, _ => fired = true);

            service.StartQuest(quest);

            Assert.IsTrue(fired);
        }

        [Test]
        public void RefreshProgressCompletesAnObjectiveOnceItsTrackingKeyReachesTargetCount()
        {
            var quest = RegisterQuest(TestDefinitionFactory.CreateQuestObjective("kill-wolves", "Kill 3 Wolves", QuestObjectiveType.Kill, "kill-wolf", 3));
            var state = service.StartQuest(quest);

            timeline.Record("kill-wolf", TimelineValue.Flag);
            timeline.Record("kill-wolf", TimelineValue.Flag);
            service.RefreshProgress(quest);
            state.TryGetObjective("kill-wolves", out var partial);

            Assert.AreEqual(2, partial.CurrentCount);
            Assert.IsFalse(partial.IsComplete);

            timeline.Record("kill-wolf", TimelineValue.Flag);
            service.RefreshProgress(quest);
            state.TryGetObjective("kill-wolves", out var complete);

            Assert.AreEqual(3, complete.CurrentCount);
            Assert.IsTrue(complete.IsComplete);
        }

        [Test]
        public void RefreshProgressCompletesTheQuestOnlyOnceEveryObjectiveIsComplete()
        {
            var quest = RegisterQuest(
                TestDefinitionFactory.CreateQuestObjective("talk", "Talk to the Elder", QuestObjectiveType.TalkTo, "talked-to-elder", 1),
                TestDefinitionFactory.CreateQuestObjective("collect", "Collect 2 Herbs", QuestObjectiveType.Collect, "collected-herb", 2));
            var state = service.StartQuest(quest);

            timeline.Record("talked-to-elder", TimelineValue.Flag);
            service.RefreshProgress(quest);

            Assert.AreEqual(QuestStatus.Active, state.Status); // herbs objective still incomplete

            timeline.Record("collected-herb", TimelineValue.Flag);
            timeline.Record("collected-herb", TimelineValue.Flag);
            service.RefreshProgress(quest);

            Assert.AreEqual(QuestStatus.Completed, state.Status);
        }

        [Test]
        public void RefreshProgressPublishesQuestObjectiveCompletedExactlyOncePerObjective()
        {
            var quest = RegisterQuest(TestDefinitionFactory.CreateQuestObjective("obj", "Objective", QuestObjectiveType.Custom, "custom-key", 1));
            service.StartQuest(quest);
            var completedCount = 0;
            eventBus.Subscribe(GameEvent.QuestObjectiveCompleted, _ => completedCount++);

            timeline.Record("custom-key", TimelineValue.Flag);
            service.RefreshProgress(quest);
            service.RefreshProgress(quest); // already complete -- must not fire again

            Assert.AreEqual(1, completedCount);
        }

        [Test]
        public void RefreshProgressPublishesQuestCompletedWhenTheLastObjectiveFinishes()
        {
            var quest = RegisterQuest(TestDefinitionFactory.CreateQuestObjective("obj", "Objective", QuestObjectiveType.Custom, "custom-key", 1));
            service.StartQuest(quest);
            var fired = false;
            eventBus.Subscribe(GameEvent.QuestCompleted, _ => fired = true);

            timeline.Record("custom-key", TimelineValue.Flag);
            service.RefreshProgress(quest);

            Assert.IsTrue(fired);
        }

        [Test]
        public void RefreshProgressIsANoOpForAQuestThatWasNeverStarted()
        {
            var quest = RegisterQuest(TestDefinitionFactory.CreateQuestObjective("obj", "Objective", QuestObjectiveType.Custom, "custom-key", 1));

            Assert.DoesNotThrow(() => service.RefreshProgress(quest));
            Assert.IsFalse(log.TryGetQuest(quest.Id, out _));
        }
    }
}
