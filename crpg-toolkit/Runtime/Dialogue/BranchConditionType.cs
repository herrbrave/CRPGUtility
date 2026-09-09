namespace CRPG.Dialogue
{
    public enum BranchConditionType
    {
        HasOccurred,
        NotOccurred,
        SumAtLeast,
        SumAtMost,
        CountAtLeast,

        /// <summary>Compares TimelineKey's first occurrence against SecondTimelineKey's.</summary>
        OccurredBefore
    }
}
