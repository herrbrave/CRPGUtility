namespace CRPG.Core
{
    /// <summary>What a roll was for, so combat log / UI presentation can label it without the dice engine
    /// knowing anything about a specific ruleset's mechanics.</summary>
    public enum RollPurpose
    {
        Generic,
        AttackRoll,
        DamageRoll,
        SavingThrow,
        AbilityCheck,
        Initiative,
        DeathSavingThrow
    }
}
