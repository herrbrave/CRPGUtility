namespace CRPG.Core
{
    /// <summary>Receives actual attack rolls after advantage is captured, including misses; previews never call this.</summary>
    public interface IAttackRollObserver
    {
        void OnAttackRolled(RuntimeEntityId attacker, RuntimeEntityId target);
    }
}
