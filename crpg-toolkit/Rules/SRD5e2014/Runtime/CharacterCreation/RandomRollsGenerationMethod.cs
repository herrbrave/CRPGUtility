using System;
using System.Collections.Generic;
using CRPG.Actors;
using CRPG.Core;

namespace CRPG.Rules.SRD5
{
    /// <summary>Classic "roll 4d6, drop the lowest" per stat.</summary>
    public sealed class RandomRollsGenerationMethod : IAbilityScoreGenerationMethod
    {
        private readonly IRandomSource random;

        public RandomRollsGenerationMethod(IRandomSource random)
        {
            this.random = random;
        }

        public StatBlock Generate(IReadOnlyList<StatDefinition> stats)
        {
            var block = new StatBlock();
            foreach (var stat in stats) block.SetScore(stat.Id, RollOne());
            return block;
        }

        private int RollOne()
        {
            var rolls = new int[4];
            for (var i = 0; i < 4; i++) rolls[i] = random.RollDie(6);
            Array.Sort(rolls);
            return rolls[1] + rolls[2] + rolls[3]; // drop the lowest
        }
    }
}
