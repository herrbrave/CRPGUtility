using System.Collections.Generic;
using CRPG.Core;
using CRPG.Inventory;

namespace CRPG.UI
{
    /// <summary>
    /// Pure data/command layer for the Store screen -- no UI Toolkit dependency, mirroring CampPresenter's
    /// split so a polished View can replace StoreView later without this changing.
    /// </summary>
    public sealed class StorePresenter
    {
        private readonly StoreInventoryState store;
        private readonly PartyInventoryState partyInventory;
        private readonly IRulesDatabase rulesDatabase;
        private readonly StoreService storeService;

        public StorePresenter(StoreInventoryState store, PartyInventoryState partyInventory, IRulesDatabase rulesDatabase, StoreService storeService)
        {
            this.store = store;
            this.partyInventory = partyInventory;
            this.rulesDatabase = rulesDatabase;
            this.storeService = storeService;
        }

        public int PartyGold => storeService.GetCurrencyTotal();

        public IReadOnlyList<StoreListingSummary> GetListings()
        {
            var summaries = new List<StoreListingSummary>();
            foreach (var listing in store.Listings)
            {
                var name = rulesDatabase.TryResolve<ItemDefinition>(listing.ItemId, out var item) ? item.DisplayName : listing.ItemId.ToString();
                var price = storeService.GetBuyPrice(store, listing.ItemId);
                summaries.Add(new StoreListingSummary(listing.ItemId, name, price, listing.Quantity));
            }

            return summaries;
        }

        public IReadOnlyList<OwnedItemSummary> GetSellableItems()
        {
            var summaries = new List<OwnedItemSummary>();
            foreach (var item in partyInventory.ItemsInSharedInventory)
            {
                var name = rulesDatabase.TryResolve<ItemDefinition>(item.DefinitionId, out var definition) ? definition.DisplayName : item.DefinitionId.ToString();
                var price = storeService.GetSellPrice(store, item.DefinitionId) * item.Quantity;
                summaries.Add(new OwnedItemSummary(item.InstanceId, name, item.Quantity, price));
            }

            return summaries;
        }

        public bool Buy(DefinitionId itemId, int quantity = 1) => storeService.TryBuy(store, itemId, quantity);

        public bool Sell(RuntimeEntityId instanceId) => storeService.TrySell(store, instanceId);
    }
}
