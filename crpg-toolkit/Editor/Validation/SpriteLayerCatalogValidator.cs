using System.Collections.Generic;
using System.Linq;
using CRPG.Core;
using CRPG.Presentation.Sprites;

namespace CRPG.Editor.Validation
{
    /// <summary>
    /// 2D campaign Task 2.4: validates <see cref="SpriteLayerCatalog"/> assets -- pure logic, no scene. A bad catalog does not
    /// break a game (an unresolvable option degrades to an empty layer with a report), which is exactly why authoring-time
    /// checks matter. Never mutates the asset.
    /// </summary>
    public static class SpriteLayerCatalogValidator
    {
        /// <param name="knownLayerSlots">The layer slots the characters' compositors have; defaults to the compositor's default set.</param>
        public static List<ValidationResult> Validate(SpriteLayerCatalog catalog, string assetPath, IReadOnlyCollection<DefinitionId> knownLayerSlots = null)
        {
            var results = new List<ValidationResult>();
            if (catalog == null) return results;

            var known = new HashSet<DefinitionId>(knownLayerSlots ?? SpriteLayerCompositor.DefaultSlots());

            if (!catalog.LayerSlot.IsValid)
                results.Add(new ValidationResult(ValidationSeverity.Error, assetPath, "layerSlot", "The catalog has no layer slot.", "Set the layer slot it provides options for, e.g. layer-hair."));
            else if (!known.Contains(catalog.LayerSlot))
                results.Add(new ValidationResult(ValidationSeverity.Warning, assetPath, "layerSlot",
                    $"'{catalog.LayerSlot.Value}' is not one of the default layer slots.", "Fine if your compositors add this slot; otherwise fix the typo."));

            if (catalog.Options.Count == 0)
                results.Add(new ValidationResult(ValidationSeverity.Warning, assetPath, "options", "The catalog has no options.", "Add options, or delete the catalog."));

            var seen = new HashSet<DefinitionId>();
            for (var i = 0; i < catalog.Options.Count; i++)
            {
                var option = catalog.Options[i];
                var field = $"options[{i}]";
                if (option == null)
                {
                    results.Add(new ValidationResult(ValidationSeverity.Error, assetPath, field, "The option entry is empty.", "Remove it or fill it in."));
                    continue;
                }

                if (!option.optionId.IsValid)
                    results.Add(new ValidationResult(ValidationSeverity.Error, assetPath, field + ".optionId", "The option has no id.", "Give it a stable id (never rename it once shipped)."));
                else if (!seen.Add(option.optionId))
                    results.Add(new ValidationResult(ValidationSeverity.Error, assetPath, field + ".optionId",
                        $"Option id '{option.optionId.Value}' appears more than once in this catalog.", "Option ids must be unique within a catalog."));

                if (option.sprite == null)
                    results.Add(new ValidationResult(ValidationSeverity.Error, assetPath, field + ".sprite", "The option has no sprite.", "Assign the sprite, or remove the option."));

                if (option.tint.a <= 0f)
                    results.Add(new ValidationResult(ValidationSeverity.Warning, assetPath, field + ".tint",
                        "The tint is fully transparent, so this option is invisible.", "Set the tint alpha above 0."));
            }

            return results;
        }

        /// <summary>Cross-asset check: two catalogs claiming one layer slot (the runtime source keeps only one).</summary>
        public static List<ValidationResult> ValidateSet(IEnumerable<(SpriteLayerCatalog catalog, string assetPath)> catalogs)
        {
            var results = new List<ValidationResult>();
            foreach (var group in catalogs.Where(c => c.catalog != null && c.catalog.LayerSlot.IsValid).GroupBy(c => c.catalog.LayerSlot))
            {
                var members = group.ToList();
                if (members.Count < 2) continue;
                var names = string.Join(", ", members.Select(m => m.assetPath));
                foreach (var member in members)
                    results.Add(new ValidationResult(ValidationSeverity.Error, member.assetPath, "layerSlot",
                        $"Layer slot '{group.Key.Value}' is claimed by {members.Count} catalogs ({names}); only one is used at runtime.", "Keep one catalog per layer slot."));
            }

            return results;
        }
    }
}
