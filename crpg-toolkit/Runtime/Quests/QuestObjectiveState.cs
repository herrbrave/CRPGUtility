namespace CRPG.Quests
{
    /// <summary>Runtime progress toward one QuestObjectiveDefinition.</summary>
    public sealed class QuestObjectiveState
    {
        public QuestObjectiveState(string objectiveId)
        {
            ObjectiveId = objectiveId;
        }

        public string ObjectiveId { get; }
        public int CurrentCount { get; set; }
        public bool IsComplete { get; set; }
    }
}
