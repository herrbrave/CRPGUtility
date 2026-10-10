using System.Collections.Generic;
using System.Linq;
using CRPG.Core;

namespace CRPG.Inventory
{
    /// <summary>
    /// One inventory for the whole party, not four backpacks. Every owned item lives here, tracked by
    /// location (shared, or equipped by a character) rather than belonging to a per-character container --
    /// looting never needs a "send to character" step since there's nowhere else for it to go.
    /// </summary>
    public sealed class PartyInventoryState
    {
        private readonly Dictionary<RuntimeEntityId, ItemInstance> itemsById = new();
        private readonly Dictionary<RuntimeEntityId, ItemLocation> locationsById = new();

        /// <param name="location">Defaults to the shared inventory.</param>
        public void AddItem(ItemInstance item, ItemLocation location = default)
        {
            itemsById[item.InstanceId] = item;
            locationsById[item.InstanceId] = location;
        }

        public void RemoveItem(RuntimeEntityId instanceId)
        {
            itemsById.Remove(instanceId);
            locationsById.Remove(instanceId);
        }

        public bool TryGetItem(RuntimeEntityId instanceId, out ItemInstance item) => itemsById.TryGetValue(instanceId, out item);

        public ItemLocation GetLocation(RuntimeEntityId instanceId) =>
            locationsById.TryGetValue(instanceId, out var location) ? location : ItemLocation.SharedInventory;

        /// <summary>Only <see cref="EquipmentService"/> should normally call this -- it validates slot
        /// compatibility and handles displacing whatever already occupied the slot.</summary>
        internal void SetLocation(RuntimeEntityId instanceId, ItemLocation location) => locationsById[instanceId] = location;

        public IEnumerable<ItemInstance> AllItems => itemsById.Values;

        /// <summary>How many of an item the party carries in the SHARED inventory (equipped copies do not count), summed across every stack.</summary>
        public int CountShared(DefinitionId definitionId)
        {
            var total = 0;
            foreach (var item in ItemsInSharedInventory)
            {
                if (item.DefinitionId == definitionId) total += item.Quantity;
            }

            return total;
        }

        /// <summary>Removes <paramref name="amount"/> of an item from the shared inventory, taking from as many stacks as needed and deleting emptied ones. All or nothing: false (and nothing changes) when the party
        /// does not have that many, or the amount is negative; removing zero is a no-op that succeeds.</summary>
        public bool TryRemoveShared(DefinitionId definitionId, int amount)
        {
            if (amount < 0) return false;
            if (amount == 0) return true;
            if (CountShared(definitionId) < amount) return false;

            var remaining = amount;
            foreach (var item in ItemsInSharedInventory.Where(i => i.DefinitionId == definitionId).ToList())
            {
                if (remaining <= 0) break;

                var take = System.Math.Min(item.Quantity, remaining);
                item.Quantity -= take;
                remaining -= take;
                if (item.Quantity <= 0) RemoveItem(item.InstanceId);
            }

            return true;
        }

        public IEnumerable<ItemInstance> ItemsInSharedInventory =>
            itemsById.Values.Where(item => !GetLocation(item.InstanceId).IsEquipped);

        public IEnumerable<ItemInstance> ItemsEquippedBy(RuntimeEntityId character) =>
            itemsById.Values.Where(item => GetLocation(item.InstanceId).Character == character);

        public bool TryGetEquippedItem(RuntimeEntityId character, DefinitionId slot, out ItemInstance item)
        {
            foreach (var candidate in itemsById.Values)
            {
                var location = GetLocation(candidate.InstanceId);
                if (location.Character == character && location.Slot == slot)
                {
                    item = candidate;
                    return true;
                }
            }

            item = null;
            return false;
        }
    }
}
