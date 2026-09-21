using System.Collections.Generic;
using CRPG.Core;
using CRPG.Inventory;
using CRPG.Presentation.Sprites;

namespace CRPG.Presentation.Equipment
{
    /// <summary>
    /// The one place that turns "an item worn on a sprite character" into "the layers it paints": item definition ->
    /// <see cref="EquipmentVisualProfile"/> -> <see cref="LayerPaint"/> list. Shared by <see cref="SpriteEquipmentBinder"/> (the world)
    /// and the character creator's preview so the preview cannot drift from what the world draws. Pure: no logging, no state; a caller
    /// that wants warn-once behaviour inspects <c>profile</c> itself.
    /// </summary>
    public static class SpriteEquipmentPaints
    {
        /// <summary>Resolves <paramref name="itemId"/>'s sprite profile. Returns true only for a Sprite-kind profile, with
        /// <paramref name="paints"/> (cleared first) holding its layers; the caller then applies
        /// <c>SetEquipment(slot, profile.SpritePriority, paints, profile.HiddenLayerSlots)</c>. Returns false when the slot should be
        /// CLEARED: no item, no profile, a <see cref="EquipmentVisualKind.None"/> profile, or a profile of another kind (which draws
        /// nothing on a sprite character). <paramref name="profile"/> is the resolved profile in every case (null when there is none), so
        /// a caller can tell "no visual" (null or None) from "a 3D profile on a sprite character" (worth one warning).</summary>
        public static bool TryResolveSpritePaints(IRulesDatabase rules, DefinitionId itemId, List<LayerPaint> paints, out EquipmentVisualProfile profile)
        {
            paints.Clear();
            profile = null;

            if (rules.TryResolve<ItemDefinition>(itemId, out var itemDef) && itemDef.EquipmentVisualProfileId.IsValid)
                rules.TryResolve(itemDef.EquipmentVisualProfileId, out profile);

            if (profile == null || profile.Kind != EquipmentVisualKind.Sprite) return false;

            foreach (var entry in profile.SpriteLayers)
                if (entry != null) paints.Add(new LayerPaint(entry.layerSlot, new SpriteOption(entry.sprite, entry.tint)));

            return true;
        }
    }
}
