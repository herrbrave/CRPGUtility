using CRPG.Core;
using UnityEngine;

namespace CRPG.Inventory
{
    /// <summary>
    /// Immutable authored item data. Whether an item is equippable is data (an <see cref="EquipmentSlotId"/>),
    /// not a type hierarchy -- a potion, a scroll and a bomb are all just items whose <see cref="GrantedAbilityId"/>
    /// points at whatever ability using them performs (Drink Potion, Cast Stored Spell, Throw Bomb), so the
    /// engine never needs a PotionSystem/ScrollSystem/BombSystem. Referencing the ability by DefinitionId
    /// means this assembly has no compile-time dependency on Abilities at all.
    /// </summary>
    [CreateAssetMenu(menuName = "CRPG Toolkit/Inventory/Item Definition")]
    public sealed class ItemDefinition : DefinitionAsset
    {
        [SerializeField] private string displayName;
        [SerializeField, TextArea] private string description;
        [SerializeField] private int maxStackSize = 1;
        [SerializeField] private float weight;
        [SerializeField] private float baseValue;
        [SerializeField] private DefinitionId equipmentSlotId;
        [SerializeField] private DefinitionId grantedAbilityId;
        [SerializeField] private DefinitionId equippedFeatureId;
        [SerializeField] private DefinitionId equipmentVisualProfileId;

        public string DisplayName => displayName;
        public string Description => description;
        public int MaxStackSize => maxStackSize;
        public float Weight => weight;

        /// <summary>What one unit of this item is worth in whatever <see cref="ItemDefinition"/> a StoreService
        /// is configured to treat as currency (e.g. "Gold Piece") -- not itself a currency amount.</summary>
        public float BaseValue => baseValue;

        public DefinitionId EquipmentSlotId => equipmentSlotId;
        public DefinitionId GrantedAbilityId => grantedAbilityId;

        /// <summary>A FeatureDefinition granted to whoever has this item equipped, removed the moment it's
        /// unequipped or displaced (e.g. armor's AC bonus, a Fighting Style tag). None (the common case) means
        /// this item has no passive effect while worn.</summary>
        public DefinitionId EquippedFeatureId => equippedFeatureId;

        /// <summary>Task 2.4: a bare <see cref="DefinitionId"/> tag for an
        /// <c>CRPG.Presentation.Equipment.EquipmentVisualProfile</c> -- same "reference by id, not by direct
        /// type" pattern as <see cref="GrantedAbilityId"/>/<see cref="EquippedFeatureId"/>, since Inventory
        /// must never reference Presentation directly (Gameplay -&gt; Presentation -&gt; {Actors, Inventory,
        /// Core}, never the reverse). None (the common case, e.g. most consumables) means this item has no
        /// visual representation. Resolved through the same IRulesDatabase everything else uses, so this
        /// assembly has no compile-time dependency on Presentation at all.</summary>
        public DefinitionId EquipmentVisualProfileId => equipmentVisualProfileId;

        public bool IsEquippable => equipmentSlotId.IsValid;
        public bool IsStackable => maxStackSize > 1;
    }
}
