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

        [Tooltip("What this NPC does when nothing else is asking: stand, wander an area, or walk a loop of work stops.")]
        public CRPG.Gameplay.Ambient.NpcRoutineSpec Routine = new();

        [Tooltip("What this NPC says when the party talks to it. Empty = it does not talk (unless its NpcDefinition has a DialogueId the ruleset registers).")]
        public CRPG.Dialogue.DialogueDefinition Dialogue;

        [Tooltip("What this NPC sells. No listings = no shop. The dialogue opens the shop with an Open Store action.")]
        public NpcStoreSpec Store = new();
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

    /// <summary>The shop of one NPC, authored on its <see cref="NpcSpawnPoint"/>: the stock it starts with and its price multipliers. The runtime store lives in the session under <see cref="ResolveId"/> (the
    /// spawn id unless <see cref="StoreId"/> is set) and keeps its depleted stock across location unloads and saves; see <see cref="NpcStores"/>.</summary>
    [System.Serializable]
    public sealed class NpcStoreSpec
    {
        [Tooltip("Empty = the spawn id. Ids are unique across the whole save (they share the session's stores with the store talk points).")]
        public string StoreId;
        public List<StoreListingSeed> Listings = new();
        [Tooltip("What the party pays: the item's base value times this.")]
        public float BuyMultiplier = 1f;
        [Tooltip("What the shop pays for the party's items: the item's base value times this.")]
        public float SellMultiplier = 0.5f;

        public bool HasStore => Listings != null && Listings.Count > 0;

        public string ResolveId(string spawnId) => string.IsNullOrEmpty(StoreId) ? spawnId : StoreId;

        /// <summary>Back to the default (no shop). Used when a new spawn point is added next to an existing one.</summary>
        public void Reset()
        {
            StoreId = null;
            Listings = new List<StoreListingSeed>();
            BuyMultiplier = 1f;
            SellMultiplier = 0.5f;
        }
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
