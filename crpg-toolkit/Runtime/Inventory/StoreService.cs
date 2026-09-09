using System;
using System.Collections.Generic;
using System.Linq;
using CRPG.Core;

namespace CRPG.Inventory
{
    /// <summary>
    /// Buy/sell against a StoreInventoryState. Currency isn't a new mechanism -- it's a regular stackable
    /// ItemDefinition (e.g. "Gold Piece") living in the party's shared inventory like anything else, so this
    /// service is configured with which DefinitionId to treat as currency rather than the engine knowing about
    /// money as a concept.
    /// </summary>
    public sealed class StoreService
    {
        private readonly IRulesDatabase rulesDatabase;
        private readonly PartyInventoryState partyInventory;
        private readonly DefinitionId currencyItemId;

        public StoreService(IRulesDatabase rulesDatabase, PartyInventoryState partyInventory, DefinitionId currencyItemId)
        {
            this.rulesDatabase = rulesDatabase;
            this.partyInventory = partyInventory;
            this.currencyItemId = currencyItemId;
        }

        public int GetBuyPrice(StoreInventoryState store, DefinitionId itemId) =>
            RoundPrice(rulesDatabase.Resolve<ItemDefinition>(itemId).BaseValue * store.BuyPriceMultiplier);

        public int GetSellPrice(StoreInventoryState store, DefinitionId itemId) =>
            RoundPrice(rulesDatabase.Resolve<ItemDefinition>(itemId).BaseValue * store.SellPriceMultiplier);

        public int GetCurrencyTotal() =>
            partyInventory.ItemsInSharedInventory.Where(item => item.DefinitionId == currencyItemId).Sum(item => item.Quantity);

        /// <returns>False without side effects if the item isn't listed, is out of stock, or the party can't afford it.</returns>
        public bool TryBuy(StoreInventoryState store, DefinitionId itemId, int quantity = 1)
        {
            if (!store.TryGetListing(itemId, out var listing)) return false;
            if (!listing.IsUnlimited && listing.Quantity < quantity) return false;

            var price = GetBuyPrice(store, itemId) * quantity;
            if (!TrySpendCurrency(price)) return false;

            if (!listing.IsUnlimited) listing.Quantity -= quantity;

            partyInventory.AddItem(new ItemInstance(RuntimeEntityId.NewId(), itemId, quantity));
            return true;
        }

        /// <returns>False without side effects if the item isn't in the party's shared inventory (equipped
        /// items must be unequipped first -- selling your own gear off your back isn't modeled).</returns>
        public bool TrySell(StoreInventoryState store, RuntimeEntityId instanceId)
        {
            if (!partyInventory.TryGetItem(instanceId, out var item)) return false;
            if (partyInventory.GetLocation(instanceId).IsEquipped) return false;

            var price = GetSellPrice(store, item.DefinitionId) * item.Quantity;
            partyInventory.RemoveItem(instanceId);
            AddCurrency(price);
            return true;
        }

        private static int RoundPrice(float value) => (int)Math.Round(value, MidpointRounding.AwayFromZero);

        private bool TrySpendCurrency(int amount)
        {
            if (GetCurrencyTotal() < amount) return false;

            var remaining = amount;
            foreach (var item in partyInventory.ItemsInSharedInventory.Where(i => i.DefinitionId == currencyItemId).ToList())
            {
                if (remaining <= 0) break;

                var take = Math.Min(item.Quantity, remaining);
                item.Quantity -= take;
                remaining -= take;
                if (item.Quantity <= 0) partyInventory.RemoveItem(item.InstanceId);
            }

            return true;
        }

        private void AddCurrency(int amount)
        {
            if (amount <= 0) return;
            partyInventory.AddItem(new ItemInstance(RuntimeEntityId.NewId(), currencyItemId, amount));
        }
    }
}
