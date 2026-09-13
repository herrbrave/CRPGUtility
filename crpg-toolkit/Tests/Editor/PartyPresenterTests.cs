using System.Collections.Generic;
using CRPG.Actors;
using CRPG.Core;
using CRPG.Party;
using CRPG.UI;
using NUnit.Framework;

namespace CRPG.Tests.EditMode
{
    public class PartyPresenterTests
    {
        private static (PartyPresenter presenter, PartyState party, RuntimeEntityId fighter, RuntimeEntityId rogue) BuildScenario(
            RulesDatabase rulesDatabase, CharacterRegistry registry)
        {
            var hp = TestDefinitionFactory.CreateResourceDefinition("resource-hp", "Hit Points", RestTrigger.LongRest);
            rulesDatabase.Register(hp);

            var fighter = new CharacterState(RuntimeEntityId.NewId());
            fighter.AddResource(new ResourceState(hp.Id, max: 28, current: 10));
            registry.Register(fighter);

            var rogue = new CharacterState(RuntimeEntityId.NewId());
            rogue.AddResource(new ResourceState(hp.Id, max: 18, current: 18));
            registry.Register(rogue);

            var party = new PartyState(new[] { fighter.Id, rogue.Id });
            var names = new Dictionary<RuntimeEntityId, string> { [fighter.Id] = "Aldric", [rogue.Id] = "Shade" };
            var presenter = new PartyPresenter(party, registry, hp, names);
            return (presenter, party, fighter.Id, rogue.Id);
        }

        [Test]
        public void GetMembersReturnsSelectionAndHitPoints()
        {
            var (presenter, party, fighter, _) = BuildScenario(new RulesDatabase(), new CharacterRegistry());
            party.Select(fighter);

            var members = presenter.GetMembers();

            Assert.AreEqual(2, members.Count);
            var fighterSummary = members[0];
            Assert.AreEqual("Aldric", fighterSummary.DisplayName.Fallback);
            Assert.IsTrue(fighterSummary.IsSelected);
            Assert.IsTrue(fighterSummary.IsActive);
            Assert.AreEqual(10, fighterSummary.CurrentHp);
            Assert.AreEqual(28, fighterSummary.MaxHp);
        }

        [Test]
        public void SetActiveOnAnUnselectedMemberReturnsNotSelectedInsteadOfThrowing()
        {
            var (presenter, party, fighter, rogue) = BuildScenario(new RulesDatabase(), new CharacterRegistry());
            party.Select(fighter); // rogue is not selected

            var result = presenter.SetActive(rogue);

            Assert.AreEqual(PartyCommandOutcome.NotSelected, result.Outcome);
            Assert.AreEqual(fighter, party.ActiveCharacter); // unchanged
        }

        [Test]
        public void SetActiveOnASelectedMemberSucceeds()
        {
            var (presenter, party, fighter, rogue) = BuildScenario(new RulesDatabase(), new CharacterRegistry());
            party.SelectMany(new[] { fighter, rogue });

            var result = presenter.SetActive(rogue);

            Assert.AreEqual(PartyCommandOutcome.Success, result.Outcome);
            Assert.AreEqual(rogue, party.ActiveCharacter);
        }

        [Test]
        public void ToggleSelectAndCycleActiveDelegateStraightToPartyState()
        {
            var (presenter, party, fighter, rogue) = BuildScenario(new RulesDatabase(), new CharacterRegistry());
            party.Select(fighter);

            presenter.ToggleSelect(rogue);
            Assert.IsTrue(party.IsSelected(rogue));

            presenter.CycleActiveNext();
            Assert.AreEqual(rogue, party.ActiveCharacter);

            presenter.CycleActivePrevious();
            Assert.AreEqual(fighter, party.ActiveCharacter);
        }

        [Test]
        public void RebindSwapsInANewPartyAndRegistry()
        {
            var (presenter, _, _, _) = BuildScenario(new RulesDatabase(), new CharacterRegistry());

            var newRegistry = new CharacterRegistry();
            var newHp = TestDefinitionFactory.CreateResourceDefinition("resource-hp-2", "Hit Points", RestTrigger.LongRest);
            var newFighter = new CharacterState(RuntimeEntityId.NewId());
            newFighter.AddResource(new ResourceState(newHp.Id, max: 12, current: 12));
            newRegistry.Register(newFighter);
            var newParty = new PartyState(new[] { newFighter.Id });

            presenter.Rebind(newParty, newRegistry);
            var members = presenter.GetMembers();

            Assert.AreEqual(1, members.Count);
            Assert.AreEqual(newFighter.Id, members[0].Id);
        }
    }
}
