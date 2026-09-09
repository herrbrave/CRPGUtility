using System.Collections.Generic;
using CRPG.Core;

namespace CRPG.Actors
{
    public sealed class ManualEntryGenerationMethod : IAbilityScoreGenerationMethod
    {
        private readonly IReadOnlyDictionary<DefinitionId, int> scores;

        public ManualEntryGenerationMethod(IReadOnlyDictionary<DefinitionId, int> scores)
        {
            this.scores = scores;
        }

        public StatBlock Generate(IReadOnlyList<StatDefinition> stats)
        {
            var block = new StatBlock();
            foreach (var stat in stats)
            {
                if (scores.TryGetValue(stat.Id, out var score)) block.SetScore(stat.Id, score);
            }

            return block;
        }
    }
}
