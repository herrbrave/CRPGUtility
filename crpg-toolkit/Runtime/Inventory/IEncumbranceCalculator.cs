using CRPG.Core;

namespace CRPG.Inventory
{
    public interface IEncumbranceCalculator
    {
        /// <summary>Total carried weight relevant to `character` under this mode -- e.g. the whole party's
        /// combined weight for PartyCombined, or just what this character has equipped for PerCharacter.</summary>
        float CalculateTotalWeight(PartyInventoryState inventory, IRulesDatabase rulesDatabase, RuntimeEntityId character);
    }
}
