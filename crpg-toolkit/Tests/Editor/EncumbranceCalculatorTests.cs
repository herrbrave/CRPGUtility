using CRPG.Core;
using CRPG.Inventory;
using NUnit.Framework;

namespace CRPG.Tests.EditMode
{
    public class EncumbranceCalculatorTests
    {
        private static readonly DefinitionId MainHand = new("slot-mainhand");

        [Test]
        public void DisabledCalculatorAlwaysReturnsZero()
        {
            var rulesDatabase = new RulesDatabase();
            var inventory = new PartyInventoryState();
            var calculator = new DisabledEncumbranceCalculator();

            var weight = calculator.CalculateTotalWeight(inventory, rulesDatabase, RuntimeEntityId.NewId());

            Assert.AreEqual(0f, weight);
        }

        [Test]
        public void PartyCombinedCalculatorSumsEveryItemRegardlessOfOwner()
        {
            var rulesDatabase = new RulesDatabase();
            var sword = TestDefinitionFactory.CreateItemDefinition("item-sword", "Longsword", weight: 3f, equipmentSlotId: MainHand);
            var potion = TestDefinitionFactory.CreateItemDefinition("item-potion", "Potion", weight: 0.5f);
            rulesDatabase.Register(sword);
            rulesDatabase.Register(potion);

            var inventory = new PartyInventoryState();
            var character = RuntimeEntityId.NewId();
            inventory.AddItem(new ItemInstance(RuntimeEntityId.NewId(), sword.Id), ItemLocation.Equipped(character, MainHand));
            inventory.AddItem(new ItemInstance(RuntimeEntityId.NewId(), potion.Id, quantity: 2)); // shared, unequipped

            var calculator = new PartyCombinedEncumbranceCalculator();
            var weight = calculator.CalculateTotalWeight(inventory, rulesDatabase, character);

            Assert.AreEqual(4f, weight, 0.001f); // 3 (sword) + 0.5*2 (potions)
        }

        [Test]
        public void PerCharacterCalculatorOnlyCountsThatCharactersEquippedItems()
        {
            var rulesDatabase = new RulesDatabase();
            var sword = TestDefinitionFactory.CreateItemDefinition("item-sword", "Longsword", weight: 3f, equipmentSlotId: MainHand);
            rulesDatabase.Register(sword);

            var inventory = new PartyInventoryState();
            var fighter = RuntimeEntityId.NewId();
            var rogue = RuntimeEntityId.NewId();
            inventory.AddItem(new ItemInstance(RuntimeEntityId.NewId(), sword.Id), ItemLocation.Equipped(fighter, MainHand));

            var calculator = new PerCharacterEncumbranceCalculator();

            Assert.AreEqual(3f, calculator.CalculateTotalWeight(inventory, rulesDatabase, fighter), 0.001f);
            Assert.AreEqual(0f, calculator.CalculateTotalWeight(inventory, rulesDatabase, rogue), 0.001f);
        }
    }
}
