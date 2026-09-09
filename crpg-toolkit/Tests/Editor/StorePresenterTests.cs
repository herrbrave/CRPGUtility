using System.Linq;
using CRPG.Core;
using CRPG.Inventory;
using CRPG.Rules;
using CRPG.UI;
using NUnit.Framework;

namespace CRPG.Tests.EditMode
{
    public class StorePresenterTests
    {
        private static (StorePresenter presenter, PartyInventoryState inventory, ItemDefinition potion, StoreInventoryState shop) BuildScenario()
        {
            var rulesDatabase = new RulesDatabase();
            var inventory = new PartyInventoryState();

            var gold = TestDefinitionFactory.CreateItemDefinition("item-gold", "Gold Piece", maxStackSize: 999);
            var potion = TestDefinitionFactory.CreateItemDefinition("item-potion", "Healing Potion", baseValue: 50f);
            rulesDatabase.Register(gold);
            rulesDatabase.Register(potion);

            var shop = new StoreInventoryState(buyPriceMultiplier: 1f, sellPriceMultiplier: 0.5f);
            shop.AddListing(potion.Id, quantity: 3);

            inventory.AddItem(new ItemInstance(RuntimeEntityId.NewId(), gold.Id, 200));

            var storeService = new StoreService(rulesDatabase, inventory, gold.Id);
            var presenter = new StorePresenter(shop, inventory, rulesDatabase, storeService);
            return (presenter, inventory, potion, shop);
        }

        [Test]
        public void PartyGoldReflectsTheSharedInventorysCurrencyStacks()
        {
            var (presenter, _, _, _) = BuildScenario();

            Assert.AreEqual(200, presenter.PartyGold);
        }

        [Test]
        public void GetListingsReportsDisplayNamePriceAndRemainingStock()
        {
            var (presenter, _, potion, _) = BuildScenario();

            var listings = presenter.GetListings();

            Assert.AreEqual(1, listings.Count);
            Assert.AreEqual(potion.Id, listings[0].ItemId);
            Assert.AreEqual("Healing Potion", listings[0].DisplayName);
            Assert.AreEqual(50, listings[0].Price);
            Assert.AreEqual(3, listings[0].RemainingQuantity);
        }

        [Test]
        public void BuyDecrementsListedStockAndAddsToPartyInventory()
        {
            var (presenter, inventory, potion, _) = BuildScenario();

            var bought = presenter.Buy(potion.Id);

            Assert.IsTrue(bought);
            Assert.AreEqual(2, presenter.GetListings()[0].RemainingQuantity);
            Assert.IsTrue(inventory.ItemsInSharedInventory.Any(i => i.DefinitionId == potion.Id));
        }

        [Test]
        public void GetSellableItemsReportsWhatThePartyIsCarrying()
        {
            var (presenter, inventory, potion, _) = BuildScenario();
            inventory.AddItem(new ItemInstance(RuntimeEntityId.NewId(), potion.Id, quantity: 2));

            var sellable = presenter.GetSellableItems().Single(i => i.DisplayName == "Healing Potion");

            Assert.AreEqual(2, sellable.Quantity);
            Assert.AreEqual(50, sellable.SellPrice); // 2 * (50 * 0.5)
        }

        [Test]
        public void SellRemovesTheItemAndIncreasesPartyGold()
        {
            var (presenter, inventory, potion, _) = BuildScenario();
            var owned = new ItemInstance(RuntimeEntityId.NewId(), potion.Id);
            inventory.AddItem(owned);

            var sold = presenter.Sell(owned.InstanceId);

            Assert.IsTrue(sold);
            Assert.AreEqual(200 + 25, presenter.PartyGold);
            Assert.IsFalse(inventory.TryGetItem(owned.InstanceId, out _));
        }
    }
}
