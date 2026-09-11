using System.Collections.Generic;
using CRPG.Actors;
using CRPG.Core;
using CRPG.Inventory;
using NUnit.Framework;

namespace CRPG.Tests.EditMode
{
    public class EquipmentServiceTests
    {
        private static readonly DefinitionId MainHand = new("slot-mainhand");
        private static readonly DefinitionId OffHand = new("slot-offhand");

        [Test]
        public void EquipMovesItemToTheRequestedSlot()
        {
            var rulesDatabase = new RulesDatabase();
            var sword = TestDefinitionFactory.CreateItemDefinition("item-sword", "Longsword", equipmentSlotId: MainHand);
            rulesDatabase.Register(sword);

            var inventory = new PartyInventoryState();
            var swordInstance = new ItemInstance(RuntimeEntityId.NewId(), sword.Id);
            inventory.AddItem(swordInstance);
            var character = RuntimeEntityId.NewId();

            var service = new EquipmentService(new CharacterRegistry(), rulesDatabase, inventory);
            service.Equip(swordInstance.InstanceId, character, MainHand);

            var location = inventory.GetLocation(swordInstance.InstanceId);
            Assert.IsTrue(location.IsEquipped);
            Assert.AreEqual(character, location.Character);
            Assert.AreEqual(MainHand, location.Slot);
        }

        [Test]
        public void EquippingIntoAnOccupiedSlotDisplacesThePreviousItemToSharedInventory()
        {
            var rulesDatabase = new RulesDatabase();
            var dagger = TestDefinitionFactory.CreateItemDefinition("item-dagger", "Dagger", equipmentSlotId: MainHand);
            var sword = TestDefinitionFactory.CreateItemDefinition("item-sword", "Longsword", equipmentSlotId: MainHand);
            rulesDatabase.Register(dagger);
            rulesDatabase.Register(sword);

            var inventory = new PartyInventoryState();
            var daggerInstance = new ItemInstance(RuntimeEntityId.NewId(), dagger.Id);
            var swordInstance = new ItemInstance(RuntimeEntityId.NewId(), sword.Id);
            inventory.AddItem(daggerInstance);
            inventory.AddItem(swordInstance);
            var character = RuntimeEntityId.NewId();

            var service = new EquipmentService(new CharacterRegistry(), rulesDatabase, inventory);
            service.Equip(daggerInstance.InstanceId, character, MainHand);

            var displaced = service.Equip(swordInstance.InstanceId, character, MainHand);

            Assert.AreSame(daggerInstance, displaced);
            Assert.IsFalse(inventory.GetLocation(daggerInstance.InstanceId).IsEquipped);
            Assert.AreEqual(character, inventory.GetLocation(swordInstance.InstanceId).Character);
        }

        [Test]
        public void EquippingIntoTheWrongSlotThrows()
        {
            var rulesDatabase = new RulesDatabase();
            var sword = TestDefinitionFactory.CreateItemDefinition("item-sword", "Longsword", equipmentSlotId: MainHand);
            rulesDatabase.Register(sword);

            var inventory = new PartyInventoryState();
            var swordInstance = new ItemInstance(RuntimeEntityId.NewId(), sword.Id);
            inventory.AddItem(swordInstance);

            var service = new EquipmentService(new CharacterRegistry(), rulesDatabase, inventory);

            Assert.Throws<System.InvalidOperationException>(() =>
                service.Equip(swordInstance.InstanceId, RuntimeEntityId.NewId(), OffHand));
        }

        [Test]
        public void EquippingANonEquippableItemThrows()
        {
            var rulesDatabase = new RulesDatabase();
            var potion = TestDefinitionFactory.CreateItemDefinition("item-potion", "Healing Potion");
            rulesDatabase.Register(potion);

            var inventory = new PartyInventoryState();
            var potionInstance = new ItemInstance(RuntimeEntityId.NewId(), potion.Id);
            inventory.AddItem(potionInstance);

            var service = new EquipmentService(new CharacterRegistry(), rulesDatabase, inventory);

            Assert.Throws<System.InvalidOperationException>(() =>
                service.Equip(potionInstance.InstanceId, RuntimeEntityId.NewId(), MainHand));
        }

        [Test]
        public void UnequipMovesItemBackToSharedInventory()
        {
            var rulesDatabase = new RulesDatabase();
            var sword = TestDefinitionFactory.CreateItemDefinition("item-sword", "Longsword", equipmentSlotId: MainHand);
            rulesDatabase.Register(sword);

            var inventory = new PartyInventoryState();
            var swordInstance = new ItemInstance(RuntimeEntityId.NewId(), sword.Id);
            inventory.AddItem(swordInstance);
            var service = new EquipmentService(new CharacterRegistry(), rulesDatabase, inventory);
            service.Equip(swordInstance.InstanceId, RuntimeEntityId.NewId(), MainHand);

            service.Unequip(swordInstance.InstanceId);

            Assert.IsFalse(inventory.GetLocation(swordInstance.InstanceId).IsEquipped);
        }

        [Test]
        public void EquippingGrantsTheItemsEquippedFeature()
        {
            var rulesDatabase = new RulesDatabase();
            var armorFeature = TestDefinitionFactory.CreateFeatureDefinition("feature-chain-shirt", "Chain Shirt Armor");
            rulesDatabase.Register(armorFeature);
            var chainShirt = TestDefinitionFactory.CreateItemDefinition("item-chain-shirt", "Chain Shirt", equipmentSlotId: MainHand, equippedFeatureId: armorFeature.Id);
            rulesDatabase.Register(chainShirt);

            var inventory = new PartyInventoryState();
            var armorInstance = new ItemInstance(RuntimeEntityId.NewId(), chainShirt.Id);
            inventory.AddItem(armorInstance);

            var registry = new CharacterRegistry();
            var character = new CharacterState(RuntimeEntityId.NewId());
            registry.Register(character);

            var service = new EquipmentService(registry, rulesDatabase, inventory);
            service.Equip(armorInstance.InstanceId, character.Id, MainHand);

            Assert.IsTrue(character.HasFeature(armorFeature.Id));
        }

        [Test]
        public void UnequippingRemovesTheItemsEquippedFeature()
        {
            var rulesDatabase = new RulesDatabase();
            var armorFeature = TestDefinitionFactory.CreateFeatureDefinition("feature-chain-shirt", "Chain Shirt Armor");
            rulesDatabase.Register(armorFeature);
            var chainShirt = TestDefinitionFactory.CreateItemDefinition("item-chain-shirt", "Chain Shirt", equipmentSlotId: MainHand, equippedFeatureId: armorFeature.Id);
            rulesDatabase.Register(chainShirt);

            var inventory = new PartyInventoryState();
            var armorInstance = new ItemInstance(RuntimeEntityId.NewId(), chainShirt.Id);
            inventory.AddItem(armorInstance);

            var registry = new CharacterRegistry();
            var character = new CharacterState(RuntimeEntityId.NewId());
            registry.Register(character);

            var service = new EquipmentService(registry, rulesDatabase, inventory);
            service.Equip(armorInstance.InstanceId, character.Id, MainHand);
            service.Unequip(armorInstance.InstanceId);

            Assert.IsFalse(character.HasFeature(armorFeature.Id));
        }

        [Test]
        public void EquippingIntoAnOccupiedSlotRemovesTheDisplacedItemsFeatureAndGrantsTheNewOne()
        {
            var rulesDatabase = new RulesDatabase();
            var daggerFeature = TestDefinitionFactory.CreateFeatureDefinition("feature-dagger", "Dagger Bonus");
            var swordFeature = TestDefinitionFactory.CreateFeatureDefinition("feature-sword", "Sword Bonus");
            rulesDatabase.Register(daggerFeature);
            rulesDatabase.Register(swordFeature);
            var dagger = TestDefinitionFactory.CreateItemDefinition("item-dagger", "Dagger", equipmentSlotId: MainHand, equippedFeatureId: daggerFeature.Id);
            var sword = TestDefinitionFactory.CreateItemDefinition("item-sword", "Longsword", equipmentSlotId: MainHand, equippedFeatureId: swordFeature.Id);
            rulesDatabase.Register(dagger);
            rulesDatabase.Register(sword);

            var inventory = new PartyInventoryState();
            var daggerInstance = new ItemInstance(RuntimeEntityId.NewId(), dagger.Id);
            var swordInstance = new ItemInstance(RuntimeEntityId.NewId(), sword.Id);
            inventory.AddItem(daggerInstance);
            inventory.AddItem(swordInstance);

            var registry = new CharacterRegistry();
            var character = new CharacterState(RuntimeEntityId.NewId());
            registry.Register(character);

            var service = new EquipmentService(registry, rulesDatabase, inventory);
            service.Equip(daggerInstance.InstanceId, character.Id, MainHand);
            service.Equip(swordInstance.InstanceId, character.Id, MainHand);

            Assert.IsFalse(character.HasFeature(daggerFeature.Id));
            Assert.IsTrue(character.HasFeature(swordFeature.Id));
        }

        [Test]
        public void EquippingPublishesItemEquippedWithTheItemAndSlot()
        {
            var rulesDatabase = new RulesDatabase();
            var sword = TestDefinitionFactory.CreateItemDefinition("item-sword", "Longsword", equipmentSlotId: MainHand);
            rulesDatabase.Register(sword);

            var inventory = new PartyInventoryState();
            var swordInstance = new ItemInstance(RuntimeEntityId.NewId(), sword.Id);
            inventory.AddItem(swordInstance);
            var character = RuntimeEntityId.NewId();

            var eventBus = new GameEventBus();
            GameEventContext received = null;
            eventBus.Subscribe(GameEvent.ItemEquipped, context => received = context);

            var service = new EquipmentService(new CharacterRegistry(), rulesDatabase, inventory, eventBus);
            service.Equip(swordInstance.InstanceId, character, MainHand);

            Assert.IsNotNull(received);
            Assert.AreEqual(character, received.Target);
            Assert.AreEqual(sword.Id, received.Data["ItemDefinitionId"]);
            Assert.AreEqual(MainHand, received.Data["SlotId"]);
        }

        [Test]
        public void UnequippingPublishesItemUnequipped()
        {
            var rulesDatabase = new RulesDatabase();
            var sword = TestDefinitionFactory.CreateItemDefinition("item-sword", "Longsword", equipmentSlotId: MainHand);
            rulesDatabase.Register(sword);

            var inventory = new PartyInventoryState();
            var swordInstance = new ItemInstance(RuntimeEntityId.NewId(), sword.Id);
            inventory.AddItem(swordInstance);
            var character = RuntimeEntityId.NewId();

            var eventBus = new GameEventBus();
            var receivedEvents = new List<GameEvent>();
            eventBus.Subscribe(GameEvent.ItemUnequipped, context => receivedEvents.Add(context.Event));

            var service = new EquipmentService(new CharacterRegistry(), rulesDatabase, inventory, eventBus);
            service.Equip(swordInstance.InstanceId, character, MainHand);
            service.Unequip(swordInstance.InstanceId);

            Assert.AreEqual(1, receivedEvents.Count);
        }

        [Test]
        public void EquippingIntoAnOccupiedSlotPublishesItemUnequippedForTheDisplacedItem()
        {
            var rulesDatabase = new RulesDatabase();
            var dagger = TestDefinitionFactory.CreateItemDefinition("item-dagger", "Dagger", equipmentSlotId: MainHand);
            var sword = TestDefinitionFactory.CreateItemDefinition("item-sword", "Longsword", equipmentSlotId: MainHand);
            rulesDatabase.Register(dagger);
            rulesDatabase.Register(sword);

            var inventory = new PartyInventoryState();
            var daggerInstance = new ItemInstance(RuntimeEntityId.NewId(), dagger.Id);
            var swordInstance = new ItemInstance(RuntimeEntityId.NewId(), sword.Id);
            inventory.AddItem(daggerInstance);
            inventory.AddItem(swordInstance);
            var character = RuntimeEntityId.NewId();

            var eventBus = new GameEventBus();
            DefinitionId? unequippedItemId = null;
            eventBus.Subscribe(GameEvent.ItemUnequipped, context => unequippedItemId = (DefinitionId)context.Data["ItemDefinitionId"]);

            var service = new EquipmentService(new CharacterRegistry(), rulesDatabase, inventory, eventBus);
            service.Equip(daggerInstance.InstanceId, character, MainHand);
            service.Equip(swordInstance.InstanceId, character, MainHand);

            Assert.AreEqual(dagger.Id, unequippedItemId);
        }
    }
}
