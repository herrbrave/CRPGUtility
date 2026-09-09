using System;

namespace CRPG.Core
{
    /// <summary>Reproducible RNG for save/load and replay: the same seed always produces the same roll sequence.</summary>
    public sealed class SeededRandomSource : IRandomSource
    {
        private readonly Random random;

        public SeededRandomSource(int seed)
        {
            Seed = seed;
            random = new Random(seed);
        }

        public int Seed { get; }

        public int Next(int minInclusive, int maxExclusive) => random.Next(minInclusive, maxExclusive);
    }
}
