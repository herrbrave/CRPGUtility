using System.Collections.Generic;
using CRPG.Core;

namespace CRPG.Quests
{
    /// <summary>The party's journal -- every quest it has started, active or completed. Party-scoped like the
    /// shared inventory, not per-character.</summary>
    public sealed class QuestLog
    {
        private readonly Dictionary<DefinitionId, QuestState> questsById = new();

        public IReadOnlyCollection<QuestState> Quests => questsById.Values;

        public void Add(QuestState quest) => questsById[quest.QuestId] = quest;

        public bool TryGetQuest(DefinitionId questId, out QuestState quest) => questsById.TryGetValue(questId, out quest);
    }
}
