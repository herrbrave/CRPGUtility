using System.Collections.Generic;

namespace CRPG.Actors
{
    public sealed class StandardArrayGenerationMethod : IAbilityScoreGenerationMethod
    {
        public static readonly IReadOnlyList<int> DefaultArray = new[] { 15, 14, 13, 12, 10, 8 };

        private readonly IReadOnlyList<int> array;

        public StandardArrayGenerationMethod(IReadOnlyList<int> array = null)
        {
            this.array = array ?? DefaultArray;
        }

        /// <summary>Assigns the array values to `stats` in the order given -- reorder `stats` to control which
        /// score lands where; the drag-and-drop assignment UI itself lives in a presenter, not here.</summary>
        public StatBlock Generate(IReadOnlyList<StatDefinition> stats)
        {
            var block = new StatBlock();
            for (var i = 0; i < stats.Count && i < array.Count; i++) block.SetScore(stats[i].Id, array[i]);
            return block;
        }
    }
}
