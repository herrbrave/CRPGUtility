using CRPG.Core;
using CRPG.Inventory;
using NUnit.Framework;

namespace CRPG.Tests.EditMode
{
    public class LootServiceTests
    {
        [Test]
        public void TakeMovesOneItemIntoSharedInventoryAndOutOfTheContainer()
        {
            var inventory = new PartyInventoryState();
            var container = new LootContainerState();
            var gold = new ItemInstance(RuntimeEntityId.NewId(), new DefinitionId("item-gold"));
            container.AddItem(gold);
            var service = new LootService(inventory);

            service.Take(container, gold);

            CollectionAssert.DoesNotContain(container.Items, gold);
            Assert.IsTrue(inventory.TryGetItem(gold.InstanceId, out _));
            Assert.IsFalse(inventory.GetLocation(gold.InstanceId).IsEquipped);
        }

        [Test]
        public void TakeAllEmptiesTheContainerIntoSharedInventory()
        {
            var inventory = new PartyInventoryState();
            var container = new LootContainerState();
            var gold = new ItemInstance(RuntimeEntityId.NewId(), new DefinitionId("item-gold"));
            var potion = new ItemInstance(RuntimeEntityId.NewId(), new DefinitionId("item-potion"));
            container.AddItem(gold);
            container.AddItem(potion);
            var service = new LootService(inventory);

            service.TakeAll(container);

            Assert.IsTrue(container.IsEmpty);
            Assert.IsTrue(inventory.TryGetItem(gold.InstanceId, out _));
            Assert.IsTrue(inventory.TryGetItem(potion.InstanceId, out _));
        }
    }
}
