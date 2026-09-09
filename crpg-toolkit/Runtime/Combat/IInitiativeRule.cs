using CRPG.Core;

namespace CRPG.Combat
{
    /// <summary>Rolls initiative for one combatant. SRD5's "d20 + Dexterity modifier" is one implementation;
    /// swapping this interface is how another ruleset replaces it entirely.</summary>
    public interface IInitiativeRule
    {
        RollResult RollInitiative(RuntimeEntityId combatant);
    }
}
