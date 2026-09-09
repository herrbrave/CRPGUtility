namespace CRPG.Core
{
    /// <summary>Caps a character's effective movement this turn -- Grappled/Restrained setting it to 0,
    /// Exhaustion or heavy encumbrance halving it, and so on -- without mutating the underlying Movement
    /// resource's own Max/Current (those still track "how far can I move today" independent of momentary
    /// conditions). Lives in CRPG.Core (not CRPG.Combat, where its default implementation does) for the same
    /// reason IAttackBonusRule/ISavingThrowRule do.</summary>
    public interface IMovementSpeedRule
    {
        float ComputeEffectiveSpeed(RuntimeEntityId entity, float baseSpeed);
    }
}
