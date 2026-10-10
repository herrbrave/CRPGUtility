using System;
using System.Linq;
using CRPG.Core;
using CRPG.Quests;
using UnityEngine;

namespace CRPG.Gameplay
{
    /// <summary>
    /// What every game with quests used to hand-roll around <see cref="QuestService"/> (Barrow's End's <c>RequestStartQuest</c> / <c>RefreshAllActiveQuests</c>): start a quest from an authored key, keep every active quest's
    /// progress current, and leave a trace in the Timeline that dialogue can branch on. One tracker per session (re-create it when the session is swapped; it unsubscribes on <see cref="Dispose"/>).
    /// <para>
    /// Progress comes only from the Timeline (see <see cref="QuestObjectiveDefinition"/>), so something must call <see cref="RefreshAll"/> after the Timeline changes: the composition root calls it when an interaction
    /// ends, and <see cref="Tick"/> (called every frame) refreshes when the Timeline has grown. A quest completing grants its item rewards once (the service does that). The tracker records two Timeline keys so a
    /// dialogue can tell where a quest stands: <see cref="StartedKey"/> and <see cref="CompletedKey"/>.
    /// </para>
    /// </summary>
    public sealed class QuestTracker : IDisposable
    {
        public const string StartedKeyPrefix = "quest-started:";
        public const string CompletedKeyPrefix = "quest-completed:";

        private readonly GameSession session;
        private readonly QuestService service;
        private int lastTimelineCount;
        private bool disposed;

        public QuestTracker(GameSession session)
        {
            this.session = session ?? throw new ArgumentNullException(nameof(session));
            service = new QuestService(session.Quests, session.Timeline, session.EventBus, session.Inventory);
            session.EventBus.Subscribe(GameEvent.QuestStarted, OnQuestStarted);
            session.EventBus.Subscribe(GameEvent.QuestCompleted, OnQuestCompleted);
            lastTimelineCount = session.Timeline.AllEvents().Count;
        }

        public GameSession Session => session;

        /// <summary>The Timeline key recorded when a quest starts.</summary>
        public static string StartedKey(string questId) => StartedKeyPrefix + questId;

        /// <summary>The Timeline key recorded when a quest completes.</summary>
        public static string CompletedKey(string questId) => CompletedKeyPrefix + questId;

        /// <summary>Starts the quest whose <see cref="DefinitionId"/> is <paramref name="questKey"/> (what <c>StartQuestAction</c> authors), then refreshes. Starting a quest that is already started changes nothing.</summary>
        /// <returns>The quest's state, or null (with a warning) when the key resolves to no quest in the session's ruleset: register the asset in the ruleset.</returns>
        public QuestState Start(string questKey)
        {
            if (disposed) return null;
            if (string.IsNullOrEmpty(questKey) || !session.RulesDatabase.TryResolve<QuestDefinition>(new DefinitionId(questKey), out var definition))
            {
                Debug.LogWarning($"[QuestTracker] no quest '{questKey}' in the session's ruleset: it was not started. Register the quest asset in the ruleset.");
                return null;
            }

            var state = service.StartQuest(definition);
            RefreshAll();
            return state;
        }

        /// <summary>Recomputes the progress of every active quest from the Timeline.</summary>
        public void RefreshAll()
        {
            if (disposed) return;

            foreach (var quest in session.Quests.Quests.Where(q => q.Status == QuestStatus.Active).ToList())
            {
                if (session.RulesDatabase.TryResolve<QuestDefinition>(quest.QuestId, out var definition)) service.RefreshProgress(definition);
            }

            lastTimelineCount = session.Timeline.AllEvents().Count;
        }

        /// <summary>Call every frame: refreshes when the Timeline has grown since the last refresh (it has no change event).</summary>
        public void Tick()
        {
            if (disposed) return;
            if (session.Timeline.AllEvents().Count != lastTimelineCount) RefreshAll();
        }

        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            session.EventBus.Unsubscribe(GameEvent.QuestStarted, OnQuestStarted);
            session.EventBus.Unsubscribe(GameEvent.QuestCompleted, OnQuestCompleted);
        }

        private void OnQuestStarted(GameEventContext context) => Record(StartedKeyPrefix, context);

        private void OnQuestCompleted(GameEventContext context) => Record(CompletedKeyPrefix, context);

        private void Record(string prefix, GameEventContext context)
        {
            if (context.Data.TryGetValue("QuestId", out var id) && id is string questId)
                session.Timeline.Record(prefix + questId, TimelineValue.Flag);
        }
    }
}
