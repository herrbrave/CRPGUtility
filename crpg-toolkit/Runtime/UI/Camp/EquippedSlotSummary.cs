namespace CRPG.UI
{
    /// <summary>One equipment slot's display info for the Camp screen's party-doll view.</summary>
    public sealed class EquippedSlotSummary
    {
        public EquippedSlotSummary(string slotName, string itemName)
        {
            SlotName = slotName;
            ItemName = itemName;
        }

        public string SlotName { get; }

        /// <summary>Null/empty means nothing is equipped in this slot.</summary>
        public string ItemName { get; }
    }
}
