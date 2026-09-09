using System;
using CRPG.Core;

namespace CRPG.Inventory
{
    /// <summary>Where one item instance currently is: the shared party inventory, or equipped by a specific
    /// character in a specific slot. There's no per-character backpack -- everything not equipped is shared.</summary>
    public readonly struct ItemLocation : IEquatable<ItemLocation>
    {
        private ItemLocation(RuntimeEntityId character, DefinitionId slot)
        {
            Character = character;
            Slot = slot;
        }

        /// <summary>Also the default value of this struct, so `default(ItemLocation)` already means shared.</summary>
        public static readonly ItemLocation SharedInventory = new(RuntimeEntityId.None, DefinitionId.None);

        public static ItemLocation Equipped(RuntimeEntityId character, DefinitionId slot) => new(character, slot);

        public RuntimeEntityId Character { get; }
        public DefinitionId Slot { get; }
        public bool IsEquipped => Character.IsValid;

        public bool Equals(ItemLocation other) => Character == other.Character && Slot == other.Slot;
        public override bool Equals(object obj) => obj is ItemLocation other && Equals(other);
        public override int GetHashCode() => HashCode.Combine(Character, Slot);
    }
}
