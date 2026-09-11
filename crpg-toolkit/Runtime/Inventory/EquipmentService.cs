using System;
using CRPG.Actors;
using CRPG.Core;

namespace CRPG.Inventory
{
    /// <summary>
    /// Validated equip/unequip against a <see cref="PartyInventoryState"/>: checks the item is actually
    /// equippable and fits the requested slot, and displaces whatever already occupied that slot back to the
    /// shared inventory rather than silently overwriting it. Equipping/unequipping grants/removes the item's
    /// <see cref="ItemDefinition.EquippedFeatureId"/> so its passive modifiers (e.g. armor's AC bonus) become
    /// active exactly while worn -- closes the gap this class's own doc comment used to flag as unmodeled.
    /// </summary>
    public sealed class EquipmentService
    {
        private readonly ICharacterRegistry characters;
        private readonly IRulesDatabase rulesDatabase;
        private readonly PartyInventoryState inventory;
        private readonly IGameEventBus eventBus;

        public EquipmentService(ICharacterRegistry characters, IRulesDatabase rulesDatabase, PartyInventoryState inventory, IGameEventBus eventBus = null)
        {
            this.characters = characters;
            this.rulesDatabase = rulesDatabase;
            this.inventory = inventory;
            this.eventBus = eventBus;
        }

        /// <returns>The item displaced out of the slot back to the shared inventory, if any.</returns>
        public ItemInstance Equip(RuntimeEntityId instanceId, RuntimeEntityId character, DefinitionId slot)
        {
            if (!inventory.TryGetItem(instanceId, out var item))
                throw new InvalidOperationException($"No item instance '{instanceId}' exists in the party inventory.");

            var definition = rulesDatabase.Resolve<ItemDefinition>(item.DefinitionId);
            if (!definition.IsEquippable)
                throw new InvalidOperationException($"'{definition.DisplayName}' cannot be equipped.");
            if (definition.EquipmentSlotId != slot)
                throw new InvalidOperationException($"'{definition.DisplayName}' does not fit the requested slot.");

            ItemInstance displaced = null;
            if (inventory.TryGetEquippedItem(character, slot, out var occupant) && occupant.InstanceId != instanceId)
            {
                inventory.SetLocation(occupant.InstanceId, ItemLocation.SharedInventory);
                RemoveEquippedFeature(character, occupant);
                PublishItemEvent(GameEvent.ItemUnequipped, character, occupant.DefinitionId, slot);
                displaced = occupant;
            }

            inventory.SetLocation(instanceId, ItemLocation.Equipped(character, slot));
            GrantEquippedFeature(character, item, definition);
            PublishItemEvent(GameEvent.ItemEquipped, character, item.DefinitionId, slot);
            return displaced;
        }

        public void Unequip(RuntimeEntityId instanceId)
        {
            if (!inventory.TryGetItem(instanceId, out var item))
                throw new InvalidOperationException($"No item instance '{instanceId}' exists in the party inventory.");

            var previousLocation = inventory.GetLocation(instanceId);
            inventory.SetLocation(instanceId, ItemLocation.SharedInventory);
            if (!previousLocation.IsEquipped) return;

            RemoveEquippedFeature(previousLocation.Character, item);
            PublishItemEvent(GameEvent.ItemUnequipped, previousLocation.Character, item.DefinitionId, previousLocation.Slot);
        }

        private void GrantEquippedFeature(RuntimeEntityId character, ItemInstance item, ItemDefinition definition)
        {
            if (!definition.EquippedFeatureId.IsValid) return;
            if (characters.TryGetCharacter(character, out var characterState)) characterState.GrantFeature(definition.EquippedFeatureId);
        }

        private void RemoveEquippedFeature(RuntimeEntityId character, ItemInstance item)
        {
            if (!rulesDatabase.TryResolve<ItemDefinition>(item.DefinitionId, out var definition) || !definition.EquippedFeatureId.IsValid) return;
            if (characters.TryGetCharacter(character, out var characterState)) characterState.RemoveFeature(definition.EquippedFeatureId);
        }

        private void PublishItemEvent(GameEvent gameEvent, RuntimeEntityId character, DefinitionId itemDefinitionId, DefinitionId slot)
        {
            var context = new GameEventContext(gameEvent, character, character);
            context.Data["ItemDefinitionId"] = itemDefinitionId;
            context.Data["SlotId"] = slot;
            eventBus?.Publish(context);
        }
    }
}
