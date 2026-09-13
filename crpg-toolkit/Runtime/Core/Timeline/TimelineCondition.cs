namespace CRPG.Core
{
    /// <summary>The TimelineConditionType -> ITimeline query mapping. Shared by Dialogue's BranchNode routing/
    /// ChoiceOption visibility gating and Quests' CurrentStatePredicate objective mode so none of them drift
    /// apart or reimplement the same switch.</summary>
    public static class TimelineCondition
    {
        public static bool Evaluate(ITimeline timeline, TimelineConditionType conditionType, string key, string secondKey, double threshold) =>
            conditionType switch
            {
                TimelineConditionType.HasOccurred => timeline.HasOccurred(key),
                TimelineConditionType.NotOccurred => !timeline.HasOccurred(key),
                TimelineConditionType.SumAtLeast => timeline.Sum(key) >= threshold,
                TimelineConditionType.SumAtMost => timeline.Sum(key) <= threshold,
                TimelineConditionType.CountAtLeast => timeline.CountOccurrences(key) >= threshold,
                TimelineConditionType.OccurredBefore => timeline.OccurredBefore(key, secondKey),
                _ => false,
            };
    }
}
