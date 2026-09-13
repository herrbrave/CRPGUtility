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

            Assert.IsTrue(bought.Success);
            Assert.AreEqual(StoreTransactionOutcome.Success, bought.Outcome);
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

            Assert.IsTrue(sold.Success);
            Assert.AreEqual(StoreTransactionOutcome.Success, sold.Outcome);
            Assert.AreEqual(200 + 25, presenter.PartyGold);
            Assert.IsFalse(inventory.TryGetItem(owned.InstanceId, out _));
        }

        [Test]
        public void BuyReportsOutOfStockWithoutSpendingAnyGold()
        {
            var (presenter, _, potion, _) = BuildScenario();

            var result = presenter.Buy(potion.Id, quantity: 99); // only 3 listed

            Assert.AreEqual(StoreTransactionOutcome.OutOfStock, result.Outcome);
            Assert.AreEqual(200, presenter.PartyGold); // untouched
        }

        [Test]
        public void BuyReportsInsufficientFundsWithoutTouchingStock()
        {
            var rulesDatabase = new RulesDatabase();
            var inventory = new PartyInventoryState();
            var gold = TestDefinitionFactory.CreateItemDefinition("item-gold", "Gold Piece", maxStackSize: 999);
            var potion = TestDefinitionFactory.CreateItemDefinition("item-potion", "Healing Potion", baseValue: 50f);
            rulesDatabase.Register(gold);
            rulesDatabase.Register(potion);
            var shop = new StoreInventoryState();
            shop.AddListing(potion.Id, quantity: 3);
            inventory.AddItem(new ItemInstance(RuntimeEntityId.NewId(), gold.Id, 10)); // not enough for 50g potion
            var presenter = new StorePresenter(shop, inventory, rulesDatabase, new StoreService(rulesDatabase, inventory, gold.Id));

            var result = presenter.Buy(potion.Id);

            Assert.AreEqual(StoreTransactionOutcome.InsufficientFunds, result.Outcome);
            Assert.AreEqual(3, presenter.GetListings()[0].RemainingQuantity); // untouched
        }

        [Test]
        public void BuyReportsNotListedForAnUnknownItem()
        {
            var (presenter, _, _, _) = BuildScenario();
            var unlisted = TestDefinitionFactory.CreateItemDefinition("item-unlisted", "Mystery Item");

            var result = presenter.Buy(unlisted.Id);

            Assert.AreEqual(StoreTransactionOutcome.NotListed, result.Outcome);
        }

        [Test]
        public void SellReportsItemNotOwnedForAnInstanceThePartyDoesNotHave()
        {
            var (presenter, _, _, _) = BuildScenario();

            var result = presenter.Sell(RuntimeEntityId.NewId());

            Assert.AreEqual(StoreTransactionOutcome.ItemNotOwned, result.Outcome);
        }

        [Test]
        public void SellReportsItemEquippedForAnEquippedItem()
        {
            var (presenter, inventory, potion, _) = BuildScenario();
            var owned = new ItemInstance(RuntimeEntityId.NewId(), potion.Id);
            var equippedCharacter = RuntimeEntityId.NewId();
            var slot = TestDefinitionFactory.CreateEquipmentSlotDefinition("slot-trinket", "Trinket");
            inventory.AddItem(owned, ItemLocation.Equipped(equippedCharacter, slot.Id));

            var result = presenter.Sell(owned.InstanceId);

            Assert.AreEqual(StoreTransactionOutcome.ItemEquipped, result.Outcome);
            Assert.IsTrue(inventory.TryGetItem(owned.InstanceId, out _)); // not removed
        }
    }
}
