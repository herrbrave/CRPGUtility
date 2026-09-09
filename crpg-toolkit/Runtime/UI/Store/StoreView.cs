using CRPG.Core;
using UnityEngine;
using UnityEngine.UIElements;

namespace CRPG.UI
{
    /// <summary>
    /// Minimal functional Store screen: a buy list, a sell list, and the party's current gold total -- built
    /// procedurally in code like CampView, for the same reason (first-pass placeholder behind the Presenter/View
    /// boundary, not a polished shop layout).
    /// </summary>
    [RequireComponent(typeof(UIDocument))]
    public sealed class StoreView : MonoBehaviour
    {
        private StorePresenter presenter;
        private UIDocument document;
        private Label goldLabel;
        private VisualElement columns;
        private VisualElement buyContainer;
        private VisualElement sellContainer;

        public void Initialize(StorePresenter storePresenter)
        {
            presenter = storePresenter;
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

            var title = new Label("Store");
            title.style.fontSize = 28;
            title.style.marginBottom = 8;
            root.Add(title);

            goldLabel = new Label();
            goldLabel.style.fontSize = 16;
            goldLabel.style.marginBottom = 16;
            root.Add(goldLabel);

            columns = new VisualElement();
            columns.style.flexDirection = FlexDirection.Row;
            root.Add(columns);

            buyContainer = new VisualElement();
            columns.Add(buyContainer);

            sellContainer = new VisualElement();
            columns.Add(sellContainer);
        }

        // Rebuilds buyContainer/sellContainer as brand-new VisualElements each refresh, rather than Clear()-ing
        // and re-populating the same ones, because the row *count* changes here (every purchase/sale adds or
        // removes a sellable stack -- buying never merges into an existing one). Camp's equivalent rebuild
        // (partyContainer.Clear() + re-add) is safe because its member count never changes between refreshes;
        // Store's does, and reusing the same container across a child-count change is what produced rows
        // rendering on top of each other after a few Buy/Sell clicks.
        private void Refresh()
        {
            goldLabel.text = $"Gold: {presenter.PartyGold}";

            var newBuyContainer = new VisualElement { style = { width = 320, marginRight = 32 } };
            newBuyContainer.Add(new Label("Buy") { style = { fontSize = 18, unityFontStyleAndWeight = FontStyle.Bold, marginBottom = 8 } });
            foreach (var listing in presenter.GetListings()) newBuyContainer.Add(BuildBuyRow(listing));
            SwapContainer(ref buyContainer, newBuyContainer);

            var newSellContainer = new VisualElement { style = { width = 320 } };
            newSellContainer.Add(new Label("Sell") { style = { fontSize = 18, unityFontStyleAndWeight = FontStyle.Bold, marginBottom = 8 } });
            foreach (var owned in presenter.GetSellableItems()) newSellContainer.Add(BuildSellRow(owned));
            SwapContainer(ref sellContainer, newSellContainer);
        }

        private void SwapContainer(ref VisualElement current, VisualElement replacement)
        {
            var index = columns.IndexOf(current);
            columns.Insert(index, replacement);
            columns.Remove(current);
            current = replacement;
        }

        private VisualElement BuildBuyRow(StoreListingSummary listing)
        {
            var row = new VisualElement { style = { flexDirection = FlexDirection.Row, marginBottom = 6, alignItems = Align.Center } };

            var stockText = listing.IsUnlimited ? string.Empty : $" ({listing.RemainingQuantity} left)";
            var label = new Label($"{listing.DisplayName} - {listing.Price}g{stockText}");
            label.style.flexGrow = 1;
            row.Add(label);

            var buyButton = new Button(() => OnBuyClicked(listing.ItemId)) { text = "Buy" };
            buyButton.SetEnabled(listing.IsUnlimited || listing.RemainingQuantity > 0);
            row.Add(buyButton);

            return row;
        }

        private VisualElement BuildSellRow(OwnedItemSummary owned)
        {
            var row = new VisualElement { style = { flexDirection = FlexDirection.Row, marginBottom = 6, alignItems = Align.Center } };

            var label = new Label($"{owned.DisplayName} x{owned.Quantity} - {owned.SellPrice}g");
            label.style.flexGrow = 1;
            row.Add(label);

            var sellButton = new Button(() => OnSellClicked(owned.InstanceId)) { text = "Sell" };
            row.Add(sellButton);

            return row;
        }

        private void OnBuyClicked(DefinitionId itemId)
        {
            presenter.Buy(itemId);
            Refresh();
        }

        private void OnSellClicked(RuntimeEntityId instanceId)
        {
            presenter.Sell(instanceId);
            Refresh();
        }
    }
}
