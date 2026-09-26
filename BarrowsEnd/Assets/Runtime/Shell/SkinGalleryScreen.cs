using System.Reflection;
using CRPG.Actors;
using CRPG.Core;
using CRPG.Party;
using CRPG.UI;
using UnityEngine;
using UnityEngine.UIElements;

namespace BarrowsEnd.Shell
{
    /// <summary>
    /// Task 5.0, Step 4: the art-direction review surface. Every <c>be-*</c> primitive and state side by side, plus
    /// a real <see cref="PartyView"/> (flag off) bound to a real <see cref="PartyPresenter"/> over a small fake
    /// party -- the Step 5 proof that the class-based re-skin contract actually works end to end, not just in
    /// isolation. Static content only (no gameplay); the hover/active states are demonstrable by an actual click in
    /// the Editor, not simulated here.
    /// </summary>
    [RequireComponent(typeof(UIDocument))]
    public sealed class SkinGalleryScreen : MonoBehaviour
    {
        private PartyView partyView;
        private PartyPresenter partyPresenter;
        private ResourceDefinition hpResource;

        private void OnEnable()
        {
            var document = GetComponent<UIDocument>();
            var root = document.rootVisualElement;
            root.Clear();

            var screen = new VisualElement { name = "gallery-screen" };
            screen.AddToClassList("be-screen");
            screen.style.position = Position.Absolute;
            screen.style.left = screen.style.top = screen.style.right = screen.style.bottom = 0;
            screen.style.paddingLeft = screen.style.paddingTop = 16;
            root.Add(screen);

            var rows = new VisualElement();
            screen.Add(rows);

            rows.Add(Section("Panels", BuildPanels()));
            rows.Add(Section("Buttons", BuildButtons()));
            rows.Add(Section("Round buttons / arrows", BuildRoundAndArrows()));
            rows.Add(Section("Bars (25/50/100%)", BuildBars()));
            rows.Add(Section("Slots", BuildSlots()));
            rows.Add(Section("Party HUD (real PartyView + PartyPresenter)", BuildPartyDemo()));
        }

        /// <summary>Bug found at the Gate review: this used to force `flexDirection: Row` onto every section's
        /// content, clobbering `BuildBars()`'s own `Column` setting (it runs after and inline styles are last-write-
        /// wins) -- all six bar rows ended up laid out side by side instead of stacked, each squeezed to a fraction
        /// of its intended width. Every Build*() method already sets its own correct flexDirection; this no longer
        /// overrides it.</summary>
        private static VisualElement Section(string title, VisualElement content)
        {
            var section = new VisualElement { style = { marginBottom = 16 } };
            var label = new Label(title);
            label.AddToClassList("be-label");
            label.AddToClassList("be-label--title");
            section.Add(label);
            content.style.marginTop = 8;
            section.Add(content);
            return section;
        }

        private static VisualElement Part(string partClass, int w, int h)
        {
            var el = new VisualElement { style = { width = w, height = h, marginRight = 12 } };
            el.AddToClassList($"be-part--{partClass}");
            return el;
        }

        private static VisualElement BuildPanels()
        {
            var row = new VisualElement { style = { flexDirection = FlexDirection.Row } };
            foreach (var name in new[] { "panel-wood", "panel-stone", "panel-paper", "panel-inset" })
            {
                var panel = Part(name, 90, 90);
                panel.AddToClassList("be-panel");
                row.Add(panel);
            }

            return row;
        }

        private static VisualElement BuildButtons()
        {
            var row = new VisualElement { style = { flexDirection = FlexDirection.Row, alignItems = Align.Center } };

            var primary = new Button { text = "Confirm" };
            primary.AddToClassList("be-button");
            primary.AddToClassList("be-part--button-primary");
            primary.style.width = 190;
            primary.style.height = 49;
            primary.style.marginRight = 12;
            row.Add(primary);

            var secondary = new Button { text = "Cancel" };
            secondary.AddToClassList("be-button");
            secondary.AddToClassList("be-part--button-secondary");
            secondary.style.width = 190;
            secondary.style.height = 49;
            secondary.style.marginRight = 12;
            row.Add(secondary);

            var disabled = new Button { text = "Disabled" };
            disabled.AddToClassList("be-button");
            disabled.AddToClassList("be-part--button-primary");
            disabled.style.width = 190;
            disabled.style.height = 49;
            disabled.SetEnabled(false);
            row.Add(disabled);

            return row;
        }

        private static VisualElement BuildRoundAndArrows()
        {
            var row = new VisualElement { style = { flexDirection = FlexDirection.Row, alignItems = Align.Center } };

            var round = new VisualElement();
            round.AddToClassList("be-button--round");
            round.AddToClassList("be-part--button-round");
            var check = new VisualElement();
            check.AddToClassList("be-icon");
            check.AddToClassList("be-part--icon-check");
            round.Add(check);
            row.Add(round);

            var left = Part("arrow-left", 22, 21);
            left.AddToClassList("be-arrow");
            row.Add(left);
            var right = Part("arrow-right", 22, 21);
            right.AddToClassList("be-arrow");
            row.Add(right);

            return row;
        }

        private const float BarTrackWidth = 160f;

        private static VisualElement BuildBars()
        {
            var col = new VisualElement { style = { flexDirection = FlexDirection.Column, width = 200 } };
            foreach (var (fillPart, percent) in new[] { ("bar-fill-hp", 25f), ("bar-fill-hp", 50f), ("bar-fill-hp", 100f), ("bar-fill-mana", 66f), ("bar-fill-xp", 80f), ("bar-fill-danger", 40f) })
            {
                var bar = new VisualElement { style = { marginBottom = 6, width = BarTrackWidth } };
                bar.AddToClassList("be-bar");
                bar.AddToClassList("be-part--bar-back-hp");
                var fill = new VisualElement();
                fill.AddToClassList("be-bar__fill");
                fill.AddToClassList($"be-part--{fillPart}");
                // A Length.Percent width here resolved against the fill's own native background-image size (36px),
                // not the track's actual width -- 25% rendered as ~9px, 50% as an 18x18 square ("a green circle").
                // A computed pixel width sidesteps it (same fix as PartyView's hp-bar).
                fill.style.width = BarTrackWidth * (percent / 100f);
                bar.Add(fill);
                col.Add(bar);
            }

            return col;
        }

        private static VisualElement BuildSlots()
        {
            var row = new VisualElement { style = { flexDirection = FlexDirection.Row } };
            var empty = Part("panel-inset", 40, 40);
            empty.AddToClassList("be-slot");
            empty.AddToClassList("be-slot--empty");
            empty.style.marginRight = 8;
            row.Add(empty);

            var selected = Part("panel-inset", 40, 40);
            selected.AddToClassList("be-slot");
            selected.AddToClassList("be-slot--selected");
            row.Add(selected);

            return row;
        }

        /// <summary>The Step 5 proof: a REAL PartyPresenter (not a mock) over a small fake party, bound to a REAL
        /// PartyView with the flag off, so the gallery shows the actual re-skin mechanism working end to end.</summary>
        private VisualElement BuildPartyDemo()
        {
            hpResource = ScriptableObject.CreateInstance<ResourceDefinition>();
            SetPrivateId(hpResource, "resource-hp-gallery");

            var registry = new CharacterRegistry();
            var members = new System.Collections.Generic.List<RuntimeEntityId>();
            var names = new System.Collections.Generic.Dictionary<RuntimeEntityId, string>();
            var seeds = new (string name, int cur, int max)[] { ("Aldric", 14, 14), ("Brenna", 6, 12), ("Corvin", 0, 10), ("Dara", 8, 8) };
            foreach (var (name, cur, max) in seeds)
            {
                var id = RuntimeEntityId.NewId();
                var character = new CharacterState(id);
                character.AddResource(new ResourceState(hpResource.Id, max, cur));
                registry.Register(character);
                members.Add(id);
                names[id] = name;
            }

            var party = new PartyState(members);
            party.Select(members[0]);
            party.SetActive(members[0]);

            partyPresenter = new PartyPresenter(party, registry, hpResource, names);

            var host = new GameObject("gallery-party-view").AddComponent<PartyView>();
            host.transform.SetParent(transform, false);
            host.UsePlaceholderStyle = false;

            // Gate review round 2: a fixed 400px container forced 4 non-shrinking-by-design 140px cards (560px +
            // margins) to flex-shrink down to 88px each, while the hp-bar inside stayed a fixed 120px -- the bar
            // then overflowed the shrunk card. `.party-card` now declares `flex-shrink: 0` (cards never shrink);
            // this container is widened to comfortably fit all four at their real size instead of fighting it.
            var container = new VisualElement { style = { width = 620 } };
            host.Initialize(partyPresenter, container);
            return container;
        }

        private static void SetPrivateId(ResourceDefinition resource, string id)
        {
            var field = typeof(DefinitionAsset).GetField("id", BindingFlags.NonPublic | BindingFlags.Instance);
            field?.SetValue(resource, id);
        }
    }
}
