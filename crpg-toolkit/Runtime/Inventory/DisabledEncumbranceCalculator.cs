using CRPG.Core;

namespace CRPG.Inventory
{
    public sealed class DisabledEncumbranceCalculator : IEncumbranceCalculator
    {
        public float CalculateTotalWeight(PartyInventoryState inventory, IRulesDatabase rulesDatabase, RuntimeEntityId character) => 0f;
    }
}
