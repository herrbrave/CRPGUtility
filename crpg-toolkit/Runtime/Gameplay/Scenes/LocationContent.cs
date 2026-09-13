using System.Collections.Generic;
using UnityEngine;

namespace CRPG.Gameplay.Scenes
{
    /// <summary>Generic, directly-authorable ILocationContent -- place one per location scene and fill in its
    /// spawn points/stores/loot containers/camera bounds in the Inspector. Any consuming game can use this
    /// as-is; a project with richer authoring needs can still implement ILocationContent itself instead.</summary>
    public sealed class LocationContent : MonoBehaviour, ILocationContent
    {
        [SerializeField] private List<NpcSpawnPoint> spawnPoints = new();
        [SerializeField] private List<StoreContentPoint> stores = new();
        [SerializeField] private List<LootContentPoint> lootContainers = new();
        [SerializeField] private bool hasCameraBounds;
        [SerializeField] private Rect cameraBoundsXZ;

        public IReadOnlyList<NpcSpawnPoint> SpawnPoints => spawnPoints;
        public IReadOnlyList<StoreContentPoint> Stores => stores;
        public IReadOnlyList<LootContentPoint> LootContainers => lootContainers;
        public Rect? CameraBoundsXZ => hasCameraBounds ? cameraBoundsXZ : null;
    }
}
