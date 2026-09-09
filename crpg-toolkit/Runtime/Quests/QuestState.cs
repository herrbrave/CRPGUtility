using System.Collections.Generic;
using CRPG.Core;

namespace CRPG.Quests
{
    /// <summary>Runtime progress for one quest a party has started. Never references QuestDefinition directly
    /// -- only its DefinitionId, resolved through an IRulesDatabase when the authored data is actually needed
    /// (same definitions/state split as everything else).</summary>
    public sealed class QuestState
    {
        private readonly Dictionary<string, QuestObjectiveState> objectives = new();

        public QuestState(DefinitionId questId)
        {
            QuestId = questId;
        }

        public DefinitionId QuestId { get; }
        public QuestStatus Status { get; set; } = QuestStatus.Inactive;
        public IReadOnlyDictionary<string, QuestObjectiveState> Objectives => objectives;

        public void AddObjective(QuestObjectiveState objective) => objectives[objective.ObjectiveId] = objective;

        public bool TryGetObjective(string objectiveId, out QuestObjectiveState objective) => objectives.TryGetValue(objectiveId, out objective);
    }
}
