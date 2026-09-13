using System;

namespace CRPG.UI
{
    /// <summary>Presentation-friendly snapshot of one save slot.</summary>
    public sealed class SaveSlotSummary
    {
        public SaveSlotSummary(int slotIndex, DateTime? savedAtUtc, TextKey statusLabel, bool isOccupied)
        {
            SlotIndex = slotIndex;
            SavedAtUtc = savedAtUtc;
            StatusLabel = statusLabel;
            IsOccupied = isOccupied;
        }

        public int SlotIndex { get; }
        public DateTime? SavedAtUtc { get; }
        public TextKey StatusLabel { get; }
        public bool IsOccupied { get; }
    }
}
