using CRPG.Core;

namespace CRPG.UI
{
    /// <summary>Task 4.5: one row of a turn-order display -- plain data, no UI Toolkit dependency, matching
    /// every other *Summary type in this folder (PartyMemberSelectionSummary, QuestSummary, ...).</summary>
    public sealed class TurnOrderEntrySummary
    {
        public TurnOrderEntrySummary(RuntimeEntityId id, string displayName, DefinitionId faction, bool isCurrentTurn)
        {
            Id = id;
            DisplayName = displayName;
            Faction = faction;
            IsCurrentTurn = isCurrentTurn;
        }

        public RuntimeEntityId Id { get; }
        public string DisplayName { get; }
        public DefinitionId Faction { get; }
        public bool IsCurrentTurn { get; }
    }
}
