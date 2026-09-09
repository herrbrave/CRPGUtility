using System;
using System.Collections.Generic;

namespace CRPG.Saving
{
    [Serializable]
    public sealed class ItemCustomStateSaveEntry
    {
        public string Key;
        public string Value;
    }

    [Serializable]
    public sealed class ItemSaveData
    {
        public string InstanceId;
        public string DefinitionId;
        public int Quantity;
        public bool HasCharges;
        public int Charges;
        public List<ItemCustomStateSaveEntry> CustomState = new();

        /// <summary>Empty CharacterId means the shared inventory (mirrors ItemLocation.SharedInventory being
        /// the default/RuntimeEntityId.None case).</summary>
        public string EquippedCharacterId;
        public string EquippedSlotId;
    }
}
