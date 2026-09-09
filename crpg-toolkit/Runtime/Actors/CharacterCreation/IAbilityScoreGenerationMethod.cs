using System.Collections.Generic;

namespace CRPG.Actors
{
    public interface IAbilityScoreGenerationMethod
    {
        StatBlock Generate(IReadOnlyList<StatDefinition> stats);
    }
}
