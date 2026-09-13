using System.Collections.Generic;
using CRPG.Quests;

namespace CRPG.UI
{
    /// <summary>Presentation-friendly snapshot of one journal entry -- decoupled from raw QuestState/
    /// QuestDefinition so a View never touches engine internals directly.</summary>
    public sealed class QuestSummary
    {
        public QuestSummary(string displayName, string description, QuestStatus status, IReadOnlyList<QuestObjectiveSummary> objectives, IReadOnlyList<QuestRewardSummary> rewards)
        {
            DisplayName = displayName;
            Description = description;
            Status = status;
            Objectives = objectives;
            Rewards = rewards;
        }

        public string DisplayName { get; }
        public string Description { get; }
        public QuestStatus Status { get; }
        public IReadOnlyList<QuestObjectiveSummary> Objectives { get; }
        public IReadOnlyList<QuestRewardSummary> Rewards { get; }
    }
}
