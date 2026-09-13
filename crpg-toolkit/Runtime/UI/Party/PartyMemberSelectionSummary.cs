using CRPG.Core;

namespace CRPG.UI
{
    /// <summary>Presentation-friendly snapshot of one party member for a roster/selection panel.</summary>
    public sealed class PartyMemberSelectionSummary
    {
        public PartyMemberSelectionSummary(RuntimeEntityId id, TextKey displayName, bool isSelected, bool isActive, int currentHp, int maxHp)
        {
            Id = id;
            DisplayName = displayName;
            IsSelected = isSelected;
            IsActive = isActive;
            CurrentHp = currentHp;
            MaxHp = maxHp;
        }

        public RuntimeEntityId Id { get; }
        public TextKey DisplayName { get; }
        public bool IsSelected { get; }
        public bool IsActive { get; }
        public int CurrentHp { get; }
        public int MaxHp { get; }
    }
}
