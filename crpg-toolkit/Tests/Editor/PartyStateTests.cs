using System.Linq;
using CRPG.Core;
using CRPG.Party;
using NUnit.Framework;

namespace CRPG.Tests.EditMode
{
    public class PartyStateTests
    {
        [Test]
        public void AddingMembersSelectsThemAndActivatesTheFirst()
        {
            var fighter = RuntimeEntityId.NewId();
            var rogue = RuntimeEntityId.NewId();
            var party = new PartyState();

            party.AddMember(fighter);
            party.AddMember(rogue);

            Assert.AreEqual(2, party.Selected.Count);
            Assert.AreEqual(fighter, party.ActiveCharacter);
        }

        [Test]
        public void SelectNarrowsToOneCharacterAndActivatesIt()
        {
            var fighter = RuntimeEntityId.NewId();
            var rogue = RuntimeEntityId.NewId();
            var party = new PartyState(new[] { fighter, rogue });

            party.Select(rogue);

            Assert.AreEqual(1, party.Selected.Count);
            Assert.IsTrue(party.IsSelected(rogue));
            Assert.AreEqual(rogue, party.ActiveCharacter);
        }

        [Test]
        public void ToggleSelectRemovingActiveReassignsActiveInPartyOrder()
        {
            var fighter = RuntimeEntityId.NewId();
            var rogue = RuntimeEntityId.NewId();
            var party = new PartyState(new[] { fighter, rogue });
            party.Select(rogue);

            party.ToggleSelect(rogue); // deselect the only selected (and active) member

            Assert.IsFalse(party.IsSelected(rogue));
            Assert.AreEqual(RuntimeEntityId.None, party.ActiveCharacter);
        }

        [Test]
        public void ToggleSelectAddingDoesNotStealActiveFromExistingSelection()
        {
            var fighter = RuntimeEntityId.NewId();
            var rogue = RuntimeEntityId.NewId();
            var party = new PartyState(new[] { fighter, rogue });
            party.Select(fighter); // selected = {fighter}, active = fighter

            party.ToggleSelect(rogue); // selected = {fighter, rogue}; active should stay fighter

            Assert.AreEqual(fighter, party.ActiveCharacter);
            Assert.IsTrue(party.IsSelected(rogue));
        }

        [Test]
        public void SetActiveRequiresTheCharacterToAlreadyBeSelected()
        {
            var fighter = RuntimeEntityId.NewId();
            var rogue = RuntimeEntityId.NewId();
            var party = new PartyState(new[] { fighter, rogue });
            party.Select(fighter);

            Assert.Throws<System.InvalidOperationException>(() => party.SetActive(rogue));
        }

        [Test]
        public void CycleActiveWrapsAcrossSelectedMembersOnlyInPartyOrder()
        {
            var fighter = RuntimeEntityId.NewId();
            var rogue = RuntimeEntityId.NewId();
            var wizard = RuntimeEntityId.NewId();
            var party = new PartyState(new[] { fighter, rogue, wizard });
            party.SelectMany(new[] { fighter, wizard }); // rogue excluded; active stays fighter (still in selection)

            Assert.AreEqual(fighter, party.ActiveCharacter);

            party.CycleActiveNext();
            Assert.AreEqual(wizard, party.ActiveCharacter);

            party.CycleActiveNext();
            Assert.AreEqual(fighter, party.ActiveCharacter);

            party.CycleActivePrevious();
            Assert.AreEqual(wizard, party.ActiveCharacter);
        }

        [Test]
        public void RemovingActiveMemberReassignsActiveOrClearsIt()
        {
            var fighter = RuntimeEntityId.NewId();
            var party = new PartyState(new[] { fighter });

            party.RemoveMember(fighter);

            Assert.AreEqual(RuntimeEntityId.None, party.ActiveCharacter);
            Assert.IsFalse(party.IsMember(fighter));
        }

        [Test]
        public void GroupsDefaultToOneGroupAndCanBeSplit()
        {
            var fighter = RuntimeEntityId.NewId();
            var rogue = RuntimeEntityId.NewId();
            var party = new PartyState(new[] { fighter, rogue });

            Assert.AreEqual(PartyState.DefaultGroup, party.GetGroup(fighter));

            party.SetGroup(rogue, "Scouts");
            party.SelectGroup("Scouts");

            Assert.AreEqual(1, party.Selected.Count);
            Assert.IsTrue(party.IsSelected(rogue));
            CollectionAssert.AreEquivalent(new[] { rogue }, party.MembersInGroup("Scouts").ToList());
        }

        [Test]
        public void ChangedFiresOnceForEachOfTheLeafMutatingOperations()
        {
            var fighter = RuntimeEntityId.NewId();
            var rogue = RuntimeEntityId.NewId();
            var party = new PartyState();
            var changeCount = 0;
            party.Changed += () => changeCount++;

            party.AddMember(fighter); // 1
            party.AddMember(rogue); // 2
            party.Select(fighter); // 3
            party.ToggleSelect(rogue); // 4
            party.SetActive(rogue); // 5
            party.CycleActiveNext(); // 6
            party.CycleActivePrevious(); // 7
            party.SetGroup(rogue, "Scouts"); // 8
            party.ClearSelection(); // 9
            party.RemoveMember(fighter); // 10

            Assert.AreEqual(10, changeCount);
        }

        [Test]
        public void ChangedFiresExactlyOnceForSelectAllAndSelectGroupNotTwice()
        {
            var fighter = RuntimeEntityId.NewId();
            var rogue = RuntimeEntityId.NewId();
            var party = new PartyState(new[] { fighter, rogue });
            party.SetGroup(rogue, "Scouts");
            var changeCount = 0;
            party.Changed += () => changeCount++;

            party.SelectAll(); // delegates to SelectMany -- must raise exactly once, not twice
            Assert.AreEqual(1, changeCount);

            party.SelectGroup("Scouts"); // also delegates to SelectMany -- must raise exactly once
            Assert.AreEqual(2, changeCount);
        }

        [Test]
        public void AddingAnAlreadyPresentMemberDoesNotRaiseChanged()
        {
            var fighter = RuntimeEntityId.NewId();
            var party = new PartyState(new[] { fighter });
            var changeCount = 0;
            party.Changed += () => changeCount++;

            party.AddMember(fighter); // early-return no-op

            Assert.AreEqual(0, changeCount);
        }
    }
}
