using System.Linq;

namespace CRPG.Inventory
{
    /// <summary>Take / Take All from a loot container into the shared party inventory. There's no "send to
    /// character" action -- the inventory is already shared, so taking an item is the only step needed.</summary>
    public sealed class LootService
    {
        private readonly PartyInventoryState inventory;

        public LootService(PartyInventoryState inventory)
        {
            this.inventory = inventory;
        }

        public void Take(LootContainerState container, ItemInstance item)
        {
            if (!container.RemoveItem(item)) return;
            inventory.AddItem(item, ItemLocation.SharedInventory);
        }

        public void TakeAll(LootContainerState container)
        {
            foreach (var item in container.Items.ToList()) Take(container, item);
        }
    }
}
