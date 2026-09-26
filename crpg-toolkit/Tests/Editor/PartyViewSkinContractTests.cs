using System.Collections.Generic;
using System.Linq;
using CRPG.Actors;
using CRPG.Core;
using CRPG.Party;
using CRPG.UI;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.UIElements;

namespace CRPG.Tests.EditMode
{
    /// <summary>Task 5.0, Step 5: PartyView had no prior tests at all (verified by search -- this is a NEW
    /// baseline, not a preserved one). EditMode has no layout/style pass, so this checks structure/classes/inline
    /// values only, never resolvedStyle (see LayeredSpritePreviewTests's own precedent for the same constraint).</summary>
    public class PartyViewSkinContractTests
    {
        private GameObject host;
        private PartyView view;
        private PartyPresenter presenter;
        private ResourceDefinition hp;

        [SetUp]
        public void SetUp()
        {
            hp = ScriptableObject.CreateInstance<ResourceDefinition>();
            typeof(DefinitionAsset).GetField("id", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)
                ?.SetValue(hp, "resource-hp-test");

            var registry = new CharacterRegistry();
            var members = new List<RuntimeEntityId>();
            var names = new Dictionary<RuntimeEntityId, string>();
            foreach (var (name, cur, max) in new (string, int, int)[] { ("Aldric", 10, 10), ("Brenna", 0, 8) })
            {
                var id = RuntimeEntityId.NewId();
                var character = new CharacterState(id);
                character.AddResource(new ResourceState(hp.Id, max, cur));
                registry.Register(character);
                members.Add(id);
                names[id] = name;
            }

            var party = new PartyState(members);
            party.Select(members[0]);
            party.SetActive(members[0]);
            presenter = new PartyPresenter(party, registry, hp, names);

            host = new GameObject("PartyViewTest");
        }

        [TearDown]
        public void TearDown()
        {
            if (host != null) Object.DestroyImmediate(host);
            if (hp != null) Object.DestroyImmediate(hp);
        }

        [Test]
        public void WithTheFlagOnTheStructureAndInlineLookMatchTodaysPlaceholder()
        {
            view = host.AddComponent<PartyView>();
            view.UsePlaceholderStyle = true;
            var container = new VisualElement();
            view.Initialize(presenter, container);

            Assert.AreEqual("party-panel", container.name);
            Assert.IsTrue(container.ClassListContains("party-panel"));
            Assert.AreEqual(new Color(0f, 0f, 0f, 0.5f), container.style.backgroundColor.value, "the exact placeholder colour, unchanged");

            var title = container.Q<Label>(name: "party-title");
            Assert.IsNotNull(title);
            Assert.AreEqual(28, title.style.fontSize.value.value, "the exact placeholder font size, unchanged");

            var roster = container.Q(name: "party-roster");
            var cards = roster.Children().ToList();
            Assert.AreEqual(2, cards.Count);
            Assert.IsNull(cards[0].Q(name: "hp-bar"), "no hp-bar child when the flag is on -- the flag-true look has no extra invisible child");
            Assert.AreEqual(140, cards[0].style.width.value.value, "the exact placeholder card width, unchanged");
        }

        [Test]
        public void WithTheFlagOffEveryStableNameAndClassIsPresent()
        {
            view = host.AddComponent<PartyView>();
            view.UsePlaceholderStyle = false;
            var container = new VisualElement();
            view.Initialize(presenter, container);

            var roster = container.Q(name: "party-roster");
            var cards = roster.Children().ToList();
            Assert.AreEqual(2, cards.Count);

            foreach (var card in cards)
            {
                Assert.IsTrue(card.ClassListContains("party-card"));
                var name = card.Q<Label>(name: "party-card-name");
                Assert.IsNotNull(name);
                Assert.IsTrue(name.ClassListContains("party-card-name"));
                Assert.IsNotNull(card.Q<Label>(name: "party-card-hp"));
                Assert.IsNotNull(card.Q(name: "hp-bar"), "an hp-bar child exists when the flag is off");
                Assert.IsNotNull(card.Q(name: "hp-bar__fill"));
            }

            var active = cards.First(c => c.ClassListContains("party-card--active"));
            Assert.AreEqual("Aldric", active.Q<Label>(name: "party-card-name").text);

            // 0 HP hides the fill (0% can't show two 9px caps under an 18px bar -- Step 3's empty-bar rule).
            var brenna = cards.First(c => c.Q<Label>(name: "party-card-name").text == "Brenna");
            Assert.AreEqual(DisplayStyle.None, brenna.Q(name: "hp-bar__fill").style.display.value);

            var aldric = cards.First(c => c.Q<Label>(name: "party-card-name").text == "Aldric");
            Assert.AreEqual(DisplayStyle.Flex, aldric.Q(name: "hp-bar__fill").style.display.value);

            // Task 5.1 Gate review (T-2): the fill's WIDTH is no longer set here at all -- it is computed from
            // the bar's real resolved width via GeometryChangedEvent, which never fires in EditMode (no layout
            // pass runs here). A pixel-width assertion used to live here; it moved to a PlayMode test
            // (PartyViewGeometryTests), which is the only context where "the bar's real resolved width" means
            // anything. Also: the bar itself must carry NO inline width -- that is now entirely the consumer's/
            // skin's to set (T-2's whole point).
            Assert.AreEqual(StyleKeyword.Null, aldric.Q(name: "hp-bar").style.width.keyword, "PartyView must not force the bar's width inline -- that would make a skin's own width rule a no-op, the exact bug T-2 fixed");
        }

        [Test]
        public void WithTheFlagOffTheRosterHasNoInlineFlexDirectionSoAConsumersUssCanChooseIt()
        {
            // Task 5.1 Gate review (T-1, blocker): this used to be forced to Row unconditionally, so a consumer's
            // `.party-roster { flex-direction: column }` rule was always a no-op (inline beats a stylesheet).
            view = host.AddComponent<PartyView>();
            view.UsePlaceholderStyle = false;
            var container = new VisualElement();
            view.Initialize(presenter, container);

            var roster = container.Q(name: "party-roster");
            Assert.AreEqual(StyleKeyword.Null, roster.style.flexDirection.keyword, "no inline flexDirection when the flag is off -- a consumer's USS must be free to set it");
        }

        [Test]
        public void WithTheFlagOnTheRosterKeepsItsExactPlaceholderRowDirection()
        {
            view = host.AddComponent<PartyView>();
            view.UsePlaceholderStyle = true;
            var container = new VisualElement();
            view.Initialize(presenter, container);

            var roster = container.Q(name: "party-roster");
            Assert.AreEqual(FlexDirection.Row, roster.style.flexDirection.value, "the flag-true look is unchanged");
        }

        [Test]
        public void RebindSwapsThePresenterAndStopsListeningToTheOldPartysChangedEvent()
        {
            // Task 5.1 Gate review (T-3): a session load swaps in a brand-new PartyState/ICharacterRegistry.
            // Without Rebind, the view would stay subscribed to the OLD (now discarded) PartyState forever.
            view = host.AddComponent<PartyView>();
            view.UsePlaceholderStyle = false;
            var container = new VisualElement();
            view.Initialize(presenter, container);

            var oldParty = presenter.Party;

            var registry2 = new CharacterRegistry();
            var members2 = new List<RuntimeEntityId>();
            var names2 = new Dictionary<RuntimeEntityId, string>();
            var id2 = RuntimeEntityId.NewId();
            var character2 = new CharacterState(id2);
            character2.AddResource(new ResourceState(hp.Id, 5, 5));
            registry2.Register(character2);
            members2.Add(id2);
            names2[id2] = "Corvin";
            var party2 = new PartyState(members2);
            party2.Select(id2);
            party2.SetActive(id2);
            var presenter2 = new PartyPresenter(party2, registry2, hp, names2);

            view.Rebind(presenter2);

            var rosterAfterRebind = container.Q(name: "party-roster");
            Assert.AreEqual(1, rosterAfterRebind.Children().Count(), "the view now shows the NEW presenter's single member");
            Assert.AreEqual("Corvin", rosterAfterRebind.Q<Label>(name: "party-card-name").text);

            // A change on the OLD (discarded) party must NOT refresh the view anymore.
            oldParty.ToggleSelect(oldParty.Members[0]);
            Assert.AreEqual(1, container.Q(name: "party-roster").Children().Count(), "the old party's Changed event must be unsubscribed after Rebind");

            // A change on the NEW party MUST refresh the view.
            party2.ToggleSelect(id2);
            var refreshedCard = container.Q(name: "party-roster").Q(name: $"party-card-{id2}");
            Assert.IsFalse(refreshedCard.ClassListContains("party-card--selected"), "the new party's Changed event must still refresh the view (toggled off)");
        }
    }
}
