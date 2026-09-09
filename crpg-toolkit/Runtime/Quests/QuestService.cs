using CRPG.Core;

namespace CRPG.Quests
{
    /// <summary>
    /// Starts quests and recomputes their progress. Deliberately depends on nothing but Core's Timeline/
    /// event bus -- whatever produces a countable moment (combat, loot, dialogue, a future interaction/
    /// navigation system) records it to the Timeline; this service only ever reads that log, so Quests never
    /// needs an assembly reference to Combat/Inventory/Dialogue. "Objectives listen for gameplay events rather
    /// than polling Update" (spec) is satisfied at the integration level: whoever calls RefreshProgress does so
    /// in reaction to a relevant event, not on a timer.
    /// </summary>
    public sealed class QuestService
    {
        private readonly QuestLog log;
        private readonly ITimeline timeline;
        private readonly IGameEventBus eventBus;

        public QuestService(QuestLog log, ITimeline timeline, IGameEventBus eventBus = null)
        {
            this.log = log;
            this.timeline = timeline;
            this.eventBus = eventBus;
        }

        /// <returns>The existing QuestState if this quest was already started, otherwise a new Active one.</returns>
        public QuestState StartQuest(QuestDefinition definition)
        {
            if (log.TryGetQuest(definition.Id, out var existing)) return existing;

            var quest = new QuestState(definition.Id) { Status = QuestStatus.Active };
            foreach (var objective in definition.Objectives) quest.AddObjective(new QuestObjectiveState(objective.Id));
            log.Add(quest);

            eventBus?.Publish(new GameEventContext(GameEvent.QuestStarted));
            return quest;
        }

        /// <summary>Recomputes every objective's progress from the Timeline and completes the quest once all
        /// of them are. A no-op if the quest hasn't been started or is already complete.</summary>
        public void RefreshProgress(QuestDefinition definition)
        {
            if (!log.TryGetQuest(definition.Id, out var quest) || quest.Status != QuestStatus.Active) return;

            var allComplete = true;
            foreach (var objectiveDefinition in definition.Objectives)
            {
                if (!quest.TryGetObjective(objectiveDefinition.Id, out var objective)) continue;

                var wasComplete = objective.IsComplete;
                objective.CurrentCount = timeline.CountOccurrences(objectiveDefinition.TrackingKey);
                objective.IsComplete = objective.CurrentCount >= objectiveDefinition.TargetCount;

                if (objective.IsComplete && !wasComplete)
                    eventBus?.Publish(new GameEventContext(GameEvent.QuestObjectiveCompleted));

                if (!objective.IsComplete) allComplete = false;
            }

            if (!allComplete) return;

            quest.Status = QuestStatus.Completed;
            eventBus?.Publish(new GameEventContext(GameEvent.QuestCompleted));
        }
    }
}
