using CRPG.Core;
using CRPG.Inventory;
using NUnit.Framework;

namespace CRPG.Tests.EditMode
{
    public class PartyInventoryStateTests
    {
        [Test]
        public void AddedItemDefaultsToSharedInventory()
        {
            var inventory = new PartyInventoryState();
            var potion = new ItemInstance(RuntimeEntityId.NewId(), new DefinitionId("item-potion"));

            inventory.AddItem(potion);

            Assert.IsFalse(inventory.GetLocation(potion.InstanceId).IsEquipped);
            CollectionAssert.Contains(inventory.ItemsInSharedInventory, potion);
        }

        [Test]
        public void RemoveItemDropsItFromInventoryAndLocation()
        {
            var inventory = new PartyInventoryState();
            var potion = new ItemInstance(RuntimeEntityId.NewId(), new DefinitionId("item-potion"));
            inventory.AddItem(potion);

            inventory.RemoveItem(potion.InstanceId);

            Assert.IsFalse(inventory.TryGetItem(potion.InstanceId, out _));
        }

        [Test]
        public void TryGetEquippedItemFindsItemInSpecificSlot()
        {
            var inventory = new PartyInventoryState();
            var character = RuntimeEntityId.NewId();
            var slot = new DefinitionId("slot-mainhand");
            var sword = new ItemInstance(RuntimeEntityId.NewId(), new DefinitionId("item-sword"));
            inventory.AddItem(sword, ItemLocation.Equipped(character, slot));

            var found = inventory.TryGetEquippedItem(character, slot, out var item);

            Assert.IsTrue(found);
            Assert.AreSame(sword, item);
        }
    }
}
