using System.Collections.Generic;
using System.Linq;
using CRPG.Actors;
using CRPG.Core;
using CRPG.Inventory;
using CRPG.Party;
using CRPG.UI;
using NUnit.Framework;

namespace CRPG.Tests.EditMode
{
    public class CampPresenterTests
    {
        private static (CampPresenter presenter, PartyState party, RuntimeEntityId fighterId, ResourceDefinition hp) BuildScenario(
            RulesDatabase rulesDatabase, CharacterRegistry registry, PartyInventoryState inventory)
        {
            var hp = TestDefinitionFactory.CreateResourceDefinition("resource-hp", "Hit Points", RestTrigger.LongRest);
            var secondWind = TestDefinitionFactory.CreateResourceDefinition("resource-second-wind", "Second Wind", RestTrigger.Both);
            rulesDatabase.Register(hp);
            rulesDatabase.Register(secondWind);

            var mainHand = TestDefinitionFactory.CreateEquipmentSlotDefinition("slot-mainhand", "Main Hand");
            var sword = TestDefinitionFactory.CreateItemDefinition("item-sword", "Longsword", equipmentSlotId: mainHand.Id);
            rulesDatabase.Register(mainHand);
            rulesDatabase.Register(sword);

            var fighter = new CharacterState(RuntimeEntityId.NewId());
            fighter.AddResource(new ResourceState(hp.Id, max: 28, current: 10));
            fighter.AddResource(new ResourceState(secondWind.Id, max: 1, current: 0));
            registry.Register(fighter);

            var swordInstance = new ItemInstance(RuntimeEntityId.NewId(), sword.Id);
            inventory.AddItem(swordInstance, ItemLocation.Equipped(fighter.Id, mainHand.Id));

            var party = new PartyState(new[] { fighter.Id });
            var names = new Dictionary<RuntimeEntityId, string> { [fighter.Id] = "Aldric" };
            var restService = new RestService(registry, rulesDatabase);

            var presenter = new CampPresenter(party, registry, rulesDatabase, inventory, restService, hp, names);
            return (presenter, party, fighter.Id, hp);
        }

        [Test]
        public void GetPartyMembersReturnsNameAndHitPoints()
        {
            var rulesDatabase = new RulesDatabase();
            var registry = new CharacterRegistry();
            var inventory = new PartyInventoryState();
            var (presenter, _, _, _) = BuildScenario(rulesDatabase, registry, inventory);

            var members = presenter.GetPartyMembers();

            Assert.AreEqual(1, members.Count);
            Assert.AreEqual("Aldric", members[0].DisplayName);
            Assert.AreEqual(10, members[0].CurrentHitPoints);
            Assert.AreEqual(28, members[0].MaxHitPoints);
        }

        [Test]
        public void GetPartyMembersIncludesEquippedSlots()
        {
            var rulesDatabase = new RulesDatabase();
            var registry = new CharacterRegistry();
            var inventory = new PartyInventoryState();
            var (presenter, _, _, _) = BuildScenario(rulesDatabase, registry, inventory);

            var members = presenter.GetPartyMembers();

            Assert.AreEqual(1, members[0].EquippedSlots.Count);
            Assert.AreEqual("Main Hand", members[0].EquippedSlots[0].SlotName);
            Assert.AreEqual("Longsword", members[0].EquippedSlots[0].ItemName);
        }

        [Test]
        public void ConfirmLongRestRestoresTheWholePartysRestTriggeredResources()
        {
            var rulesDatabase = new RulesDatabase();
            var registry = new CharacterRegistry();
            var inventory = new PartyInventoryState();
            var (presenter, _, fighterId, hp) = BuildScenario(rulesDatabase, registry, inventory);

            presenter.ConfirmLongRest();

            registry.TryGetCharacter(fighterId, out var fighter);
            fighter.TryGetResource(hp.Id, out var hpState);
            Assert.AreEqual(28, hpState.Current); // fully restored
        }
    }
}
