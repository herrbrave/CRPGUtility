using System.Collections.Generic;
using CRPG.Core;

namespace CRPG.Inventory
{
    /// <summary>
    /// A specific copy of an <see cref="ItemDefinition"/> -- what makes two Longswords different items even
    /// though they share a definition. Reuses <see cref="RuntimeEntityId"/> for its identity rather than a
    /// third id type: an item instance is just another thing with runtime identity, like a character or a
    /// loot container.
    /// </summary>
    public sealed class ItemInstance
    {
        private readonly Dictionary<string, string> customState = new();

        public ItemInstance(RuntimeEntityId instanceId, DefinitionId definitionId, int quantity = 1, int? charges = null)
        {
            InstanceId = instanceId;
            DefinitionId = definitionId;
            Quantity = quantity;
            Charges = charges;
        }

        public RuntimeEntityId InstanceId { get; }
        public DefinitionId DefinitionId { get; }
        public int Quantity { get; set; }
        public int? Charges { get; set; }

        /// <summary>Arbitrary per-instance data (identified, enchantment level, ...) that doesn't warrant its
        /// own field. Kept as strings so it stays trivially save-friendly.</summary>
        public IReadOnlyDictionary<string, string> CustomState => customState;

        public void SetCustomState(string key, string value) => customState[key] = value;
        public bool TryGetCustomState(string key, out string value) => customState.TryGetValue(key, out value);
    }
}
