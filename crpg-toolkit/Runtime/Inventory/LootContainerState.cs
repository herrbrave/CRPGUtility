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

        /// <summary>Task 6.0: removes every item -- for SaveService.RestoreLootContainers, which must REPLACE
        /// whatever a fresh GetOrCreateLootContainer(seedIfNew:...) just populated (a same-id container is
        /// always "new" to a just-loaded session, so it always re-seeds from InitialItems first), not layer the
        /// saved items on top -- each restored ItemInstance carries its own distinct saved InstanceId, so
        /// without this a load would leave both the stale reseeded items AND the real restored ones present.</summary>
        public void ClearItems() => items.Clear();
    }
}
