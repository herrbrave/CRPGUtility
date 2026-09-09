using CRPG.Core;

namespace CRPG.UI
{
    /// <summary>Presentation-friendly snapshot of one item in the party's shared inventory the player can sell.</summary>
    public sealed class OwnedItemSummary
    {
        public OwnedItemSummary(RuntimeEntityId instanceId, string displayName, int quantity, int sellPrice)
        {
            InstanceId = instanceId;
            DisplayName = displayName;
            Quantity = quantity;
            SellPrice = sellPrice;
        }

        public RuntimeEntityId InstanceId { get; }
        public string DisplayName { get; }
        public int Quantity { get; }

        /// <summary>Total for the whole stack (SellPrice-per-unit * Quantity), matching what TrySell actually pays.</summary>
        public int SellPrice { get; }
    }
}
