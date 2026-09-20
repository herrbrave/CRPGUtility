using System.Collections.Generic;
using CRPG.Core;
using UnityEngine;

namespace CRPG.Presentation.Sprites
{
    /// <summary>
    /// Resolves appearance options through the <see cref="SpriteLayerCatalog"/> assets registered in a rules database, indexed
    /// by layer slot when constructed. Two catalogs claiming one slot: the one with the smaller ordinal id wins (deterministic:
    /// the database enumerates definitions in dictionary order, which is not a contract), with one warning naming both.
    /// Content registered after construction needs <see cref="Rebuild"/> (each SpriteAppearanceBinder builds its own source when
    /// it applies, so the next spawn sees new content).
    /// </summary>
    public sealed class SpriteCatalogSource : ISpriteOptionSource
    {
        private readonly IRulesDatabase rulesDatabase;
        private readonly Dictionary<DefinitionId, Dictionary<DefinitionId, SpriteOptionEntry>> optionsBySlot = new();

        public SpriteCatalogSource(IRulesDatabase rulesDatabase)
        {
            this.rulesDatabase = rulesDatabase;
            Rebuild();
        }

        public void Rebuild()
        {
            optionsBySlot.Clear();
            if (rulesDatabase == null) return;

            var winners = new Dictionary<DefinitionId, SpriteLayerCatalog>();
            foreach (var catalog in rulesDatabase.All<SpriteLayerCatalog>())
            {
                var slot = catalog.LayerSlot;
                if (!slot.IsValid)
                {
                    Debug.LogWarning($"[SpriteCatalogSource] Catalog '{catalog.name}' has no layer slot; ignored.");
                    continue;
                }

                if (winners.TryGetValue(slot, out var existing))
                {
                    var keepExisting = string.CompareOrdinal(existing.Id.Value, catalog.Id.Value) <= 0;
                    Debug.LogWarning($"[SpriteCatalogSource] Catalogs '{existing.name}' and '{catalog.name}' both claim layer slot '{slot.Value}'; " +
                                     $"using '{(keepExisting ? existing : catalog).name}' (smaller id).");
                    if (keepExisting) continue;
                }

                winners[slot] = catalog;
            }

            foreach (var pair in winners)
            {
                var map = new Dictionary<DefinitionId, SpriteOptionEntry>();
                foreach (var entry in pair.Value.Options)
                    if (entry != null && entry.optionId.IsValid) map.TryAdd(entry.optionId, entry); // first wins; the validator flags duplicates
                optionsBySlot[pair.Key] = map;
            }
        }

        public bool TryGetOption(DefinitionId slot, DefinitionId option, out SpriteOption result)
        {
            if (optionsBySlot.TryGetValue(slot, out var map) && map.TryGetValue(option, out var entry) && entry.sprite != null)
            {
                result = new SpriteOption(entry.sprite, entry.tint);
                return true;
            }

            result = default;
            return false;
        }
    }
}
