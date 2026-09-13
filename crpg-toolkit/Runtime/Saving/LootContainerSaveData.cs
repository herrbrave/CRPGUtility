using System;
using System.Collections.Generic;

namespace CRPG.Saving
{
    /// <summary>LootContainerState carries no id of its own -- ContainerId is the caller-supplied stable key,
    /// same convention as StoreSaveData.StoreId. Reuses the existing ItemSaveData shape rather than a second
    /// item DTO (EquippedCharacterId/EquippedSlotId are simply left unset -- loot items are never equipped).</summary>
    [Serializable]
    public sealed class LootContainerSaveData
    {
        public string ContainerId;
        public List<ItemSaveData> Items = new();
    }
}
