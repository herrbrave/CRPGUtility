using CRPG.Core;

namespace CRPG.Inventory
{
    /// <summary>One item a StoreInventoryState offers. Quantity &lt; 0 means unlimited stock -- the KISS
    /// default, since it needs no restocking-timer mechanic at all.</summary>
    public sealed class StoreListing
    {
        public StoreListing(DefinitionId itemId, int quantity = -1)
        {
            ItemId = itemId;
            Quantity = quantity;
        }

        public DefinitionId ItemId { get; }
        public int Quantity { get; set; }
        public bool IsUnlimited => Quantity < 0;
    }
}
