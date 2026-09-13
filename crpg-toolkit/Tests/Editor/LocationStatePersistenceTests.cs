using System.Collections.Generic;
using System.Linq;
using CRPG.Core;
using CRPG.Gameplay;
using CRPG.Gameplay.Interactions.Adapters;
using CRPG.Gameplay.Scenes;
using CRPG.Inventory;
using CRPG.Rules;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace CRPG.Tests.EditMode
{
    /// <summary>
    /// Task 3.6's acceptance test for the location-state-loss fix: loot a container and buy from a store in
    /// location A, "travel" to B (WorldSpawnService.ReleaseSpawnsInScene on A's content) and back (LocationInitializer.Prepare
    /// re-run against A), and confirm the chest is still empty and the store's stock still reflects the
    /// purchase -- not reset. Runs entirely in EditMode against the real GameSession/WorldSpawnService/
    /// LocationInitializer machinery (no real additive scene load involved -- that integration is
    /// SceneTravelTests' job) since none of the claims here depend on Play mode.
    /// </summary>
    public class LocationStatePersistenceTests
    {
        private sealed class FakeLocationContent : ILocationContent
        {
            public List<NpcSpawnPoint> SpawnPointsList { get; } = new();
            public List<StoreContentPoint> StoresList { get; } = new();
            public List<LootContentPoint> LootContainersList { get; } = new();

            public IReadOnlyList<NpcSpawnPoint> SpawnPoints => SpawnPointsList;
            public IReadOnlyList<StoreContentPoint> Stores => StoresList;
            public IReadOnlyList<LootContentPoint> LootContainers => LootContainersList;
            public Rect? CameraBoundsXZ => null;
        }

        private readonly List<GameObject> spawned = new();

        [TearDown]
        public void TearDown()
        {
            foreach (var go in spawned) if (go != null) UnityEngine.Object.DestroyImmediate(go);
            spawned.Clear();
        }

        private GameObject Track(GameObject go)
        {
            spawned.Add(go);
            return go;
        }

        [Test]
        public void StoreStockAndLootContainerPersistThroughLeavingAndReturningToALocation()
        {
            var rulesDatabase = new RulesDatabase();
            var potion = TestDefinitionFactory.CreateItemDefinition("item-potion", "Potion", baseValue: 10);
            var gold = TestDefinitionFactory.CreateItemDefinition("item-gold", "Gold", baseValue: 1);
            rulesDatabase.Register(potion);
            rulesDatabase.Register(gold);

            var session = new GameSession(rulesDatabase, new DiceService(new SeededRandomSource(1)));
            var worldSpawnService = new WorldSpawnService(session);
            var currentScene = SceneManager.GetActiveScene();

            var lootAdapter = Track(new GameObject("Chest")).AddComponent<LootInteractionAdapter>();
            // LocationInitializer must wire the destination too, not just the container.
            var storeAdapter = Track(new GameObject("Merchant")).AddComponent<TalkInteractionAdapter>();

            var locationA = new FakeLocationContent();
            locationA.StoresList.Add(new StoreContentPoint
            {
                StoreId = "store-a",
                Adapter = storeAdapter,
                InitialListings = { new StoreListingSeed { Item = potion, Quantity = 5 } },
            });
            locationA.LootContainersList.Add(new LootContentPoint
            {
                ContainerId = "chest-a",
                Adapter = lootAdapter,
                InitialItems = { new LootItemSeed { Item = gold, Quantity = 3 } },
            });

            // First visit: content is freshly seeded.
            LocationInitializer.Prepare(locationA, session, worldSpawnService, currentScene);
            Assert.IsFalse(lootAdapter.Container.IsEmpty);
            Assert.AreEqual(5, GetListingQuantity(storeAdapter.Store, potion.Id));

            // Loot the chest and buy a potion.
            Assert.AreSame(session.Inventory, lootAdapter.Inventory);
            lootAdapter.BeginExecute(RuntimeEntityId.NewId(), _ => { });
            session.Inventory.AddItem(new ItemInstance(RuntimeEntityId.NewId(), gold.Id, 100));
            var storeService = new StoreService(rulesDatabase, session.Inventory, gold.Id);
            Assert.IsTrue(storeService.TryBuy(storeAdapter.Store, potion.Id, 2));
            Assert.AreEqual(3, GetListingQuantity(storeAdapter.Store, potion.Id));
            Assert.IsTrue(lootAdapter.Container.IsEmpty);

            // "Travel" to another location: release A's views (records/session state stay).
            worldSpawnService.ReleaseSpawnsInScene(currentScene);

            var locationB = new FakeLocationContent();
            LocationInitializer.Prepare(locationB, session, worldSpawnService, currentScene);

            // Travel back to A: re-Prepare must NOT re-seed -- GetOrCreateStore/LootContainer return the SAME,
            // already-modified instances.
            LocationInitializer.Prepare(locationA, session, worldSpawnService, currentScene);

            Assert.IsTrue(lootAdapter.Container.IsEmpty); // still empty, not reseeded
            Assert.AreEqual(3, GetListingQuantity(storeAdapter.Store, potion.Id)); // still depleted, not reset to 5
            Assert.AreSame(session.GetOrCreateStore("store-a"), storeAdapter.Store);
            Assert.AreSame(session.GetOrCreateLootContainer("chest-a"), lootAdapter.Container);
        }

        [Test]
        public void AnNpcsRecordSurvivesItsLocationBeingReleasedAndItsViewRebindsOnReturnWithoutRegrantingItems()
        {
            var rulesDatabase = new RulesDatabase();
            var startingItem = TestDefinitionFactory.CreateItemDefinition("item-dagger", "Dagger");
            rulesDatabase.Register(startingItem);
            var npcDefinition = TestDefinitionFactory.CreateNpcDefinition("npc-guard", "Guard");
            var visualPrefab = Track(new GameObject("Guard-VisualPrefab"));
            TestDefinitionFactory.SetPrivateField(npcDefinition, "visualPrefab", visualPrefab);
            TestDefinitionFactory.SetPrivateField(npcDefinition, "startingItemIds", new List<DefinitionId> { startingItem.Id });

            var session = new GameSession(rulesDatabase, new DiceService(new SeededRandomSource(1)));
            var worldSpawnService = new WorldSpawnService(session);
            var currentScene = SceneManager.GetActiveScene();

            var anchorGo = Track(new GameObject("SpawnAnchor"));
            anchorGo.transform.position = new Vector3(5, 0, 5);

            var location = new FakeLocationContent();
            location.SpawnPointsList.Add(new NpcSpawnPoint { SpawnId = "guard-1", Definition = npcDefinition, Anchor = anchorGo.transform });

            LocationInitializer.Prepare(location, session, worldSpawnService, currentScene);
            Assert.AreEqual(1, worldSpawnService.OwnedAgents.Count);
            worldSpawnService.TryGetCharacterId("guard-1", out var characterId, out _, out _);
            var itemCountAfterFirstPrepare = session.Inventory.AllItems.Count();

            worldSpawnService.ReleaseSpawnsInScene(currentScene);
            Assert.AreEqual(0, worldSpawnService.OwnedAgents.Count); // view gone
            Assert.IsTrue(worldSpawnService.HasRecord("guard-1")); // record survives

            LocationInitializer.Prepare(location, session, worldSpawnService, currentScene); // "return"

            Assert.AreEqual(1, worldSpawnService.OwnedAgents.Count);
            worldSpawnService.TryGetCharacterId("guard-1", out var characterIdAfterReturn, out _, out _);
            Assert.AreEqual(characterId, characterIdAfterReturn); // same character, not a duplicate
            Assert.AreEqual(itemCountAfterFirstPrepare, session.Inventory.AllItems.Count()); // starting items not re-granted
        }

        private static int GetListingQuantity(StoreInventoryState store, DefinitionId itemId)
        {
            store.TryGetListing(itemId, out var listing);
            return listing.Quantity;
        }
    }
}
