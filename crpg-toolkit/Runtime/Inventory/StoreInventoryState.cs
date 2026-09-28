using System.Collections.Generic;
using CRPG.Core;

namespace CRPG.Inventory
{
    /// <summary>A merchant's stock -- a plain runtime object like LootContainerState, not an authored asset.
    /// Whoever represents a merchant (a scene NPC, a dialogue bootstrap) constructs and owns one directly;
    /// there's no StoreDefinition ScriptableObject, since a shop's stock is world/save state, not reusable
    /// authored data.</summary>
    public sealed class StoreInventoryState
    {
        private readonly List<StoreListing> listings = new();

        public StoreInventoryState(float buyPriceMultiplier = 1f, float sellPriceMultiplier = 0.5f)
        {
            BuyPriceMultiplier = buyPriceMultiplier;
            SellPriceMultiplier = sellPriceMultiplier;
        }

        /// <summary>Multiplies an ItemDefinition's BaseValue for what the party pays to buy it. Settable
        /// (not just constructor-assigned) so a merchant's prices can react to world/story state after the
        /// store already exists -- e.g. a price hike once a Timeline flag fires -- without recreating it.</summary>
        public float BuyPriceMultiplier { get; set; }

        /// <summary>Multiplies an ItemDefinition's BaseValue for what the party receives selling it here.</summary>
        public float SellPriceMultiplier { get; set; }

        public IReadOnlyList<StoreListing> Listings => listings;

        public void AddListing(DefinitionId itemId, int quantity = -1) => listings.Add(new StoreListing(itemId, quantity));

        /// <summary>Task 6.0: removes every listing -- for SaveService.RestoreStores, which must REPLACE
        /// whatever a fresh GetOrCreateStore(seedIfNew:...) just populated (a same-id store is always "new" to
        /// a just-loaded session, so it always re-seeds from InitialListings first), not layer the saved
        /// listings on top of them. AddListing has no per-item identity check, so without this, TryGetListing
        /// would keep returning the FIRST (stale, reseeded) listing for an item that also has a restored one.</summary>
        public void ClearListings() => listings.Clear();

        public bool TryGetListing(DefinitionId itemId, out StoreListing listing)
        {
            foreach (var candidate in listings)
            {
                if (candidate.ItemId != itemId) continue;
                listing = candidate;
                return true;
            }

            listing = null;
            return false;
        }
    }
}
