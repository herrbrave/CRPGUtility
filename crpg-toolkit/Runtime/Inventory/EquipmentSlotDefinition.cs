using CRPG.Core;
using UnityEngine;

namespace CRPG.Inventory
{
    /// <summary>A slot a character can equip an item into (MainHand, OffHand, Armor, a homebrew ruleset's
    /// "Cloak", ...). Authored data rather than a fixed enum, so a ruleset can add its own slots without
    /// touching engine code.</summary>
    [CreateAssetMenu(menuName = "CRPG Toolkit/Inventory/Equipment Slot Definition")]
    public sealed class EquipmentSlotDefinition : DefinitionAsset
    {
        [SerializeField] private string displayName;

        public string DisplayName => displayName;
    }
}
