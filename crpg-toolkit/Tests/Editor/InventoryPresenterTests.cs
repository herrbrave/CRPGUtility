using CRPG.Actors;
using CRPG.Core;
using CRPG.Inventory;
using CRPG.Rules;
using CRPG.UI;
using NUnit.Framework;

namespace CRPG.Tests.EditMode
{
    public class InventoryPresenterTests
    {
        private static (InventoryPresenter presenter, PartyInventoryState inventory, RuntimeEntityId character, ItemDefinition sword, EquipmentSlotDefinition mainHand)
            BuildScenario()
        {
            var rulesDatabase = new RulesDatabase();
            var registry = new CharacterRegistry();
            var inventory = new PartyInventoryState();

            var mainHand = TestDefinitionFactory.CreateEquipmentSlotDefinition("slot-mainhand", "Main Hand");
            var sword = TestDefinitionFactory.CreateItemDefinition("item-sword", "Longsword", equipmentSlotId: mainHand.Id);
            rulesDatabase.Register(mainHand);
            rulesDatabase.Register(sword);

            var character = RuntimeEntityId.NewId();
            registry.Register(new CharacterState(character));

            var equipmentService = new EquipmentService(registry, rulesDatabase, inventory);
            var presenter = new InventoryPresenter(inventory, rulesDatabase, equipmentService);
            return (presenter, inventory, character, sword, mainHand);
        }

        [Test]
        public void EquipMovesAnEligibleItemIntoTheRequestedSlot()
        {
            var (presenter, inventory, character, sword, mainHand) = BuildScenario();
            var instance = new ItemInstance(RuntimeEntityId.NewId(), sword.Id);
            inventory.AddItem(instance);

            var result = presenter.Equip(instance.InstanceId, character, mainHand.Id);

            Assert.IsTrue(result.Success);
            Assert.IsTrue(inventory.GetLocation(instance.InstanceId).IsEquipped);
        }

        [Test]
        public void EquipReportsWrongSlotWithoutThrowing()
        {
            var (presenter, inventory, character, sword, _) = BuildScenario();
            var instance = new ItemInstance(RuntimeEntityId.NewId(), sword.Id);
            inventory.AddItem(instance);
            var wrongSlot = TestDefinitionFactory.CreateEquipmentSlotDefinition("slot-offhand", "Off Hand");

            var result = presenter.Equip(instance.InstanceId, character, wrongSlot.Id);

            Assert.AreEqual(InventoryOperationOutcome.WrongSlot, result.Outcome);
            Assert.IsFalse(inventory.GetLocation(instance.InstanceId).IsEquipped);
        }

        [Test]
        public void EquipReportsItemNotFoundForAnUnknownInstance()
        {
            var (presenter, _, character, _, mainHand) = BuildScenario();

            var result = presenter.Equip(RuntimeEntityId.NewId(), character, mainHand.Id);

            Assert.AreEqual(InventoryOperationOutcome.ItemNotFound, result.Outcome);
        }

        [Test]
        public void UnequipMovesTheItemBackToSharedInventory()
        {
            var (presenter, inventory, character, sword, mainHand) = BuildScenario();
            var instance = new ItemInstance(RuntimeEntityId.NewId(), sword.Id);
            inventory.AddItem(instance);
            presenter.Equip(instance.InstanceId, character, mainHand.Id);

            var result = presenter.Unequip(instance.InstanceId);

            Assert.IsTrue(result.Success);
            Assert.IsFalse(inventory.GetLocation(instance.InstanceId).IsEquipped);
        }

        [Test]
        public void DropRemovesASharedItem()
        {
            var (presenter, inventory, _, sword, _) = BuildScenario();
            var instance = new ItemInstance(RuntimeEntityId.NewId(), sword.Id);
            inventory.AddItem(instance);

            var result = presenter.Drop(instance.InstanceId);

            Assert.IsTrue(result.Success);
            Assert.IsFalse(inventory.TryGetItem(instance.InstanceId, out _));
        }

        [Test]
        public void DropRejectsAnEquippedItemWithoutRemovingIt()
        {
            var (presenter, inventory, character, sword, mainHand) = BuildScenario();
            var instance = new ItemInstance(RuntimeEntityId.NewId(), sword.Id);
            inventory.AddItem(instance);
            presenter.Equip(instance.InstanceId, character, mainHand.Id);

            var result = presenter.Drop(instance.InstanceId);

            Assert.AreEqual(InventoryOperationOutcome.ItemEquipped, result.Outcome);
            Assert.IsTrue(inventory.TryGetItem(instance.InstanceId, out _)); // still there
        }

        [Test]
        public void UseAlwaysReportsNotSupportedYet()
        {
            var (presenter, inventory, character, sword, _) = BuildScenario();
            var instance = new ItemInstance(RuntimeEntityId.NewId(), sword.Id);
            inventory.AddItem(instance);

            var result = presenter.Use(instance.InstanceId, character);

            Assert.AreEqual(InventoryOperationOutcome.NotSupportedYet, result.Outcome);
        }

        [Test]
        public void GetSharedItemsExposesTheRequiredSlotForEquippableItems()
        {
            var (presenter, inventory, _, sword, mainHand) = BuildScenario();
            var instance = new ItemInstance(RuntimeEntityId.NewId(), sword.Id);
            inventory.AddItem(instance);

            var shared = presenter.GetSharedItems();

            Assert.AreEqual(1, shared.Count);
            Assert.AreEqual(mainHand.Id, shared[0].RequiredSlot);
            Assert.IsFalse(shared[0].IsEquipped);
        }

        [Test]
        public void RebindSwapsInANewInventoryAndEquipmentService()
        {
            var (presenter, _, _, sword, _) = BuildScenario();

            var newRulesDatabase = new RulesDatabase();
            newRulesDatabase.Register(sword);
            var newInventory = new PartyInventoryState();
            var newInstance = new ItemInstance(RuntimeEntityId.NewId(), sword.Id);
            newInventory.AddItem(newInstance);
            var newEquipmentService = new EquipmentService(new CharacterRegistry(), newRulesDatabase, newInventory);

            presenter.Rebind(newInventory, newRulesDatabase, newEquipmentService);
            var shared = presenter.GetSharedItems();

            Assert.AreEqual(1, shared.Count);
            Assert.AreEqual(newInstance.InstanceId, shared[0].InstanceId);
        }
    }
}
