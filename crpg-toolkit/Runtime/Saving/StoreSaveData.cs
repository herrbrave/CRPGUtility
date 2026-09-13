using System;
using System.Collections.Generic;

namespace CRPG.Saving
{
    [Serializable]
    public sealed class StoreListingSaveEntry
    {
        public string ItemId;
        public int Quantity;
    }

    /// <summary>StoreInventoryState carries no id of its own -- StoreId is the caller-supplied stable key
    /// SaveService.Capture/RestoreStores were given, the same "caller supplies identity" convention spawn ids
    /// use (Task 3.5).</summary>
    [Serializable]
    public sealed class StoreSaveData
    {
        public string StoreId;
        public float BuyPriceMultiplier;
        public float SellPriceMultiplier;
        public List<StoreListingSaveEntry> Listings = new();
    }
}
