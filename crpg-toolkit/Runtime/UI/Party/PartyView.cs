using UnityEngine;
using UnityEngine.UIElements;

namespace CRPG.UI
{
    /// <summary>
    /// Minimal functional party roster/selection panel: click a portrait to select-and-focus, shift-click to
    /// toggle, double-click to re-focus an already-selected member. Built procedurally, same first-pass
    /// placeholder convention as CampView/StoreView. Builds into an externally-supplied container region
    /// (see HudRoot) rather than owning its own UIDocument -- Task 3.6's HUD consolidation.
    /// <para><b>Contract for a re-skin (Phase 5 Task 5.0), the same shape as <c>CharacterCreatorScreen</c>'s:</b>
    /// every element carries a stable <c>name</c> and a semantic USS class; the placeholder LOOK (inline colours,
    /// borders, sizes) applies only while <see cref="UsePlaceholderStyle"/> is true, because an inline style beats
    /// any USS rule. A consumer that supplies its own stylesheet through <see cref="Initialize"/>'s panel/theme
    /// sets it false. Names/classes: <c>party-panel</c>, <c>party-title</c>, <c>party-roster</c>, <c>party-card</c>
    /// (+ <c>party-card--active</c>/<c>party-card--selected</c>), <c>party-card-name</c>, <c>party-card-hp</c>, and
    /// (only when the flag is false) an <c>hp-bar</c> + <c>hp-bar__fill</c> child sized by a computed inline pixel
    /// width (a Length.Percent width on the fill resolved against the fill's own native background-image size, not
    /// the bar's actual width -- a real bug found at Gate review, fixed by computing pixels directly) -- built
    /// only in that case so the flag-true look stays exactly what it always was (no extra child to account for).</para>
    /// </summary>
    public sealed class PartyView : MonoBehaviour
    {
        private const float HpBarWidth = 120f;

        [Tooltip("Applies the plain built-in look (inline colours/borders). Turn off when a StyleSheet supplies the look (inline styles beat USS).")]
        [SerializeField] private bool usePlaceholderStyle = true;

        private PartyPresenter presenter;
        private VisualElement container;
        private VisualElement rosterContainer;
        public ITextLocalizer Localizer { get; set; }

        /// <summary>Test/consumer hook: set before <see cref="Initialize"/> builds the layout.</summary>
        public bool UsePlaceholderStyle
        {
            get => usePlaceholderStyle;
            set => usePlaceholderStyle = value;
        }

        public void Initialize(PartyPresenter partyPresenter, VisualElement hostContainer)
        {
            if (presenter != null) presenter.Party.Changed -= Refresh;

            presenter = partyPresenter;
            container = hostContainer;
            BuildLayout();
            Refresh();

            // Task 3.6: PartyState is mutated both by this view's own commands AND externally (world
            // click/drag-box via PartyExplorationController) -- unlike Camp/Store, refreshing only after our
            // own button clicks would leave the panel stale after a world-driven selection change.
            presenter.Party.Changed += Refresh;
        }

        private void OnDisable()
        {
            if (presenter != null) presenter.Party.Changed -= Refresh;
        }

        private void BuildLayout()
        {
            container.Clear();
            container.name = "party-panel";
            container.AddToClassList("party-panel");
            if (usePlaceholderStyle)
            {
                container.style.paddingLeft = 16;
                container.style.paddingTop = 16;
                container.style.paddingRight = 16;
                container.style.paddingBottom = 16;
                container.style.backgroundColor = new Color(0f, 0f, 0f, 0.5f);
                container.style.color = Color.white;
            }

            var title = new Label("Party") { name = "party-title" };
            title.AddToClassList("party-title");
            if (usePlaceholderStyle)
            {
                title.style.fontSize = 28;
                title.style.marginBottom = 16;
            }
            container.Add(title);

            rosterContainer = new VisualElement { name = "party-roster" };
            rosterContainer.AddToClassList("party-roster");
            rosterContainer.style.flexDirection = FlexDirection.Row;
            container.Add(rosterContainer);
        }

        private void Refresh()
        {
            rosterContainer.Clear();
            foreach (var member in presenter.GetMembers()) rosterContainer.Add(BuildPortrait(member));
        }

        private VisualElement BuildPortrait(PartyMemberSelectionSummary member)
        {
            var card = new VisualElement { name = $"party-card-{member.Id}" };
            card.AddToClassList("party-card");
            card.EnableInClassList("party-card--active", member.IsActive);
            card.EnableInClassList("party-card--selected", member.IsSelected);

            if (usePlaceholderStyle)
            {
                card.style.width = 140;
                card.style.marginRight = 12;
                card.style.paddingLeft = 8;
                card.style.paddingTop = 8;
                card.style.paddingRight = 8;
                card.style.paddingBottom = 8;
                card.style.borderTopWidth = member.IsActive ? 3 : 1;
                card.style.borderBottomWidth = member.IsActive ? 3 : 1;
                card.style.borderLeftWidth = member.IsActive ? 3 : 1;
                card.style.borderRightWidth = member.IsActive ? 3 : 1;
                var borderColor = member.IsSelected ? Color.yellow : Color.gray;
                card.style.borderTopColor = borderColor;
                card.style.borderBottomColor = borderColor;
                card.style.borderLeftColor = borderColor;
                card.style.borderRightColor = borderColor;
            }

            var name = new Label(TextKeyResolver.Resolve(member.DisplayName, Localizer)) { name = "party-card-name" };
            name.AddToClassList("party-card-name");
            if (usePlaceholderStyle)
            {
                name.style.fontSize = 16;
                name.style.unityFontStyleAndWeight = FontStyle.Bold;
            }
            card.Add(name);

            var hp = new Label($"HP {member.CurrentHp} / {member.MaxHp}") { name = "party-card-hp" };
            hp.AddToClassList("party-card-hp");
            card.Add(hp);

            // The hp-bar child is built ONLY when a stylesheet will actually skin it -- with the flag true, the
            // placeholder look stays exactly what it always was, with no extra (invisible-but-present) child.
            if (!usePlaceholderStyle)
            {
                var bar = new VisualElement { name = "hp-bar" };
                bar.AddToClassList("be-bar");
                bar.AddToClassList("be-part--bar-back-hp");
                bar.style.width = HpBarWidth;
                var fill = new VisualElement { name = "hp-bar__fill" };
                fill.AddToClassList("be-bar__fill");
                fill.AddToClassList("be-part--bar-fill-hp");
                var percent = member.MaxHp > 0 ? Mathf.Clamp01((float)member.CurrentHp / member.MaxHp) : 0f;
                // A Length.Percent width on an absolutely-positioned child resolved against the fill's own
                // native background-image size (36px), not the bar's actual width -- 25% rendered as ~9px, 50%
                // as an 18x18 square. A computed pixel width sidesteps whatever layout-resolution quirk that is.
                fill.style.width = HpBarWidth * percent;
                fill.style.display = percent <= 0f ? DisplayStyle.None : DisplayStyle.Flex;
                bar.Add(fill);
                card.Add(bar);
            }

            card.RegisterCallback<ClickEvent>(evt => OnPortraitClicked(member.Id, evt));
            return card;
        }

        private void OnPortraitClicked(CRPG.Core.RuntimeEntityId id, ClickEvent evt)
        {
            if (evt.shiftKey) presenter.ToggleSelect(id);
            else if (evt.clickCount >= 2) presenter.SetActive(id);
            else presenter.SelectCharacter(id);

            Refresh();
        }
    }
}
