using System;
using System.Collections.Generic;
using System.Linq;
using CRPG.Core;

namespace VikingRaid.Appearance
{
    /// <summary>Seeded randomisation for the creator's Randomize button and for tests: the same seed always rolls the same NPC.</summary>
    public static class ModelAppearanceRandomizer
    {
        /// <summary>The chance an OPTIONAL row rolls "None" rather than one of its options.</summary>
        public const double NoneChance = 0.4;

        /// <param name="tab">Roll only this tab's rows (null: every row). The rest is taken from <paramref name="basis"/> unchanged.</param>
        public static Dictionary<DefinitionId, DefinitionId> Roll(ModelAppearanceCatalog catalog, int seed, string tab = null, IReadOnlyDictionary<DefinitionId, DefinitionId> basis = null)
        {
            var random = new Random(seed);
            var result = basis != null ? basis.ToDictionary(p => p.Key, p => p.Value) : new Dictionary<DefinitionId, DefinitionId>();
            foreach (var slot in catalog.Slots)
            {
                if (tab != null && slot.tab != tab) continue;

                var key = new DefinitionId(slot.id);
                // draw in a fixed order whatever the outcome, so adding a row later does not reshuffle every earlier row for the same seed
                var noneRoll = random.NextDouble();
                var pick = slot.options.Count == 0 ? -1 : random.Next(slot.options.Count);
                if (pick < 0 || (!slot.required && noneRoll < NoneChance)) result.Remove(key);
                else result[key] = new DefinitionId(slot.options[pick].id);
            }

            return result;
        }
    }
}
