namespace CRPG.Quests
{
    /// <summary>Spec's objective kinds (§47). Purely descriptive/for-display -- every kind resolves the same
    /// way at runtime (QuestService.RefreshProgress counts Timeline occurrences of the objective's TrackingKey
    /// against its TargetCount), so this never branches completion logic. Whatever produces the moment worth
    /// tracking -- combat code on a kill, loot code on a pickup, Dialogue's RecordEventAction on a
    /// conversation, a future Interaction/Navigation system on reaching a place or using something -- just
    /// needs to call ITimeline.Record with the matching key; the quest engine doesn't need to know which of
    /// these kinds produced it.</summary>
    public enum QuestObjectiveType
    {
        ReachLocation,
        TalkTo,
        Kill,
        Collect,
        Interact,
        SetFlag,
        Custom
    }
}
