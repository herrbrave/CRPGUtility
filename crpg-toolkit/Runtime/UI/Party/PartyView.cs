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
    /// (only when the flag is false) an <c>hp-bar</c> + <c>hp-bar__fill</c> child. The bar's WIDTH is entirely the
    /// consumer's/skin's to set (inline or via USS) -- this view never forces it, so a strip-shaped HUD row and a
    /// tall portrait card can size the same bar completely differently. The fill reacts to whatever width the bar
    /// actually resolves to via <see cref="GeometryChangedEvent"/>, recomputing <c>percent * the bar's real
    /// resolved width</c> every time it changes (Task 5.1 Gate review: an earlier fixed-pixel constant meant the
    /// bar could never be resized by a skin without the fill going wrong again, the same bug class as the
    /// Length.Percent-resolves-against-the-wrong-thing bug Task 5.0's Gate review found). Consequence: EditMode
    /// (no layout pass ever runs there) never fires that event, so the fill's exact pixel width is only meaningful
    /// in a PlayMode test -- EditMode checks structure/display state only (see PartyViewSkinContractTests vs.
    /// PartyViewGeometryTests).</para>
    /// </summary>
    public sealed class PartyView : MonoBehaviour
    {
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

        /// <summary>Task 5.1 Gate review: a session load swaps in a brand-new PartyState/ICharacterRegistry
        /// (PartyPresenter.Rebind updates the presenter's own references), but this view's <c>Party.Changed</c>
        /// subscription from <see cref="Initialize"/> is NOT re-raised by that -- left alone, the view stays
        /// subscribed to the discarded, disposed PartyState forever and never refreshes from a world-driven
        /// selection change again. Call this (not <see cref="Initialize"/> again) after rebinding the presenter:
        /// cheaper than rebuilding the DOM, and leaves no dangling handler on the old, dead PartyState.</summary>
        public void Rebind(PartyPresenter newPresenter)
        {
            if (presenter != null) presenter.Party.Changed -= Refresh;
            presenter = newPresenter;
            presenter.Party.Changed += Refresh;
            Refresh();
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
            // Found live (user-reported, Phase 5 Gate E follow-up): with the flag off, this had NO background at
            // all -- only an inline placeholder colour existed, gated below, and no be-part--panel-* class was
            // ever added to replace it. The exact same gap already found and fixed twice this phase (be-slot's
            // missing background in Task 5.2, the backpack icons' non-integer scale) -- a real look was never
            // actually wired up here either. be-part--panel-stone matches HudBootstrap's own system-menu panel,
            // so the HUD's two corners read as one visual family.
            container.AddToClassList("be-panel");
            container.AddToClassList("be-part--panel-stone");
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
            // Task 5.1 Gate review (blocker): this was unconditional, so a consumer's USS `.party-roster
            // { flex-direction: column }` rule was always a no-op (inline beats a stylesheet) -- the exact bug
            // class the Task 5.0 gallery review already caught once. Gated so a re-skin can actually choose.
            if (usePlaceholderStyle) rosterContainer.style.flexDirection = FlexDirection.Row;
            container.Add(rosterContainer);
        }

        /// <summary>Rebuilds the roster from the presenter now. The view only refreshes by itself when the party's
        /// membership/selection changes -- hit points changing (combat damage, healing, a rest) is invisible to it,
        /// so a host that knows the numbers moved calls this.</summary>
        public void RefreshNow() => Refresh();

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
                // No width set here, deliberately (Task 5.1 Gate review): the bar's width is entirely the
                // consumer's/skin's to decide (inline or USS) -- a tall portrait card and a wide HUD strip can
                // size it completely differently. The fill tracks whatever that resolves to, below.
                var fill = new VisualElement { name = "hp-bar__fill" };
                fill.AddToClassList("be-bar__fill");
                fill.AddToClassList("be-part--bar-fill-hp");
                var percent = member.MaxHp > 0 ? Mathf.Clamp01((float)member.CurrentHp / member.MaxHp) : 0f;
                fill.style.display = percent <= 0f ? DisplayStyle.None : DisplayStyle.Flex;
                // Recomputes on every layout change to the bar (not just once): a bar whose own width changes
                // later (a window resize, a different skin swapped in) keeps the fill correctly proportioned.
                // This never fires in EditMode (no layout pass runs there) -- see the class doc comment.
                bar.RegisterCallback<GeometryChangedEvent>(evt => fill.style.width = evt.newRect.width * percent);
                bar.Add(fill);
                card.Add(bar);
            }

            // Task 5.1 Gate review (a real, pre-existing bug -- PartyView had no tests at all before Task 5.0,
            // so this was never exercised through a real click): a bare RegisterCallback<ClickEvent> never fires
            // from a genuine pointer press+release on a plain VisualElement -- there is no Clickable manipulator
            // here to synthesize one, unlike Button (which owns one internally). This never worked for an actual
            // mouse click either, not just in a test. A Clickable manipulator is what makes press+release-on-
            // target register as a click at all; its callback receives the raw EventBase (the PointerUpEvent),
            // read as IPointerEvent for the shift/double-click info the original ClickEvent handler used.
            card.AddManipulator(new Clickable(evt => OnPortraitClicked(member.Id, evt)));
            return card;
        }

        private void OnPortraitClicked(CRPG.Core.RuntimeEntityId id, EventBase evt)
        {
            var shiftKey = evt is IPointerEvent pointerEvent && pointerEvent.shiftKey;
            var clickCount = evt is IPointerEvent pe ? pe.clickCount : 1;

            if (shiftKey) presenter.ToggleSelect(id);
            else if (clickCount >= 2) presenter.SetActive(id);
            else presenter.SelectCharacter(id);

            Refresh();
        }
    }
}
