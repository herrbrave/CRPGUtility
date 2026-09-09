using CRPG.Core;

namespace CRPG.Inventory
{
    /// <summary>Only what a specific character has equipped counts against their own capacity.</summary>
    public sealed class PerCharacterEncumbranceCalculator : IEncumbranceCalculator
    {
        public float CalculateTotalWeight(PartyInventoryState inventory, IRulesDatabase rulesDatabase, RuntimeEntityId character)
        {
            var total = 0f;
            foreach (var item in inventory.ItemsEquippedBy(character))
            {
                var definition = rulesDatabase.Resolve<ItemDefinition>(item.DefinitionId);
                total += definition.Weight * item.Quantity;
            }

            return total;
        }
    }
}
