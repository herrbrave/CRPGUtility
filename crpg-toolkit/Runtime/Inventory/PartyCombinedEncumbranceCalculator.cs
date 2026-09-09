using CRPG.Core;

namespace CRPG.Inventory
{
    /// <summary>The sample project's default (spec §43): every item anyone carries or has equipped counts
    /// against one shared party capacity.</summary>
    public sealed class PartyCombinedEncumbranceCalculator : IEncumbranceCalculator
    {
        public float CalculateTotalWeight(PartyInventoryState inventory, IRulesDatabase rulesDatabase, RuntimeEntityId character)
        {
            var total = 0f;
            foreach (var item in inventory.AllItems)
            {
                var definition = rulesDatabase.Resolve<ItemDefinition>(item.DefinitionId);
                total += definition.Weight * item.Quantity;
            }

            return total;
        }
    }
}
