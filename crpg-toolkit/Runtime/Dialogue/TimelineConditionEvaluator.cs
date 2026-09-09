using CRPG.Core;

namespace CRPG.Dialogue
{
    /// <summary>The BranchConditionType -> ITimeline query mapping, shared by BranchNode's routing and
    /// ChoiceOption's visibility gating so the two don't drift apart.</summary>
    internal static class TimelineConditionEvaluator
    {
        public static bool Evaluate(ITimeline timeline, BranchConditionType conditionType, string key, string secondKey, double threshold) =>
            conditionType switch
            {
                BranchConditionType.HasOccurred => timeline.HasOccurred(key),
                BranchConditionType.NotOccurred => !timeline.HasOccurred(key),
                BranchConditionType.SumAtLeast => timeline.Sum(key) >= threshold,
                BranchConditionType.SumAtMost => timeline.Sum(key) <= threshold,
                BranchConditionType.CountAtLeast => timeline.CountOccurrences(key) >= threshold,
                BranchConditionType.OccurredBefore => timeline.OccurredBefore(key, secondKey),
                _ => false,
            };
    }
}
