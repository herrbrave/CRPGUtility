using UnityEngine;
using UnityEngine.UIElements;

namespace CRPG.UI
{
    /// <summary>
    /// Minimal functional Camp screen: party list with per-member equipped slots and HP, one Rest button.
    /// Built procedurally in code rather than a hand-authored .uxml/.uss, since this is a first-pass
    /// placeholder -- spec's Presenter -&gt; View boundary means a polished paper-doll layout can replace this
    /// later without CampPresenter (or anything upstream of it) changing.
    /// </summary>
    [RequireComponent(typeof(UIDocument))]
    public sealed class CampView : MonoBehaviour
    {
        private CampPresenter presenter;
        private UIDocument document;
        private VisualElement partyContainer;

        public void Initialize(CampPresenter campPresenter)
        {
            presenter = campPresenter;
            document = GetComponent<UIDocument>();
            BuildLayout();
            Refresh();
        }

        private void BuildLayout()
        {
            var root = document.rootVisualElement;
            root.Clear();
            root.style.flexGrow = 1;
            root.style.paddingLeft = 24;
            root.style.paddingTop = 24;
            root.style.paddingRight = 24;

            var title = new Label("Camp");
            title.style.fontSize = 28;
            title.style.marginBottom = 16;
            root.Add(title);

            partyContainer = new VisualElement();
            partyContainer.style.flexDirection = FlexDirection.Row;
            root.Add(partyContainer);

            var restButton = new Button(OnRestClicked) { text = "Rest (Long)" };
            restButton.style.marginTop = 24;
            restButton.style.width = 160;
            restButton.style.height = 32;
            root.Add(restButton);
        }

        private void Refresh()
        {
            partyContainer.Clear();
            foreach (var member in presenter.GetPartyMembers()) partyContainer.Add(BuildMemberCard(member));
        }

        private static VisualElement BuildMemberCard(PartyMemberSummary member)
        {
            var card = new VisualElement();
            card.style.width = 200;
            card.style.marginRight = 16;
            card.style.paddingLeft = 12;
            card.style.paddingTop = 12;
            card.style.paddingRight = 12;
            card.style.paddingBottom = 12;
            card.style.borderTopWidth = 1;
            card.style.borderBottomWidth = 1;
            card.style.borderLeftWidth = 1;
            card.style.borderRightWidth = 1;
            card.style.borderTopColor = Color.gray;
            card.style.borderBottomColor = Color.gray;
            card.style.borderLeftColor = Color.gray;
            card.style.borderRightColor = Color.gray;

            var name = new Label(member.DisplayName);
            name.style.fontSize = 18;
            name.style.unityFontStyleAndWeight = FontStyle.Bold;
            card.Add(name);

            var hp = new Label($"HP {member.CurrentHitPoints} / {member.MaxHitPoints}");
            hp.style.marginBottom = 8;
            card.Add(hp);

            if (member.EquippedSlots.Count == 0)
            {
                card.Add(new Label("(nothing equipped)"));
            }
            else
            {
                foreach (var slot in member.EquippedSlots) card.Add(new Label($"{slot.SlotName}: {slot.ItemName}"));
            }

            return card;
        }

        private void OnRestClicked()
        {
            presenter.ConfirmLongRest();
            Refresh();
        }
    }
}
