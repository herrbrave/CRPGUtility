namespace CRPG.Core
{
    /// <summary>Adjusts a rolled damage amount for the target's resistance/immunity/vulnerability to the given
    /// damage type. Lives in CRPG.Core (not CRPG.Combat, where its default implementation does) for the same
    /// reason IAttackBonusRule/IArmorClassRule/ISavingThrowRule do: DamageEffect lives in CRPG.Abilities, which
    /// can't reference CRPG.Combat without a circular dependency.</summary>
    public interface IDamageResistanceRule
    {
        int ApplyResistance(RuntimeEntityId target, DefinitionId damageTypeId, int rawAmount);
    }
}
