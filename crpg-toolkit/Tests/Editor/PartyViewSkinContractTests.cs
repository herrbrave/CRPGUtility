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
            Assert.AreEqual(100f, aldric.Q(name: "hp-bar__fill").style.width.value.value, 0.01f, "10/10 HP is a full-width fill");
        }
    }
}
