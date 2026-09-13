using System.Linq;
using CRPG.Core;
using CRPG.Inventory;
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

        [Test]
        public void CountSinceActivationIgnoresOccurrencesRecordedBeforeTheQuestStarted()
        {
            timeline.Record("kill-wolf", TimelineValue.Flag); // pre-existing history, before the quest exists
            var quest = RegisterQuest(TestDefinitionFactory.CreateQuestObjective(
                "kill-wolves", "Kill 2 Wolves", QuestObjectiveType.Kill, "kill-wolf", 2,
                progressMode: QuestObjectiveProgressMode.CountSinceActivation));
            var state = service.StartQuest(quest);

            timeline.Record("kill-wolf", TimelineValue.Flag); // only this one counts
            service.RefreshProgress(quest);
            state.TryGetObjective("kill-wolves", out var objective);

            Assert.AreEqual(1, objective.CurrentCount);
            Assert.IsFalse(objective.IsComplete);

            timeline.Record("kill-wolf", TimelineValue.Flag);
            service.RefreshProgress(quest);
            state.TryGetObjective("kill-wolves", out objective);

            Assert.AreEqual(2, objective.CurrentCount);
            Assert.IsTrue(objective.IsComplete);
        }

        [Test]
        public void HistoricalCountStillCountsOccurrencesFromBeforeActivationUnlikeCountSinceActivation()
        {
            timeline.Record("kill-wolf", TimelineValue.Flag); // pre-existing history
            var quest = RegisterQuest(TestDefinitionFactory.CreateQuestObjective(
                "kill-wolves", "Kill 1 Wolf", QuestObjectiveType.Kill, "kill-wolf", 1)); // default HistoricalCount
            var state = service.StartQuest(quest);

            service.RefreshProgress(quest); // no NEW occurrence needed -- history alone already satisfies it

            state.TryGetObjective("kill-wolves", out var objective);
            Assert.IsTrue(objective.IsComplete);
        }

        [Test]
        public void CurrentStatePredicateCompletesBasedOnLiveTimelineStateNotACount()
        {
            var quest = RegisterQuest(TestDefinitionFactory.CreateQuestObjective(
                "flagged", "Something Happened", QuestObjectiveType.Custom, "world-event", targetCount: 0,
                progressMode: QuestObjectiveProgressMode.CurrentStatePredicate, conditionType: TimelineConditionType.HasOccurred));
            var state = service.StartQuest(quest);

            service.RefreshProgress(quest);
            state.TryGetObjective("flagged", out var beforeObjective);
            Assert.IsFalse(beforeObjective.IsComplete);

            timeline.Record("world-event", TimelineValue.Flag);
            service.RefreshProgress(quest);
            state.TryGetObjective("flagged", out var afterObjective);

            Assert.IsTrue(afterObjective.IsComplete);
            Assert.AreEqual(QuestStatus.Completed, state.Status);
        }

        [Test]
        public void QuestStartedAndCompletedEventsCarryTheQuestId()
        {
            var quest = RegisterQuest(TestDefinitionFactory.CreateQuestObjective("obj", "Objective", QuestObjectiveType.Custom, "custom-key", 1));
            GameEventContext startedEvent = null, completedEvent = null, objectiveEvent = null;
            eventBus.Subscribe(GameEvent.QuestStarted, e => startedEvent = e);
            eventBus.Subscribe(GameEvent.QuestCompleted, e => completedEvent = e);
            eventBus.Subscribe(GameEvent.QuestObjectiveCompleted, e => objectiveEvent = e);

            service.StartQuest(quest);
            timeline.Record("custom-key", TimelineValue.Flag);
            service.RefreshProgress(quest);

            Assert.AreEqual(quest.Id.Value, startedEvent.Data["QuestId"]);
            Assert.AreEqual(quest.Id.Value, completedEvent.Data["QuestId"]);
            Assert.AreEqual(quest.Id.Value, objectiveEvent.Data["QuestId"]);
            Assert.AreEqual("obj", objectiveEvent.Data["ObjectiveId"]);
        }

        [Test]
        public void CompletingAQuestGrantsItsRewardsExactlyOnceAcrossTwoRefreshCalls()
        {
            var inventory = new PartyInventoryState();
            var reward = TestDefinitionFactory.CreateItemDefinition("item-gem", "Gem");
            rulesDatabase.Register(reward);
            var quest = TestDefinitionFactory.CreateQuestDefinitionWithRewards(
                "quest-reward", "Reward Quest",
                new[] { TestDefinitionFactory.CreateQuestObjective("obj", "Objective", QuestObjectiveType.Custom, "custom-key", 1) },
                TestDefinitionFactory.CreateQuestReward(reward, quantity: 2));
            rulesDatabase.Register(quest);
            var rewardingService = new QuestService(log, timeline, eventBus, inventory);
            var state = rewardingService.StartQuest(quest);

            timeline.Record("custom-key", TimelineValue.Flag);
            rewardingService.RefreshProgress(quest);
            rewardingService.RefreshProgress(quest); // already Completed -- must not grant a second time

            Assert.IsTrue(state.RewardClaimed);
            var granted = inventory.AllItems.Where(i => i.DefinitionId == reward.Id).ToList();
            Assert.AreEqual(1, granted.Count);
            Assert.AreEqual(2, granted[0].Quantity);
        }

        [Test]
        public void RewardIsNotDoubleGrantedAfterASimulatedReload()
        {
            // Simulates SaveService.RestoreQuests: a QuestState is reconstructed directly with Status already
            // Completed and RewardClaimed already true, bypassing StartQuest/RefreshProgress entirely.
            var inventory = new PartyInventoryState();
            var reward = TestDefinitionFactory.CreateItemDefinition("item-gem", "Gem");
            rulesDatabase.Register(reward);
            var quest = TestDefinitionFactory.CreateQuestDefinitionWithRewards(
                "quest-reward", "Reward Quest",
                new[] { TestDefinitionFactory.CreateQuestObjective("obj", "Objective", QuestObjectiveType.Custom, "custom-key", 1) },
                TestDefinitionFactory.CreateQuestReward(reward, quantity: 2));
            rulesDatabase.Register(quest);

            var reloadedQuest = new QuestState(quest.Id) { Status = QuestStatus.Completed, RewardClaimed = true };
            reloadedQuest.AddObjective(new QuestObjectiveState("obj") { IsComplete = true });
            log.Add(reloadedQuest);

            var rewardingService = new QuestService(log, timeline, eventBus, inventory);
            rewardingService.RefreshProgress(quest); // Status != Active guard makes this a no-op regardless

            Assert.IsEmpty(inventory.AllItems.Where(i => i.DefinitionId == reward.Id));
        }
    }
}
