using System.Linq;
using CRPG.Core;
using CRPG.Inventory;
using CRPG.Rules;
using NUnit.Framework;

namespace CRPG.Tests.EditMode
{
    public class StoreServiceTests
    {
        private RulesDatabase rulesDatabase;
        private PartyInventoryState inventory;
        private ItemDefinition gold;
        private ItemDefinition potion;
        private StoreService store;

        [SetUp]
        public void SetUp()
        {
            rulesDatabase = new RulesDatabase();
            inventory = new PartyInventoryState();

            gold = TestDefinitionFactory.CreateItemDefinition("item-gold", "Gold Piece", maxStackSize: 999);
            potion = TestDefinitionFactory.CreateItemDefinition("item-potion", "Healing Potion", baseValue: 50f);
            rulesDatabase.Register(gold);
            rulesDatabase.Register(potion);

            store = new StoreService(rulesDatabase, inventory, gold.Id);
        }

        private void GiveParty(int goldAmount) => inventory.AddItem(new ItemInstance(RuntimeEntityId.NewId(), gold.Id, goldAmount));

        [Test]
        public void GetBuyPriceMultipliesBaseValueByTheStoresBuyMultiplier()
        {
            var shop = new StoreInventoryState(buyPriceMultiplier: 1.5f);

            Assert.AreEqual(75, store.GetBuyPrice(shop, potion.Id)); // 50 * 1.5
        }

        [Test]
        public void ChangingBuyPriceMultiplierAfterConstructionAffectsSubsequentPrices()
        {
            var shop = new StoreInventoryState(buyPriceMultiplier: 1f);
            Assert.AreEqual(50, store.GetBuyPrice(shop, potion.Id));

            shop.BuyPriceMultiplier = 2f;

            Assert.AreEqual(100, store.GetBuyPrice(shop, potion.Id));
        }

        [Test]
        public void GetSellPriceMultipliesBaseValueByTheStoresSellMultiplier()
        {
            var shop = new StoreInventoryState(sellPriceMultiplier: 0.5f);

            Assert.AreEqual(25, store.GetSellPrice(shop, potion.Id)); // 50 * 0.5
        }

        [Test]
        public void TryBuyWithUnlimitedStockSucceedsAndNeverDepletes()
        {
            var shop = new StoreInventoryState(buyPriceMultiplier: 1f);
            shop.AddListing(potion.Id); // unlimited by default
            GiveParty(1000);

            var bought = store.TryBuy(shop, potion.Id, 3);

            Assert.IsTrue(bought);
            Assert.AreEqual(1000 - 150, store.GetCurrencyTotal());
            Assert.IsTrue(shop.TryGetListing(potion.Id, out var listing));
            Assert.IsTrue(listing.IsUnlimited);
            var given = inventory.ItemsInSharedInventory.Single(i => i.DefinitionId == potion.Id);
            Assert.AreEqual(3, given.Quantity);
        }

        [Test]
        public void TryBuyWithFiniteStockDepletesAndEventuallyFails()
        {
            var shop = new StoreInventoryState();
            shop.AddListing(potion.Id, quantity: 2);
            GiveParty(1000);

            Assert.IsTrue(store.TryBuy(shop, potion.Id, 2));
            Assert.IsFalse(store.TryBuy(shop, potion.Id, 1)); // out of stock now

            shop.TryGetListing(potion.Id, out var listing);
            Assert.AreEqual(0, listing.Quantity);
        }

        [Test]
        public void TryBuyFailsWithoutSideEffectsWhenPartyCannotAfford()
        {
            var shop = new StoreInventoryState();
            shop.AddListing(potion.Id);
            GiveParty(10); // potion costs 50

            var bought = store.TryBuy(shop, potion.Id);

            Assert.IsFalse(bought);
            Assert.AreEqual(10, store.GetCurrencyTotal());
            Assert.IsFalse(inventory.ItemsInSharedInventory.Any(i => i.DefinitionId == potion.Id));
        }

        [Test]
        public void TryBuyFailsWhenItemIsNotListed()
        {
            var shop = new StoreInventoryState();
            GiveParty(1000);

            Assert.IsFalse(store.TryBuy(shop, potion.Id));
        }

        [Test]
        public void TrySellRemovesTheItemAndPaysTheParty()
        {
            var shop = new StoreInventoryState(sellPriceMultiplier: 0.5f);
            var owned = new ItemInstance(RuntimeEntityId.NewId(), potion.Id, quantity: 2);
            inventory.AddItem(owned);

            var sold = store.TrySell(shop, owned.InstanceId);

            Assert.IsTrue(sold);
            Assert.IsFalse(inventory.TryGetItem(owned.InstanceId, out _));
            Assert.AreEqual(50, store.GetCurrencyTotal()); // 2 * (50 * 0.5)
        }

        [Test]
        public void TrySellFailsForAnEquippedItem()
        {
            var shop = new StoreInventoryState();
            var sword = new ItemInstance(RuntimeEntityId.NewId(), potion.Id);
            inventory.AddItem(sword, ItemLocation.Equipped(RuntimeEntityId.NewId(), new DefinitionId("slot-mainhand")));

            var sold = store.TrySell(shop, sword.InstanceId);

            Assert.IsFalse(sold);
            Assert.IsTrue(inventory.TryGetItem(sword.InstanceId, out _));
        }

        [Test]
        public void TrySellFailsWhenTheInstanceIsNotInThePartyInventory()
        {
            var shop = new StoreInventoryState();

            Assert.IsFalse(store.TrySell(shop, RuntimeEntityId.NewId()));
        }
    }
}
