using System.Collections.Generic;

namespace CRPG.UI
{
    /// <summary>Presentation-friendly snapshot of one party member for the Camp screen -- decoupled from raw
    /// CharacterState/EquipmentService so the View never touches engine internals directly.</summary>
    public sealed class PartyMemberSummary
    {
        public PartyMemberSummary(string displayName, int currentHitPoints, int maxHitPoints, IReadOnlyList<EquippedSlotSummary> equippedSlots)
        {
            DisplayName = displayName;
            CurrentHitPoints = currentHitPoints;
            MaxHitPoints = maxHitPoints;
            EquippedSlots = equippedSlots;
        }

        public string DisplayName { get; }
        public int CurrentHitPoints { get; }
        public int MaxHitPoints { get; }
        public IReadOnlyList<EquippedSlotSummary> EquippedSlots { get; }
    }
}
