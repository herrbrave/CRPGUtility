using UnityEngine;
using UnityEngine.UIElements;

namespace CRPG.UI
{
    /// <summary>
    /// Minimal functional party roster/selection panel: click a portrait to select-and-focus, shift-click to
    /// toggle, double-click to re-focus an already-selected member. Built procedurally, same first-pass
    /// placeholder convention as CampView/StoreView. Builds into an externally-supplied container region
    /// (see HudRoot) rather than owning its own UIDocument -- Task 3.6's HUD consolidation.
    /// </summary>
    public sealed class PartyView : MonoBehaviour
    {
        private PartyPresenter presenter;
        private VisualElement container;
        private VisualElement rosterContainer;
        public ITextLocalizer Localizer { get; set; }

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
            container.style.paddingLeft = 16;
            container.style.paddingTop = 16;
            container.style.paddingRight = 16;
            container.style.paddingBottom = 16;
            container.style.backgroundColor = new Color(0f, 0f, 0f, 0.5f);
            container.style.color = Color.white;

            var title = new Label("Party");
            title.style.fontSize = 28;
            title.style.marginBottom = 16;
            container.Add(title);

            rosterContainer = new VisualElement();
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
            var card = new VisualElement();
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

            var name = new Label(TextKeyResolver.Resolve(member.DisplayName, Localizer));
            name.style.fontSize = 16;
            name.style.unityFontStyleAndWeight = FontStyle.Bold;
            card.Add(name);
            card.Add(new Label($"HP {member.CurrentHp} / {member.MaxHp}"));

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
