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

        /// <summary>Task 3.6: re-derives the specific failure reason from the same cheap checks StoreService.TryBuy
        /// itself performs, immediately before calling through -- StoreService's own bool-returning signature is
        /// deliberately left untouched (already tested, called from other real code paths).</summary>
        public StoreTransactionResult Buy(DefinitionId itemId, int quantity = 1)
        {
            if (!store.TryGetListing(itemId, out var listing)) return StoreTransactionResult.Fail(StoreTransactionOutcome.NotListed);
            if (!listing.IsUnlimited && listing.Quantity < quantity) return StoreTransactionResult.Fail(StoreTransactionOutcome.OutOfStock);
            if (storeService.GetCurrencyTotal() < storeService.GetBuyPrice(store, itemId) * quantity)
                return StoreTransactionResult.Fail(StoreTransactionOutcome.InsufficientFunds);

            var succeeded = storeService.TryBuy(store, itemId, quantity);
            return succeeded ? StoreTransactionResult.Ok() : StoreTransactionResult.Fail(StoreTransactionOutcome.NotListed);
        }

        public StoreTransactionResult Sell(RuntimeEntityId instanceId)
        {
            if (!partyInventory.TryGetItem(instanceId, out _)) return StoreTransactionResult.Fail(StoreTransactionOutcome.ItemNotOwned);
            if (partyInventory.GetLocation(instanceId).IsEquipped) return StoreTransactionResult.Fail(StoreTransactionOutcome.ItemEquipped);

            var succeeded = storeService.TrySell(store, instanceId);
            return succeeded ? StoreTransactionResult.Ok() : StoreTransactionResult.Fail(StoreTransactionOutcome.ItemNotOwned);
        }
    }
}
