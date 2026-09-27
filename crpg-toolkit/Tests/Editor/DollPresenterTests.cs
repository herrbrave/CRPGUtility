using System.Linq;
using CRPG.Actors;
using CRPG.Core;
using CRPG.Inventory;
using CRPG.Party;
using CRPG.Rules;
using CRPG.UI;
using NUnit.Framework;

namespace CRPG.Tests.EditMode
{
    public class DollPresenterTests
    {
        private sealed class Scenario
        {
            public DollPresenter Presenter;
            public PartyState Party;
            public RulesDatabase Rules;
            public CharacterRegistry Registry;
            public PartyInventoryState Inventory;
            public EquipmentService Equipment;
            public InventoryPresenter InventoryPresenter;
            public ResourceDefinition Hp;
            public RuntimeEntityId Fighter;
            public RuntimeEntityId Rogue;
            public EquipmentSlotDefinition MainHand;
            public ItemDefinition Sword;
        }

        private static Scenario BuildScenario()
        {
            var rules = new RulesDatabase();
            var registry = new CharacterRegistry();
            var inventory = new PartyInventoryState();

            var hp = TestDefinitionFactory.CreateResourceDefinition("resource-hp", "Hit Points", RestTrigger.LongRest);
            rules.Register(hp);

            foreach (var (id, name, abbr) in new[]
                     {
                         ("stat-strength", "Strength", "STR"), ("stat-dexterity", "Dexterity", "DEX"),
                         ("stat-constitution", "Constitution", "CON"), ("stat-intelligence", "Intelligence", "INT"),
                         ("stat-wisdom", "Wisdom", "WIS"), ("stat-charisma", "Charisma", "CHA"),
                     })
            {
                var stat = TestDefinitionFactory.CreateStatDefinition(id, name);
                TestDefinitionFactory.SetPrivateField(stat, "abbreviation", abbr);
                rules.Register(stat);
            }

            var head = TestDefinitionFactory.CreateEquipmentSlotDefinition("slot-head", "Head");
            var armor = TestDefinitionFactory.CreateEquipmentSlotDefinition("slot-armor", "Armor");
            var mainHand = TestDefinitionFactory.CreateEquipmentSlotDefinition("slot-mainhand", "Main Hand");
            var offHand = TestDefinitionFactory.CreateEquipmentSlotDefinition("slot-offhand", "Off Hand");
            var clothingBody = TestDefinitionFactory.CreateEquipmentSlotDefinition("slot-clothing-body", "Body");
            foreach (var slot in new[] { head, armor, mainHand, offHand, clothingBody }) rules.Register(slot);

            var sword = TestDefinitionFactory.CreateItemDefinition("item-sword", "Longsword", equipmentSlotId: mainHand.Id);
            rules.Register(sword);

            var fighter = new CharacterState(RuntimeEntityId.NewId());
            fighter.AddResource(new ResourceState(hp.Id, max: 28, current: 20));
            fighter.Stats.SetScore(new DefinitionId("stat-strength"), 16);
            registry.Register(fighter);

            var rogue = new CharacterState(RuntimeEntityId.NewId());
            rogue.AddResource(new ResourceState(hp.Id, max: 18, current: 18));
            rogue.Stats.SetScore(new DefinitionId("stat-strength"), 10);
            registry.Register(rogue);

            var party = new PartyState(new[] { fighter.Id, rogue.Id });
            var equipmentService = new EquipmentService(registry, rules, inventory);
            var inventoryPresenter = new InventoryPresenter(inventory, rules, equipmentService);
            var presenter = new DollPresenter(party, registry, rules, inventoryPresenter, hp);

            return new Scenario
            {
                Presenter = presenter, Party = party, Rules = rules, Registry = registry, Inventory = inventory,
                Equipment = equipmentService, InventoryPresenter = inventoryPresenter, Hp = hp,
                Fighter = fighter.Id, Rogue = rogue.Id, MainHand = mainHand, Sword = sword,
            };
        }

        [Test]
        public void StartsOnThePartysActiveCharacter()
        {
            var s = BuildScenario();
            s.Party.Select(s.Rogue);

            var presenter = new DollPresenter(s.Party, s.Registry, s.Rules, s.InventoryPresenter, s.Hp);

            Assert.AreEqual(s.Rogue, presenter.CurrentCharacter);
        }

        [Test]
        public void GetAnatomicalSlotsReturnsAllFiveEvenWhenEmpty()
        {
            var s = BuildScenario();

            var slots = s.Presenter.GetAnatomicalSlots();

            Assert.AreEqual(5, slots.Count);
            Assert.IsTrue(slots.All(slot => !slot.HasItem));
            CollectionAssert.AreEqual(DollPresenter.AnatomicalSlots, slots.Select(slot => slot.SlotId).ToList());
        }

        [Test]
        public void GetAnatomicalSlotsShowsAnEquippedItemInTheRightSlot()
        {
            var s = BuildScenario();
            var instance = new ItemInstance(RuntimeEntityId.NewId(), s.Sword.Id);
            s.Inventory.AddItem(instance);
            s.Party.Select(s.Fighter); // Fighter is not the default active if Select was never called -- AddMember picks Members[0] already, but be explicit.
            s.Equipment.Equip(instance.InstanceId, s.Fighter, s.MainHand.Id);

            var slots = s.Presenter.GetAnatomicalSlots();
            var mainHandSlot = slots.Single(slot => slot.SlotId == s.MainHand.Id);

            Assert.IsTrue(mainHandSlot.HasItem);
            Assert.AreEqual(instance.InstanceId, mainHandSlot.Item.InstanceId);
        }

        [Test]
        public void GetAbilityScoresReadsAllSixWithRealAbbreviations()
        {
            var s = BuildScenario();
            s.Party.Select(s.Fighter);

            var scores = s.Presenter.GetAbilityScores();

            Assert.AreEqual(6, scores.Count);
            var strength = scores.Single(sc => sc.StatId == new DefinitionId("stat-strength"));
            Assert.AreEqual("STR", strength.Abbreviation);
            Assert.AreEqual(16, strength.Score);
        }

        [Test]
        public void GetHitPointsReadsTheCurrentCharactersResource()
        {
            var s = BuildScenario();
            s.Party.Select(s.Rogue); // must happen before the presenter snapshots CurrentCharacter at construction
            var presenter = new DollPresenter(s.Party, s.Registry, s.Rules, s.InventoryPresenter, s.Hp);

            var (current, max) = presenter.GetHitPoints();

            Assert.AreEqual(18, current);
            Assert.AreEqual(18, max);
        }

        [Test]
        public void CycleNextMovesToTheNextMemberAndWrapsAround()
        {
            var s = BuildScenario();
            s.Party.Select(s.Fighter);
            var presenter = new DollPresenter(s.Party, s.Registry, s.Rules, s.InventoryPresenter, s.Hp);

            presenter.CycleNext();
            Assert.AreEqual(s.Rogue, presenter.CurrentCharacter);
            Assert.AreEqual(s.Rogue, s.Party.ActiveCharacter); // cycling also moves the party's real active character

            presenter.CycleNext();
            Assert.AreEqual(s.Fighter, presenter.CurrentCharacter); // wrapped back around
        }

        [Test]
        public void CyclePreviousWrapsBackward()
        {
            var s = BuildScenario();
            s.Party.Select(s.Fighter);
            var presenter = new DollPresenter(s.Party, s.Registry, s.Rules, s.InventoryPresenter, s.Hp);

            presenter.CyclePrevious();

            Assert.AreEqual(s.Rogue, presenter.CurrentCharacter);
        }

        [Test]
        public void EquipAndUnequipRouteThroughTheWrappedInventoryPresenter()
        {
            var s = BuildScenario();
            var instance = new ItemInstance(RuntimeEntityId.NewId(), s.Sword.Id);
            s.Inventory.AddItem(instance);
            s.Party.Select(s.Fighter);
            var presenter = new DollPresenter(s.Party, s.Registry, s.Rules, s.InventoryPresenter, s.Hp);

            var equipResult = presenter.Equip(instance.InstanceId, s.MainHand.Id);
            Assert.IsTrue(equipResult.Success);
            Assert.IsTrue(s.Inventory.GetLocation(instance.InstanceId).IsEquipped);

            var unequipResult = presenter.Unequip(instance.InstanceId);
            Assert.IsTrue(unequipResult.Success);
            Assert.IsFalse(s.Inventory.GetLocation(instance.InstanceId).IsEquipped);
        }

        [Test]
        public void SyncToActiveCharacterFollowsTheHudsSelection()
        {
            var s = BuildScenario();
            s.Party.Select(s.Fighter);
            var presenter = new DollPresenter(s.Party, s.Registry, s.Rules, s.InventoryPresenter, s.Hp);
            s.Party.Select(s.Rogue); // e.g. a HUD portrait click while the doll is closed

            presenter.SyncToActiveCharacter();

            Assert.AreEqual(s.Rogue, presenter.CurrentCharacter);
        }

        [Test]
        public void RebindSwapsInANewSessionsState()
        {
            var s = BuildScenario();
            s.Party.Select(s.Fighter);
            var presenter = new DollPresenter(s.Party, s.Registry, s.Rules, s.InventoryPresenter, s.Hp);

            var newRules = new RulesDatabase();
            newRules.Register(s.Hp);
            var newRegistry = new CharacterRegistry();
            var newFighter = new CharacterState(RuntimeEntityId.NewId());
            newFighter.AddResource(new ResourceState(s.Hp.Id, max: 12, current: 12));
            newRegistry.Register(newFighter);
            var newParty = new PartyState(new[] { newFighter.Id });
            var newInventory = new PartyInventoryState();
            var newEquipmentService = new EquipmentService(newRegistry, newRules, newInventory);

            presenter.Rebind(newParty, newRegistry, newRules, newInventory, newEquipmentService);

            Assert.AreEqual(newFighter.Id, presenter.CurrentCharacter);
            var (current, max) = presenter.GetHitPoints();
            Assert.AreEqual(12, current);
            Assert.AreEqual(12, max);
        }
    }
}
