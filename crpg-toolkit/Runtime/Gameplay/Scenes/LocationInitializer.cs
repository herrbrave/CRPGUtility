using System;
using System.Collections.Generic;
using CRPG.Core;
using CRPG.Inventory;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace CRPG.Gameplay.Scenes
{
    /// <summary>
    /// Task 3.6: the one place that brings a location's authored ILocationContent to life against a live
    /// GameSession/WorldSpawnService -- shared by both in-fiction travel (SceneTravelCoordinator) and
    /// cross-scene save load (SessionLoadCoordinator) so neither reimplements location bring-up ad hoc. Runs
    /// entirely against the newly-loaded scene while it is still inactive/not yet the active scene; every
    /// GameObject it creates is moved into targetScene explicitly (Unity's default "goes into the active scene"
    /// would otherwise put them in whatever scene was active when Prepare ran, i.e. the OLD scene). Only once
    /// Prepare returns without throwing should the caller activate the scene's roots.
    /// </summary>
    public static class LocationInitializer
    {
        /// <exception cref="Exception">Rolls back every spawn point THIS call freshly created (via
        /// WorldSpawnService.Unspawn) before rethrowing -- a location that fails to prepare leaves no partial
        /// trace of itself in the session. Store/loot content points opened for the first time by this same
        /// failed call are NOT rolled back (no undo primitive exists for those yet, matching this project's
        /// established practice of flagging a known, narrow limitation rather than open-endedly engineering a
        /// rare failure path) -- a Store/LootContainer that was newly created just before the failure stays in
        /// GameSession.Stores/LootContainers, inert and unreferenced by any live adapter, which is harmless
        /// (never surfaced to a player) but not byte-for-byte reversed.</exception>
        public static void Prepare(ILocationContent content, GameSession session, WorldSpawnService worldSpawnService, Scene targetScene)
        {
            var freshlyCreatedSpawnIds = new List<string>();
            try
            {
                foreach (var spawnPoint in content.SpawnPoints)
                    PrepareSpawnPoint(spawnPoint, worldSpawnService, targetScene, freshlyCreatedSpawnIds);

                foreach (var storePoint in content.Stores) PrepareStore(storePoint, session);
                foreach (var lootPoint in content.LootContainers) PrepareLootContainer(lootPoint, session);
                foreach (var spawnPoint in content.SpawnPoints) NpcStores.Ensure(spawnPoint, session);
            }
            catch (Exception)
            {
                foreach (var spawnId in freshlyCreatedSpawnIds) worldSpawnService.Unspawn(spawnId);
                throw;
            }
        }

        private static void PrepareSpawnPoint(NpcSpawnPoint spawnPoint, WorldSpawnService worldSpawnService, Scene targetScene, List<string> freshlyCreatedSpawnIds)
        {
            if (worldSpawnService.HasRecord(spawnPoint.SpawnId))
            {
                if (!worldSpawnService.TryGetCharacterId(spawnPoint.SpawnId, out var characterId, out var npcDefinitionId, out var defeated) || defeated)
                    return; // defeated (or somehow missing) -- nothing to bind a view onto

                var position = spawnPoint.Anchor.position;
                var facingYaw = spawnPoint.Anchor.eulerAngles.y;
                if (worldSpawnService.TryGetLastKnownTransform(spawnPoint.SpawnId, out var lastPosition, out var lastFacingYaw))
                {
                    position = lastPosition;
                    facingYaw = lastFacingYaw;
                }

                var instance = worldSpawnService.BindRestoredView(
                    spawnPoint.SpawnId, characterId, npcDefinitionId, spawnPoint.Definition.VisualPrefab,
                    position, Quaternion.Euler(0f, facingYaw, 0f), spawnPoint.Definition.Faction, startInactive: true);
                SceneManager.MoveGameObjectToScene(instance, targetScene);
            }
            else
            {
                var agent = worldSpawnService.SpawnNpc(spawnPoint.SpawnId, spawnPoint.Definition, spawnPoint.Anchor.position, spawnPoint.Anchor.rotation);
                freshlyCreatedSpawnIds.Add(spawnPoint.SpawnId);
                // SpawnNpc has no startInactive option (its only other caller, ordinary single-scene spawning,
                // wants it live immediately) -- deactivate right after, so this location's NPCs stay inert in
                // the not-yet-active target scene exactly like a restored view does, until the caller commits.
                agent.gameObject.SetActive(false);
                SceneManager.MoveGameObjectToScene(agent.gameObject, targetScene);
            }
        }

        /// <summary>Task 6.0: the Store/LootContainer half of <see cref="Prepare"/>, on its own -- for a
        /// same-scene session swap (a save load) where SpawnPoints are already fully handled by
        /// <c>SessionLoadCoordinator</c>'s own restoration. Re-running the SpawnPoints loop there would
        /// duplicate every already-loaded NPC: <c>WorldSpawnService.BindRestoredView</c> unconditionally
        /// instantiates a fresh view regardless of whether a spawn record already exists (confirmed by reading
        /// it directly, not assumed) -- it only skips re-inserting the RECORD, never the GameObject. Safe to call
        /// against an already-prepared scene: <c>GetOrCreateStore</c>/<c>GetOrCreateLootContainer</c> are
        /// idempotent by design (return the existing one if the id is already known).</summary>
        public static void RebindStoresAndLoot(ILocationContent content, GameSession session)
        {
            foreach (var storePoint in content.Stores) PrepareStore(storePoint, session);
            foreach (var lootPoint in content.LootContainers) PrepareLootContainer(lootPoint, session);
            foreach (var spawnPoint in content.SpawnPoints) NpcStores.Ensure(spawnPoint, session);   // an NPC's shop must exist BEFORE SaveService.RestoreStores, or depleted stock reseeds
        }

        private static void PrepareStore(StoreContentPoint storePoint, GameSession session)
        {
            var store = session.GetOrCreateStore(storePoint.StoreId, seedIfNew: s =>
            {
                foreach (var seed in storePoint.InitialListings)
                {
                    if (seed.Item != null) s.AddListing(seed.Item.Id, seed.Quantity);
                }
            });

            if (storePoint.Adapter != null) storePoint.Adapter.Store = store;
        }

        private static void PrepareLootContainer(LootContentPoint lootPoint, GameSession session)
        {
            var container = session.GetOrCreateLootContainer(lootPoint.ContainerId, seedIfNew: c =>
            {
                foreach (var seed in lootPoint.InitialItems)
                {
                    if (seed.Item != null) c.AddItem(new ItemInstance(RuntimeEntityId.NewId(), seed.Item.Id, seed.Quantity));
                }
            });

            if (lootPoint.Adapter != null)
            {
                lootPoint.Adapter.Container = container;
                lootPoint.Adapter.Inventory = session.Inventory;
            }
        }
    }
}
