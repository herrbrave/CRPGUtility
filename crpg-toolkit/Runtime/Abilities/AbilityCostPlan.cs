using System.Collections.Generic;
using CRPG.Actors;

namespace CRPG.Abilities
{
    /// <summary>One resource+amount in a built AbilityCostPlan. Distinct from AbilityCost (authored content)
    /// deliberately -- this is a computed runtime value, not authored data, even though it carries the same
    /// two fields.</summary>
    public readonly struct AbilityCostEntry
    {
        public AbilityCostEntry(ResourceDefinition resource, int amount)
        {
            Resource = resource;
            Amount = amount;
        }

        public ResourceDefinition Resource { get; }
        public int Amount { get; }
    }

    /// <summary>
    /// Task 4.2: the single authoritative place ritual/upcast "alternate cost policies" transform an ability's
    /// authored Costs rather than bypassing them outright -- both AbilityResolutionService.Validate
    /// (affordability) and Execute (actual spend) call Build() and act on the same plan, so there is exactly
    /// one place cost semantics live. Fixes two real bugs in the pre-4.2 code: (1) a ritual cast used to skip
    /// every authored cost, including any action-economy cost unrelated to the spell slot; (2) upcasting used
    /// to skip every authored cost except the chosen slot, for the same reason. Both now keep every
    /// non-slot-shaped cost entry untouched, only substituting/removing the slot-shaped one.
    /// </summary>
    public sealed class AbilityCostPlan
    {
        private AbilityCostPlan(IReadOnlyList<AbilityCostEntry> entries)
        {
            Entries = entries;
        }

        public IReadOnlyList<AbilityCostEntry> Entries { get; }

        public static AbilityCostPlan Build(AbilityDefinition ability, bool asRitual, ResourceDefinition upcastWithSlot)
        {
            // Aggregate by resource first -- two authored AbilityCost entries against the same resource must
            // be treated as one combined requirement, not checked/spent independently (the "grouped duplicate
            // costs" bug: two 2-unit costs against a 3-unit pool used to both individually pass).
            var aggregated = new Dictionary<ResourceDefinition, int>();
            foreach (var cost in ability.Costs)
            {
                aggregated.TryGetValue(cost.Resource, out var existing);
                aggregated[cost.Resource] = existing + cost.Amount;
            }

            // An ability with no authored slot-shaped cost at all (e.g. a class feature with only an action
            // cost) simply has nothing for asRitual/upcastWithSlot to substitute or remove -- every authored
            // cost is kept as-is in that case too, matching ordinary non-alternate casting.
            var entries = new List<AbilityCostEntry>(aggregated.Count);
            foreach (var (resource, amount) in aggregated)
            {
                var isSlotShaped = resource != null && resource.SpellSlotLevel > 0;
                if (isSlotShaped)
                {
                    if (asRitual) continue; // SRD5 ritual casting spends no slot at all.
                    if (upcastWithSlot != null)
                    {
                        entries.Add(new AbilityCostEntry(upcastWithSlot, 1));
                        continue;
                    }
                }

                entries.Add(new AbilityCostEntry(resource, amount));
            }

            return new AbilityCostPlan(entries);
        }
    }
}
