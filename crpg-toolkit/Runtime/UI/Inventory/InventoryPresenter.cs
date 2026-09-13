using System.Collections.Generic;
using CRPG.Core;
using CRPG.Inventory;

namespace CRPG.UI
{
    /// <summary>
    /// Pure data/command layer for equip/unequip/drop/use against the party's shared inventory -- distinct
    /// from StorePresenter (buy/sell against a merchant's stock).
    /// </summary>
    public sealed class InventoryPresenter
    {
        private readonly IGameplayCueSink cueSink;
        private PartyInventoryState inventory;
        private IRulesDatabase rulesDatabase;
        private EquipmentService equipmentService;

        public InventoryPresenter(PartyInventoryState inventory, IRulesDatabase rulesDatabase, EquipmentService equipmentService, IGameplayCueSink cueSink = null)
        {
            this.inventory = inventory;
            this.rulesDatabase = rulesDatabase;
            this.equipmentService = equipmentService;
            this.cueSink = cueSink;
        }

        /// <summary>Task 3.6: called by the composition root after a successful load -- a plain method rather
        /// than a shared ISessionConsumer interface, for the same CRPG.UI/CRPG.Gameplay layering reason
        /// PartyPresenter.Rebind documents.</summary>
        public void Rebind(PartyInventoryState inventory, IRulesDatabase rulesDatabase, EquipmentService equipmentService)
        {
            this.inventory = inventory;
            this.rulesDatabase = rulesDatabase;
            this.equipmentService = equipmentService;
        }

        public IReadOnlyList<InventoryItemSummary> GetSharedItems()
        {
            var summaries = new List<InventoryItemSummary>();
            foreach (var item in inventory.ItemsInSharedInventory) summaries.Add(BuildSummary(item));
            return summaries;
        }

        public IReadOnlyList<InventoryItemSummary> GetEquippedItems(RuntimeEntityId character)
        {
            var summaries = new List<InventoryItemSummary>();
            foreach (var item in inventory.ItemsEquippedBy(character)) summaries.Add(BuildSummary(item));
            return summaries;
        }

        /// <summary>Whether this item declares a GrantedAbilityId -- "has a use ability," NOT "is currently
        /// executable." The View shows a disabled Use button with the NotSupportedYet explanation, not a live
        /// one (Use itself always returns NotSupportedYet this task -- real ability-execution plumbing is Phase
        /// 4 territory).</summary>
        public bool HasUsableAbility(RuntimeEntityId instanceId)
        {
            if (!inventory.TryGetItem(instanceId, out var item)) return false;
            return rulesDatabase.TryResolve<ItemDefinition>(item.DefinitionId, out var definition) && definition.GrantedAbilityId.IsValid;
        }

        /// <summary>Pre-validates the same checks EquipmentService.Equip performs internally so the common
        /// failure path avoids the exception; the try/catch around the actual call is a last-resort net for a
        /// TOCTOU race, catching InvalidOperationException specifically -- what EquipmentService actually
        /// throws -- not a bare catch(Exception), since an unexpected exception type is a real bug that must
        /// not be silently swallowed into "nothing happened."</summary>
        public InventoryOperationResult Equip(RuntimeEntityId instanceId, RuntimeEntityId character, DefinitionId slot)
        {
            if (!inventory.TryGetItem(instanceId, out var item)) return InventoryOperationResult.Fail(InventoryOperationOutcome.ItemNotFound);
            if (!rulesDatabase.TryResolve<ItemDefinition>(item.DefinitionId, out var definition)) return InventoryOperationResult.Fail(InventoryOperationOutcome.ItemNotFound);
            if (!definition.IsEquippable) return InventoryOperationResult.Fail(InventoryOperationOutcome.NotEquippable);
            if (definition.EquipmentSlotId != slot) return InventoryOperationResult.Fail(InventoryOperationOutcome.WrongSlot);

            try
            {
                equipmentService.Equip(instanceId, character, slot);
                cueSink?.Raise(new GameplayCue("inventory.equip", character, item.DefinitionId));
                return InventoryOperationResult.Ok();
            }
            catch (System.InvalidOperationException)
            {
                return InventoryOperationResult.Fail(InventoryOperationOutcome.ItemNotFound);
            }
        }

        public InventoryOperationResult Unequip(RuntimeEntityId instanceId)
        {
            if (!inventory.TryGetItem(instanceId, out var item)) return InventoryOperationResult.Fail(InventoryOperationOutcome.ItemNotFound);
            var location = inventory.GetLocation(instanceId);
            var character = location.Character;

            try
            {
                equipmentService.Unequip(instanceId);
                cueSink?.Raise(new GameplayCue("inventory.unequip", character, item.DefinitionId));
                return InventoryOperationResult.Ok();
            }
            catch (System.InvalidOperationException)
            {
                return InventoryOperationResult.Fail(InventoryOperationOutcome.ItemNotFound);
            }
        }

        /// <summary>Rejects an equipped item outright rather than silently unequipping-then-removing --
        /// dropping is a separate, explicit, single-purpose action from unequipping (Task 3.6 scope call).</summary>
        public InventoryOperationResult Drop(RuntimeEntityId instanceId)
        {
            if (!inventory.TryGetItem(instanceId, out var item)) return InventoryOperationResult.Fail(InventoryOperationOutcome.ItemNotFound);
            if (inventory.GetLocation(instanceId).IsEquipped) return InventoryOperationResult.Fail(InventoryOperationOutcome.ItemEquipped);

            inventory.RemoveItem(instanceId);
            cueSink?.Raise(new GameplayCue("inventory.drop", null, item.DefinitionId));
            return InventoryOperationResult.Ok();
        }

        /// <summary>Confirmed scope call (Task 3.6): unconditionally NotSupportedYet -- real ability-execution
        /// plumbing for GrantedAbilityId doesn't exist yet and building it is Phase 4/Combat territory, not
        /// this task's UI-contract work.</summary>
        public InventoryOperationResult Use(RuntimeEntityId instanceId, RuntimeEntityId character) =>
            InventoryOperationResult.Fail(InventoryOperationOutcome.NotSupportedYet);

        private InventoryItemSummary BuildSummary(ItemInstance item)
        {
            var found = rulesDatabase.TryResolve<ItemDefinition>(item.DefinitionId, out var definition);
            var name = found ? definition.DisplayName : item.DefinitionId.ToString();
            var requiredSlot = found && definition.IsEquippable ? definition.EquipmentSlotId : default;
            var location = inventory.GetLocation(item.InstanceId);

            TextKey? slotName = null;
            if (location.IsEquipped)
            {
                var resolvedSlotName = rulesDatabase.TryResolve<EquipmentSlotDefinition>(location.Slot, out var slot) ? slot.DisplayName : location.Slot.ToString();
                slotName = TextKey.Literal(resolvedSlotName);
            }

            return new InventoryItemSummary(
                item.InstanceId, TextKey.Literal(name), item.Quantity, location.IsEquipped,
                location.IsEquipped ? location.Character : null, slotName, requiredSlot);
        }
    }
}
