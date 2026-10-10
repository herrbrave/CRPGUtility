using System;
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
        private Label statusLabel;
        private VisualElement columns;
        private VisualElement buyContainer;
        private VisualElement sellContainer;

        /// <summary>Fired once the player leaves the store. No consumer needed this before Task 3.4's
        /// conversation store hand-off -- StoreView previously had no way to close at all.</summary>
        public event Action Closed;

        private bool plainStyle;

        /// <param name="plainStyle">True shows a self-contained dark panel with light text (a game with no USS skin); false keeps the default look that a game's own skin paints over (Barrow's End).</param>
        public void Initialize(StorePresenter storePresenter, bool plainStyle = false)
        {
            this.plainStyle = plainStyle;
            presenter = storePresenter;
            document = GetComponent<UIDocument>();
            BuildLayout();
            Refresh();
        }

        // Task 3.1: registers with the shared pointer-capture provider (CRPG.UI.Input) so a world click
        // landing on this panel's own UI Toolkit elements is correctly suppressed. GetComponent directly, not
        // the `document` field -- OnEnable can run before an external Initialize() call sets it.
        private void OnEnable() => UIToolkitPointerCapture.Register(GetComponent<UIDocument>());

        private void OnDisable() => UIToolkitPointerCapture.Unregister(GetComponent<UIDocument>());

        private void BuildLayout()
        {
            var root = document.rootVisualElement;
            root.Clear();
            root.style.flexGrow = 1;
            root.style.alignItems = Align.Center;
            root.style.justifyContent = Justify.Center;
            root.style.backgroundColor = new Color(0f, 0f, 0f, 0.55f);

            var panel = new VisualElement();
            panel.AddToClassList("be-panel");
            panel.AddToClassList("be-part--panel-wood");
            panel.style.backgroundColor = new Color(0.34f, 0.35f, 0.38f, 1f);
            panel.style.color = new Color(0.231f, 0.161f, 0.098f, 1f);
            if (plainStyle)
            {
                panel.style.backgroundColor = new Color(0.10f, 0.11f, 0.14f, 0.94f);
                panel.style.color = new Color(0.94f, 0.91f, 0.85f, 1f);
            }
            panel.style.paddingLeft = panel.style.paddingRight = panel.style.paddingTop = panel.style.paddingBottom = 24;
            root.Add(panel);
            root = panel;

            var title = new Label("Store");
            title.style.fontSize = 28;
            title.style.marginBottom = 8;
            root.Add(title);

            goldLabel = new Label();
            goldLabel.style.fontSize = 16;
            goldLabel.style.marginBottom = 8;
            root.Add(goldLabel);

            statusLabel = new Label();
            statusLabel.style.fontSize = 13;
            statusLabel.style.color = Color.red;
            statusLabel.style.marginBottom = 8;
            root.Add(statusLabel);

            columns = new VisualElement();
            columns.style.flexDirection = FlexDirection.Row;
            root.Add(columns);

            buyContainer = new VisualElement();
            columns.Add(buyContainer);

            sellContainer = new VisualElement();
            columns.Add(sellContainer);

            var leaveButton = new Button(Leave) { text = "Leave" };
            StyleButton(leaveButton);
            leaveButton.style.marginTop = 24;
            leaveButton.style.width = 160;
            leaveButton.style.height = 32;
            root.Add(leaveButton);
        }

        /// <summary>Leaves the store, firing Closed -- the Leave button's own handler, also callable directly
        /// (e.g. by a conversation controller resuming dialogue, or a future hotkey/interaction-cancel path).</summary>
        public void Leave() => Closed?.Invoke();

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

        // The plain style's buttons: a dark fill with the panel's light text (the default button is light grey, on which inherited light text vanishes).
        private void StyleButton(Button button)
        {
            if (!plainStyle) return;
            button.style.backgroundColor = new Color(0.22f, 0.24f, 0.30f, 1f);
            button.style.color = new Color(0.94f, 0.91f, 0.85f, 1f);
            var edge = new Color(0.62f, 0.55f, 0.40f, 1f);
            button.style.borderTopColor = edge;
            button.style.borderBottomColor = edge;
            button.style.borderLeftColor = edge;
            button.style.borderRightColor = edge;
        }

        private VisualElement BuildBuyRow(StoreListingSummary listing)
        {
            var row = new VisualElement { style = { flexDirection = FlexDirection.Row, marginBottom = 6, alignItems = Align.Center } };

            var stockText = listing.IsUnlimited ? string.Empty : $" ({listing.RemainingQuantity} left)";
            var label = new Label($"{listing.DisplayName} - {listing.Price}g{stockText}");
            label.style.flexGrow = 1;
            row.Add(label);

            var buyButton = new Button(() => OnBuyClicked(listing.ItemId)) { text = "Buy" };
            StyleButton(buyButton);
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
            StyleButton(sellButton);
            row.Add(sellButton);

            return row;
        }

        private void OnBuyClicked(DefinitionId itemId)
        {
            var result = presenter.Buy(itemId);
            statusLabel.text = result.Success ? string.Empty : DescribeFailure(result.Outcome);
            Refresh();
        }

        private void OnSellClicked(RuntimeEntityId instanceId)
        {
            var result = presenter.Sell(instanceId);
            statusLabel.text = result.Success ? string.Empty : DescribeFailure(result.Outcome);
            Refresh();
        }

        private static string DescribeFailure(StoreTransactionOutcome outcome) => outcome switch
        {
            StoreTransactionOutcome.NotListed => "That item is no longer available.",
            StoreTransactionOutcome.OutOfStock => "Out of stock.",
            StoreTransactionOutcome.InsufficientFunds => "Not enough gold.",
            StoreTransactionOutcome.ItemNotOwned => "You don't have that item.",
            StoreTransactionOutcome.ItemEquipped => "Unequip that item before selling it.",
            _ => "That didn't work.",
        };
    }
}
