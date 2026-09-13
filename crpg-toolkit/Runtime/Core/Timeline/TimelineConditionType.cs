namespace CRPG.Core
{
    /// <summary>An ITimeline query kind, shared by anything that needs to gate on Timeline state -- Dialogue's
    /// BranchNode/ChoiceOption visibility today, Quests' CurrentStatePredicate objective mode as of Task 3.4.
    /// Lives in Core (not Dialogue) specifically so Quests can reuse it without a Quests -> Dialogue reference.</summary>
    public enum TimelineConditionType
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
