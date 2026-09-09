using System;
using System.Collections.Generic;

namespace CRPG.Core
{
    /// <summary>Deterministic random source for unit tests: enqueue exact die faces and they're returned in order.
    /// Lets combat/rules tests say "the next d20 will be 14" instead of tolerating randomness.</summary>
    public sealed class TestRandomSource : IRandomSource
    {
        private readonly Queue<int> queuedResults = new();

        public void EnqueueResult(int value) => queuedResults.Enqueue(value);

        public void EnqueueResults(params int[] values)
        {
            foreach (var value in values) queuedResults.Enqueue(value);
        }

        public int Next(int minInclusive, int maxExclusive)
        {
            if (queuedResults.Count == 0)
                throw new InvalidOperationException("TestRandomSource ran out of queued results. Call EnqueueResult before rolling.");

            var value = queuedResults.Dequeue();
            if (value < minInclusive || value >= maxExclusive)
                throw new InvalidOperationException($"Queued result {value} is outside the requested range [{minInclusive}, {maxExclusive}).");

            return value;
        }
    }
}
