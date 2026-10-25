namespace CRPG.Core
{
    /// <summary>Whether another enemy of the target (an ally of the attacker) is within reach of it: the second way to qualify for 5e Sneak Attack, besides advantage. Needs the live battlefield (who stands where, who is an ally), so the game supplies it per fight.</summary>
    public interface IAllyAdjacencyQuery
    {
        bool IsAllyAdjacentToTarget(RuntimeEntityId attacker, RuntimeEntityId target);
    }
}
