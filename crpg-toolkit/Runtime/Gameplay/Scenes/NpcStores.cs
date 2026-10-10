using CRPG.Core;
using CRPG.Inventory;
using UnityEngine;

namespace CRPG.Gameplay.Scenes
{
    /// <summary>
    /// Creates the shop an <see cref="NpcSpawnPoint"/> authors (<see cref="NpcStoreSpec"/>). Idempotent by store id: the first call seeds the stock and the multipliers, every later call returns the same store with
    /// whatever stock is left, which is what lets a shop survive its location being unloaded and, with <c>SaveService.RestoreStores</c> run AFTER this, a save/load. <c>LocationInitializer</c> calls it for every spawn
    /// point (so the store exists before a load restores into it) and <c>NpcDialogues.AttachAll</c> calls it again to bind the store to the NPC's talk adapter.
    /// </summary>
    public static class NpcStores
    {
        /// <returns>The session's store for this spawn point, or null when the spawn point authors no shop.</returns>
        public static StoreInventoryState Ensure(NpcSpawnPoint point, GameSession session)
        {
            if (point == null || point.Store == null || !point.Store.HasStore || session == null) return null;

            var spec = point.Store;
            var id = spec.ResolveId(point.SpawnId);
            if (string.IsNullOrEmpty(id)) return null;

            return session.GetOrCreateStore(id, store =>
            {
                // GetOrCreateStore builds the store with the default multipliers: apply the authored ones here, inside the seed.
                store.BuyPriceMultiplier = Mathf.Max(0f, spec.BuyMultiplier);
                store.SellPriceMultiplier = Mathf.Max(0f, spec.SellMultiplier);

                foreach (var seed in spec.Listings)
                {
                    if (seed == null || seed.Item == null) continue;

                    // The shop screen resolves every listing through the rules database and throws on an id it does not know: leave such an item out and say so.
                    if (!session.RulesDatabase.TryResolve<ItemDefinition>(seed.Item.Id, out _))
                    {
                        Debug.LogWarning($"[NpcStores] '{id}': the item '{seed.Item.name}' is not in this session's ruleset, so it is not stocked. Register it in the ruleset.");
                        continue;
                    }

                    store.AddListing(seed.Item.Id, seed.Quantity);
                }
            });
        }
    }
}
