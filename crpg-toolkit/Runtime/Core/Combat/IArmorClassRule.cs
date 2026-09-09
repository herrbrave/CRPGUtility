namespace CRPG.Core
{
    /// <summary>Defender-side derived stat: how hard this character is to hit. See IAttackBonusRule's remarks
    /// for why this lives in CRPG.Core rather than alongside its default implementation in CRPG.Combat.</summary>
    public interface IArmorClassRule
    {
        int ComputeArmorClass(RuntimeEntityId target);
    }
}
