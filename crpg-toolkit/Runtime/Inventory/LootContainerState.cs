using System.Collections.Generic;

namespace CRPG.Inventory
{
    /// <summary>A chest, corpse, or other world container holding items not yet taken by the party.</summary>
    public sealed class LootContainerState
    {
        private readonly List<ItemInstance> items = new();

        public IReadOnlyList<ItemInstance> Items => items;
        public bool IsEmpty => items.Count == 0;

        public void AddItem(ItemInstance item) => items.Add(item);
        public bool RemoveItem(ItemInstance item) => items.Remove(item);
    }
}
