namespace CRPG.UI
{
    /// <summary>Presentation-friendly snapshot of one quest objective's progress.</summary>
    public sealed class QuestObjectiveSummary
    {
        public QuestObjectiveSummary(string displayName, int currentCount, int targetCount, bool isComplete)
        {
            DisplayName = displayName;
            CurrentCount = currentCount;
            TargetCount = targetCount;
            IsComplete = isComplete;
        }

        public string DisplayName { get; }
        public int CurrentCount { get; }
        public int TargetCount { get; }
        public bool IsComplete { get; }
    }
}
