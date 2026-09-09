using CRPG.Core;

namespace CRPG.UI
{
    /// <summary>Presentation-friendly snapshot of one item the store has for sale.</summary>
    public sealed class StoreListingSummary
    {
        public StoreListingSummary(DefinitionId itemId, string displayName, int price, int remainingQuantity)
        {
            ItemId = itemId;
            DisplayName = displayName;
            Price = price;
            RemainingQuantity = remainingQuantity;
        }

        public DefinitionId ItemId { get; }
        public string DisplayName { get; }
        public int Price { get; }

        /// <summary>Negative means unlimited stock.</summary>
        public int RemainingQuantity { get; }
        public bool IsUnlimited => RemainingQuantity < 0;
    }
}
