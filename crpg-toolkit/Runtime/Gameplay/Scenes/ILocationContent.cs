using System.Collections.Generic;
using CRPG.Actors;
using CRPG.Gameplay.Interactions.Adapters;
using CRPG.Inventory;
using UnityEngine;

namespace CRPG.Gameplay.Scenes
{
    /// <summary>One NPC a location authors. SpawnId must be stable and unique within a save's whole world (not
    /// just this location) -- WorldSpawnService.HasRecord(SpawnId) is what LocationInitializer uses to decide
    /// SpawnNpc (fresh) vs BindRestoredView (restore) for it.</summary>
    [System.Serializable]
    public sealed class NpcSpawnPoint
    {
        public string SpawnId;
        public NpcDefinition Definition;
        public Transform Anchor;
    }

    /// <summary>A merchant reachable through this location's own TalkInteractionAdapter. StoreId must be
    /// stable/unique within a save's whole world -- LocationInitializer resolves the actual StoreInventoryState
    /// via GameSession.GetOrCreateStore(StoreId, ...) and assigns it to Adapter.Store, seeding InitialListings
    /// only the first time this id is ever requested.</summary>
    [System.Serializable]
    public sealed class StoreContentPoint
    {
        public string StoreId;
        public TalkInteractionAdapter Adapter;
        public List<StoreListingSeed> InitialListings = new();
    }

    [System.Serializable]
    public sealed class StoreListingSeed
    {
        public ItemDefinition Item;

        /// <summary>-1 (StoreInventoryState.AddListing's own default) means unlimited stock.</summary>
        public int Quantity = -1;
    }

    /// <summary>A lootable container reachable through this location's own LootInteractionAdapter. ContainerId
    /// must be stable/unique within a save's whole world -- resolved via
    /// GameSession.GetOrCreateLootContainer(ContainerId, ...), seeded only on first-ever request.</summary>
    [System.Serializable]
    public sealed class LootContentPoint
    {
        public string ContainerId;
        public LootInteractionAdapter Adapter;
        public List<LootItemSeed> InitialItems = new();
    }

    [System.Serializable]
    public sealed class LootItemSeed
    {
        public ItemDefinition Item;
        public int Quantity = 1;
    }

    /// <summary>Task 3.6: authored on one root GameObject per location scene (found via
    /// FindObjectsByType&lt;MonoBehaviour&gt;().OfType&lt;ILocationContent&gt;() in the freshly-loaded, still-
    /// inactive scene -- the same discovery idiom Task 2.5's ICharacterPreviewAnchor already established).
    /// LocationInitializer is the ONLY thing that reads this and acts on it; the scene's own authored
    /// MonoBehaviours should avoid meaningful Awake-time side effects so "prepare, then activate" (see
    /// LocationInitializer.Prepare) stays meaningful even though Unity itself has already run the newly-loaded
    /// scene's Awake/OnEnable by the time this is queried.</summary>
    public interface ILocationContent
    {
        IReadOnlyList<NpcSpawnPoint> SpawnPoints { get; }
        IReadOnlyList<StoreContentPoint> Stores { get; }
        IReadOnlyList<LootContentPoint> LootContainers { get; }

        /// <summary>Null means "no explicit bounds for this location" -- CameraRigState.SetBounds(null).</summary>
        Rect? CameraBoundsXZ { get; }
    }
}
