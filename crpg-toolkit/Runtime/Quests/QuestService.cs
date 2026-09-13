using System.Linq;
using CRPG.Core;
using CRPG.Inventory;

namespace CRPG.Quests
{
    /// <summary>
    /// Starts quests and recomputes their progress. Depends only on Core's Timeline/event bus plus, as of
    /// Task 3.4, an optional PartyInventoryState used solely to grant a completed quest's authored item
    /// rewards -- whatever produces a countable moment (combat, loot, dialogue, a future interaction/
    /// navigation system) records it to the Timeline; this service only ever reads that log for progress, so
    /// Quests still never needs a reference to Combat/Dialogue. "Objectives listen for gameplay events rather
    /// than polling Update" (spec) is satisfied at the integration level: whoever calls RefreshProgress does so
    /// in reaction to a relevant event, not on a timer.
    /// </summary>
    public sealed class QuestService
    {
        private readonly QuestLog log;
        private readonly ITimeline timeline;
        private readonly IGameEventBus eventBus;
        private readonly PartyInventoryState inventory;

        public QuestService(QuestLog log, ITimeline timeline, IGameEventBus eventBus = null, PartyInventoryState inventory = null)
        {
            this.log = log;
            this.timeline = timeline;
            this.eventBus = eventBus;
            this.inventory = inventory;
        }

        /// <returns>The existing QuestState if this quest was already started, otherwise a new Active one.</returns>
        public QuestState StartQuest(QuestDefinition definition)
        {
            if (log.TryGetQuest(definition.Id, out var existing)) return existing;

            var activationCursor = timeline.AllEvents().Count;
            var quest = new QuestState(definition.Id) { Status = QuestStatus.Active };
            foreach (var objective in definition.Objectives)
                quest.AddObjective(new QuestObjectiveState(objective.Id) { ActivationCursor = activationCursor });
            log.Add(quest);

            eventBus?.Publish(PublishWithQuestId(GameEvent.QuestStarted, definition.Id));
            return quest;
        }

        /// <summary>Recomputes every objective's progress from the Timeline and completes the quest once all
        /// of them are, granting the quest's rewards exactly once. A no-op if the quest hasn't been started or
        /// is already complete.</summary>
        public void RefreshProgress(QuestDefinition definition)
        {
            if (!log.TryGetQuest(definition.Id, out var quest) || quest.Status != QuestStatus.Active) return;

            var allComplete = true;
            foreach (var objectiveDefinition in definition.Objectives)
            {
                if (!quest.TryGetObjective(objectiveDefinition.Id, out var objective)) continue;

                var wasComplete = objective.IsComplete;
                EvaluateObjective(objectiveDefinition, objective);

                if (objective.IsComplete && !wasComplete)
                {
                    var evt = PublishWithQuestId(GameEvent.QuestObjectiveCompleted, definition.Id);
                    evt.Data["ObjectiveId"] = objectiveDefinition.Id;
                    eventBus?.Publish(evt);
                }

                if (!objective.IsComplete) allComplete = false;
            }

            if (!allComplete) return;

            quest.Status = QuestStatus.Completed;
            ClaimReward(definition, quest);
            eventBus?.Publish(PublishWithQuestId(GameEvent.QuestCompleted, definition.Id));
        }

        private void EvaluateObjective(QuestObjectiveDefinition objectiveDefinition, QuestObjectiveState objective)
        {
            switch (objectiveDefinition.ProgressMode)
            {
                case QuestObjectiveProgressMode.CountSinceActivation:
                    objective.CurrentCount = timeline.AllEvents(objectiveDefinition.TrackingKey)
                        .Count(e => e.Sequence >= objective.ActivationCursor);
                    objective.IsComplete = objective.CurrentCount >= objectiveDefinition.TargetCount;
                    break;

                case QuestObjectiveProgressMode.CurrentStatePredicate:
                    objective.IsComplete = TimelineCondition.Evaluate(
                        timeline, objectiveDefinition.ConditionType, objectiveDefinition.TrackingKey,
                        objectiveDefinition.SecondTimelineKey, objectiveDefinition.Threshold);
                    break;

                default: // HistoricalCount
                    objective.CurrentCount = timeline.CountOccurrences(objectiveDefinition.TrackingKey);
                    objective.IsComplete = objective.CurrentCount >= objectiveDefinition.TargetCount;
                    break;
            }
        }

        /// <summary>Idempotent regardless of whether Rewards is empty or inventory is null -- RewardClaimed is
        /// set unconditionally so "repeated refresh" and "reload" both can't double-grant, independent of the
        /// Status != Active early-return above (that guard is a separate, coarser safety net, not this one).</summary>
        private void ClaimReward(QuestDefinition definition, QuestState quest)
        {
            if (quest.RewardClaimed) return;

            if (inventory != null)
            {
                foreach (var reward in definition.Rewards)
                    inventory.AddItem(new ItemInstance(RuntimeEntityId.NewId(), reward.Item.Id, reward.Quantity));
            }

            quest.RewardClaimed = true;
        }

        private static GameEventContext PublishWithQuestId(GameEvent gameEvent, DefinitionId questId)
        {
            var evt = new GameEventContext(gameEvent);
            evt.Data["QuestId"] = questId.Value;
            return evt;
        }
    }
}
