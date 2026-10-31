namespace CRPG.Core
{
    /// <summary>
    /// Who can see whom. The one answer rules, abilities and AI share; the ruleset implements it (it needs conditions and senses), the assemblies below the ruleset take it as an optional dependency.
    /// <see cref="CanSee"/> is true for <see cref="Visibility.Clear"/> and <see cref="Visibility.LightlyObscured"/>: a heavily obscured creature is effectively unseen, a lightly obscured one only harder to notice.
    /// </summary>
    public interface IPerceptionQuery
    {
        Visibility SightOf(RuntimeEntityId observer, RuntimeEntityId target);
        bool CanSee(RuntimeEntityId observer, RuntimeEntityId target);
    }
}
