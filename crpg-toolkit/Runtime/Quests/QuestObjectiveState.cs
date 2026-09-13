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

        /// <summary>Timeline.AllEvents().Count at the moment this objective's quest was started -- the baseline
        /// CountSinceActivation subtracts against so pre-activation history doesn't count (Task 3.4).</summary>
        public int ActivationCursor { get; set; }
    }
}
