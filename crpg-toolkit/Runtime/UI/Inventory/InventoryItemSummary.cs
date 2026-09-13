using CRPG.Core;

namespace CRPG.UI
{
    /// <summary>Presentation-friendly snapshot of one owned item instance.</summary>
    public sealed class InventoryItemSummary
    {
        public InventoryItemSummary(RuntimeEntityId instanceId, TextKey name, int quantity, bool isEquipped, RuntimeEntityId? equippedBy, TextKey? slotName, DefinitionId requiredSlot)
        {
            InstanceId = instanceId;
            Name = name;
            Quantity = quantity;
            IsEquipped = isEquipped;
            EquippedBy = equippedBy;
            SlotName = slotName;
            RequiredSlot = requiredSlot;
        }

        public RuntimeEntityId InstanceId { get; }
        public TextKey Name { get; }
        public int Quantity { get; }
        public bool IsEquipped { get; }
        public RuntimeEntityId? EquippedBy { get; }
        public TextKey? SlotName { get; }

        /// <summary>The slot this item's own ItemDefinition declares (DefinitionId.None if it isn't
        /// equippable at all) -- what a caller must pass back to InventoryPresenter.Equip. Populated
        /// regardless of whether the item is currently equipped, unlike SlotName (display-only, only set once
        /// actually worn).</summary>
        public DefinitionId RequiredSlot { get; }
    }
}
