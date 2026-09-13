using System;
using System.Collections.Generic;
using CRPG.Actors;
using CRPG.Core;

namespace CRPG.Rules.SRD5
{
    /// <summary>5e-standard point buy: scores 8-15, with the standard cost table, validated against a budget.</summary>
    public sealed class PointBuyGenerationMethod : IAbilityScoreGenerationMethod
    {
        private static readonly Dictionary<int, int> CostByScore = new()
        {
            { 8, 0 }, { 9, 1 }, { 10, 2 }, { 11, 3 }, { 12, 4 }, { 13, 5 }, { 14, 7 }, { 15, 9 }
        };

        private readonly IReadOnlyDictionary<DefinitionId, int> desiredScores;
        private readonly int budget;

        public PointBuyGenerationMethod(IReadOnlyDictionary<DefinitionId, int> desiredScores, int budget = 27)
        {
            this.desiredScores = desiredScores;
            this.budget = budget;
        }

        public StatBlock Generate(IReadOnlyList<StatDefinition> stats)
        {
            var totalCost = 0;
            foreach (var stat in stats)
            {
                var score = desiredScores.TryGetValue(stat.Id, out var value) ? value : 8;
                if (!CostByScore.TryGetValue(score, out var cost))
                    throw new ArgumentOutOfRangeException(nameof(desiredScores), $"Point buy scores must be between 8 and 15 (got {score}).");
                totalCost += cost;
            }

            if (totalCost > budget)
                throw new InvalidOperationException($"Point buy allocation costs {totalCost}, which exceeds the budget of {budget}.");

            var block = new StatBlock();
            foreach (var stat in stats)
            {
                var score = desiredScores.TryGetValue(stat.Id, out var value) ? value : 8;
                block.SetScore(stat.Id, score);
            }

            return block;
        }
    }
}
