using System;
using System.Collections.Generic;
using CRPG.Core;
using UnityEngine;

namespace CRPG.Presentation.Sprites
{
    /// <summary>One choice a catalog offers for its layer slot.</summary>
    [Serializable]
    public sealed class SpriteOptionEntry
    {
        [Tooltip("Stable id stored in saves. Never rename an id that has shipped: a stored id that stops resolving degrades to an empty layer.")]
        public DefinitionId optionId;

        [Tooltip("What a creator UI shows. Free to change.")]
        public string displayName;

        public Sprite sprite;

        [Tooltip("Multiplied with the sprite. Keep alpha above 0 or the layer is invisible.")]
        public Color tint = Color.white;

        public SpriteOptionEntry() { }

        public SpriteOptionEntry(DefinitionId optionId, string displayName, Sprite sprite, Color tint)
        {
            this.optionId = optionId;
            this.displayName = displayName;
            this.sprite = sprite;
            this.tint = tint;
        }
    }

    /// <summary>
    /// The options available for ONE layer slot (a hair catalog, a helmet catalog...): each option id maps to a sprite and
    /// tint. Character appearance and NPC definitions store only (slot, option id); this asset is what turns an id into pixels.
    /// Option ids are unique within a catalog and scoped by the slot, so hair-01 and helmet-01 may coexist. Register the catalog
    /// in the game's rules database like any other definition.
    /// </summary>
    [CreateAssetMenu(menuName = "CRPG Toolkit/Presentation/Sprite Layer Catalog")]
    public sealed class SpriteLayerCatalog : DefinitionAsset
    {
        [SerializeField] private DefinitionId layerSlot;
        [Tooltip("The category name a creator UI shows, e.g. Hair.")]
        [SerializeField] private string displayName;
        [SerializeField] private List<SpriteOptionEntry> options = new();

        public DefinitionId LayerSlot => layerSlot;
        public string DisplayName => displayName;
        public IReadOnlyList<SpriteOptionEntry> Options => options;

        /// <summary>The first option with this id (the validator flags duplicates).</summary>
        public bool TryGetOption(DefinitionId optionId, out SpriteOptionEntry entry)
        {
            foreach (var candidate in options)
            {
                if (candidate != null && candidate.optionId == optionId)
                {
                    entry = candidate;
                    return true;
                }
            }

            entry = null;
            return false;
        }
    }
}
