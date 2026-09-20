using System.Collections.Generic;
using CRPG.Core;
using CRPG.Presentation.Equipment;
using CRPG.Presentation.Sprites;

namespace CRPG.Editor.Validation
{
    /// <summary>
    /// 2D campaign Task 2.3: validates the sprite side of an <see cref="EquipmentVisualProfile"/> -- pure logic, no scene.
    /// At runtime a typo'd layer slot is only reported and ignored by <see cref="SpriteLayerState"/>, and a profile with the
    /// wrong kind silently draws nothing, so these are worth catching at authoring time. Never mutates the asset.
    /// </summary>
    public static class SpriteEquipmentProfileValidator
    {
        /// <param name="knownLayerSlots">The layer slots the character's compositor has; defaults to the compositor's default set.</param>
        public static List<ValidationResult> Validate(EquipmentVisualProfile profile, string assetPath, IReadOnlyCollection<DefinitionId> knownLayerSlots = null)
        {
            var results = new List<ValidationResult>();
            if (profile == null) return results;

            var known = new HashSet<DefinitionId>(knownLayerSlots ?? SpriteLayerCompositor.DefaultSlots());

            if (profile.Kind != EquipmentVisualKind.Sprite)
            {
                if (profile.SpriteLayers.Count > 0 || profile.HiddenLayerSlots.Count > 0)
                    results.Add(new ValidationResult(ValidationSeverity.Warning, assetPath, "kind",
                        $"Sprite layers or hidden layer slots are authored, but the kind is {profile.Kind}, so they will never be used.",
                        "Set Kind to Sprite (or clear the sprite fields)."));
                return results;
            }

            var painted = new HashSet<DefinitionId>();
            for (var i = 0; i < profile.SpriteLayers.Count; i++)
            {
                var entry = profile.SpriteLayers[i];
                var field = $"spriteLayers[{i}]";
                if (entry == null)
                {
                    results.Add(new ValidationResult(ValidationSeverity.Error, assetPath, field, "The layer entry is empty.", "Remove it or fill it in."));
                    continue;
                }

                if (!entry.layerSlot.IsValid)
                    results.Add(new ValidationResult(ValidationSeverity.Error, assetPath, field + ".layerSlot", "No layer slot is set.", "Set the slot this sprite paints, e.g. layer-weapon."));
                else if (!known.Contains(entry.layerSlot))
                    results.Add(new ValidationResult(ValidationSeverity.Error, assetPath, field + ".layerSlot",
                        $"'{entry.layerSlot.Value}' is not one of the character's layer slots.", "Fix the typo, or add the slot to the compositor's slot list."));
                else if (!painted.Add(entry.layerSlot))
                    results.Add(new ValidationResult(ValidationSeverity.Error, assetPath, field + ".layerSlot",
                        $"Layer slot '{entry.layerSlot.Value}' is painted more than once by this profile.", "Keep one entry per layer slot."));

                if (entry.sprite == null)
                    results.Add(new ValidationResult(ValidationSeverity.Error, assetPath, field + ".sprite", "The layer entry has no sprite.", "Assign the sprite, or remove the entry."));

                if (entry.tint.a <= 0f)
                    results.Add(new ValidationResult(ValidationSeverity.Warning, assetPath, field + ".tint",
                        "The tint is fully transparent, so this layer will be invisible.", "Set the tint alpha above 0 (a new inspector entry can copy a transparent one)."));
            }

            foreach (var hidden in profile.HiddenLayerSlots)
            {
                if (!hidden.IsValid)
                {
                    results.Add(new ValidationResult(ValidationSeverity.Error, assetPath, "hiddenLayerSlots", "A hidden layer slot has no id.", "Remove the empty entry."));
                    continue;
                }

                if (!known.Contains(hidden))
                    results.Add(new ValidationResult(ValidationSeverity.Warning, assetPath, "hiddenLayerSlots",
                        $"'{hidden.Value}' is not one of the character's layer slots, so hiding it does nothing.", "Fix the typo, or add the slot to the compositor's slot list."));

                if (painted.Contains(hidden))
                    results.Add(new ValidationResult(ValidationSeverity.Error, assetPath, "hiddenLayerSlots",
                        $"The profile hides layer '{hidden.Value}' and also paints it, so it would paint nothing.", "Remove the slot from the hidden list or from the painted layers."));
            }

            if (profile.SpriteLayers.Count == 0 && profile.HiddenLayerSlots.Count == 0)
                results.Add(new ValidationResult(ValidationSeverity.Warning, assetPath, "spriteLayers", "A Sprite profile with no layers and no hidden slots does nothing.", "Add a layer, or set the kind to None."));

            if (profile.VisualPrefab != null || profile.SocketId.IsValid || profile.BoneMap != null || profile.BodyRegionId.IsValid || profile.HiddenBodyRegions.Count > 0)
                results.Add(new ValidationResult(ValidationSeverity.Warning, assetPath, "kind",
                    "3D fields (prefab, socket, bone map, body regions) are authored on a Sprite profile and will be ignored.", "Clear them, or change the kind."));

            return results;
        }
    }
}
