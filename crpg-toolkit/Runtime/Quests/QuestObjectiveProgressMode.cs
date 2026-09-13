namespace CRPG.Quests
{
    /// <summary>How a QuestObjectiveDefinition's progress is derived from the Timeline (Task 3.4).</summary>
    public enum QuestObjectiveProgressMode
    {
        /// <summary>Counts every occurrence of TrackingKey ever recorded, including before the quest was
        /// started -- today's original (and only) behavior, kept as the default so every existing/migrated
        /// QuestObjectiveDefinition asset behaves exactly as before.</summary>
        HistoricalCount,

        /// <summary>Counts only occurrences of TrackingKey recorded at or after this objective's own
        /// ActivationCursor (captured when the quest was started) -- pre-existing Timeline history from before
        /// the quest was ever active doesn't count.</summary>
        CountSinceActivation,

        /// <summary>Ignores CurrentCount/TargetCount entirely -- this objective's IsComplete is whatever the
        /// current Timeline state satisfies (same TimelineCondition evaluator Dialogue's BranchNode uses),
        /// re-evaluated fresh every RefreshProgress call while the quest is still Active. This objective alone
        /// can flip back to incomplete on a later refresh if the condition stops holding; once every objective
        /// is simultaneously complete the whole quest still only ever transitions Active -> Completed once
        /// (QuestService never re-evaluates a quest that has already completed).</summary>
        CurrentStatePredicate,
    }
}
