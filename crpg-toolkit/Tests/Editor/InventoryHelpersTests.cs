using System.Collections.Generic;
using System.Linq;
using CRPG.Core;
using CRPG.Dialogue;
using CRPG.Editor.Validation;
using CRPG.Inventory;
using CRPG.Rules;
using CRPG.UI;
using NUnit.Framework;
using UnityEngine;

namespace CRPG.Tests.EditMode
{
    /// <summary>Slice 0 of NPC services (<c>Docs/Plans/Toolkit-NpcServices.md</c>): counting and removing N of an item across stacks, the store never selling the party's money, and the dialogue validator's give-item rule.</summary>
    public sealed class InventoryHelpersTests
    {
        private readonly DefinitionId gold = new("item-gold");
        private readonly DefinitionId arrow = new("item-arrow");

        private static PartyInventoryState InventoryWith(params (DefinitionId item, int quantity)[] stacks)
        {
            var inventory = new PartyInventoryState();
            foreach (var (item, quantity) in stacks) inventory.AddItem(new ItemInstance(RuntimeEntityId.NewId(), item, quantity));
            return inventory;
        }

        [Test]
        public void CountShared_SumsEveryStackOfThatItemOnly()
        {
            var inventory = InventoryWith((gold, 30), (gold, 25), (arrow, 7));

            Assert.AreEqual(55, inventory.CountShared(gold));
            Assert.AreEqual(7, inventory.CountShared(arrow));
            Assert.AreEqual(0, inventory.CountShared(new DefinitionId("item-none")));
        }

        [Test]
        public void CountShared_IgnoresAnEquippedCopy()
        {
            var inventory = new PartyInventoryState();
            var worn = new ItemInstance(RuntimeEntityId.NewId(), arrow, 1);
            inventory.AddItem(worn, ItemLocation.Equipped(RuntimeEntityId.NewId(), new DefinitionId("slot-hand")));
            inventory.AddItem(new ItemInstance(RuntimeEntityId.NewId(), arrow, 4));

            Assert.AreEqual(4, inventory.CountShared(arrow), "what a character wears is not for spending");
        }

        [Test]
        public void TryRemoveShared_TakesFromSeveralStacks_AndDeletesTheEmptiedOnes()
        {
            var inventory = InventoryWith((gold, 30), (gold, 25));

            Assert.IsTrue(inventory.TryRemoveShared(gold, 40));

            Assert.AreEqual(15, inventory.CountShared(gold));
            Assert.AreEqual(1, inventory.AllItems.Count(i => i.DefinitionId == gold), "the stack that was emptied is gone, not left at 0");
        }

        [Test]
        public void TryRemoveShared_IsAllOrNothing()
        {
            var inventory = InventoryWith((gold, 10), (gold, 5));

            Assert.IsFalse(inventory.TryRemoveShared(gold, 16));

            Assert.AreEqual(15, inventory.CountShared(gold), "nothing was taken");
            Assert.AreEqual(2, inventory.AllItems.Count());
        }

        [Test]
        public void TryRemoveShared_ZeroSucceedsAsANoOp_NegativeFails()
        {
            var inventory = InventoryWith((gold, 10));

            Assert.IsTrue(inventory.TryRemoveShared(gold, 0));
            Assert.IsFalse(inventory.TryRemoveShared(gold, -3));
            Assert.AreEqual(10, inventory.CountShared(gold));
            Assert.IsTrue(new PartyInventoryState().TryRemoveShared(gold, 0), "even with none of it");
        }

        [Test]
        public void TryRemoveShared_LeavesAnEquippedCopyAlone()
        {
            var inventory = new PartyInventoryState();
            inventory.AddItem(new ItemInstance(RuntimeEntityId.NewId(), arrow, 2), ItemLocation.Equipped(RuntimeEntityId.NewId(), new DefinitionId("slot-hand")));
            inventory.AddItem(new ItemInstance(RuntimeEntityId.NewId(), arrow, 3));

            Assert.IsFalse(inventory.TryRemoveShared(arrow, 4), "only 3 are spendable");
            Assert.IsTrue(inventory.TryRemoveShared(arrow, 3));
            Assert.AreEqual(1, inventory.AllItems.Count(i => i.DefinitionId == arrow), "the worn one stayed");
        }

        // ---- store

        [Test]
        public void TheStoreStillSpendsAcrossCurrencyStacks_NowThroughTheSharedHelper()
        {
            var rules = new RulesDatabase();
            var goldItem = TestDefinitionFactory.CreateItemDefinition("item-gold", "Gold Piece", maxStackSize: 999);
            var potion = TestDefinitionFactory.CreateItemDefinition("item-potion", "Potion", baseValue: 50f);
            rules.Register(goldItem);
            rules.Register(potion);
            var inventory = InventoryWith((goldItem.Id, 30), (goldItem.Id, 30));
            var shop = new StoreInventoryState();
            shop.AddListing(potion.Id);
            var service = new StoreService(rules, inventory, goldItem.Id);

            Assert.IsTrue(service.TryBuy(shop, potion.Id));

            Assert.AreEqual(10, service.GetCurrencyTotal());
            Assert.IsFalse(service.TryBuy(shop, potion.Id), "10 gold cannot buy a 50 gold potion");
            Assert.AreEqual(10, service.GetCurrencyTotal(), "and a refused purchase takes nothing");
        }

        [Test]
        public void TheStoreNeverOffersThePartysMoneyForSale()
        {
            var rules = new RulesDatabase();
            var goldItem = TestDefinitionFactory.CreateItemDefinition("item-gold", "Gold Piece", baseValue: 0f, maxStackSize: 999);
            var dagger = TestDefinitionFactory.CreateItemDefinition("item-dagger", "Dagger", baseValue: 2f);
            rules.Register(goldItem);
            rules.Register(dagger);
            var inventory = InventoryWith((goldItem.Id, 80), (dagger.Id, 1));
            var presenter = new StorePresenter(new StoreInventoryState(), inventory, rules, new StoreService(rules, inventory, goldItem.Id));

            var sellable = presenter.GetSellableItems();

            Assert.AreEqual(1, sellable.Count);
            Assert.AreEqual("Dagger", sellable[0].DisplayName);
        }

        // ---- validator

        [Test]
        public void DialogueValidator_AGiveItemActionWithNoItem_IsAnError()
        {
            var give = new ActionNode();
            TestDefinitionFactory.SetPrivateField(give, "nodeId", SerializableGuid.FromString("give"));
            TestDefinitionFactory.SetPrivateField(give, "action", new GiveItemAction());
            TestDefinitionFactory.SetPrivateField(give, "nextNodeId", SerializableGuid.FromString("end"));
            var dialogue = ScriptableObject.CreateInstance<DialogueDefinition>();
            try
            {
                TestDefinitionFactory.SetPrivateField(dialogue, "id", "dlg-give");
                TestDefinitionFactory.SetPrivateField(dialogue, "startNodeId", SerializableGuid.FromString("give"));
                TestDefinitionFactory.SetPrivateField(dialogue, "nodes", new List<DialogueNode> { give, DialogueTestFactory.CreateEnd("end") });

                Assert.IsTrue(DialogueValidator.Validate(dialogue).Any(r => r.Severity == ValidationSeverity.Error && r.Message.Contains("no item")));
            }
            finally
            {
                Object.DestroyImmediate(dialogue);
            }
        }
    }
}
