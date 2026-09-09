using System;
using CRPG.Core;

namespace CRPG.Actors
{
    /// <summary>One character's current/maximum for a single <see cref="ResourceDefinition"/>
    /// (current hit points, remaining 3rd-level spell slots, rage uses left, ...).</summary>
    public sealed class ResourceState
    {
        public ResourceState(DefinitionId resourceId, int max, int? current = null)
        {
            ResourceId = resourceId;
            Max = max;
            Current = current ?? max;
        }

        public DefinitionId ResourceId { get; }
        public int Max { get; set; }
        public int Current { get; set; }

        /// <returns>The amount actually spent, clamped to what was available.</returns>
        public int Spend(int amount)
        {
            var spent = Math.Min(amount, Current);
            Current -= spent;
            return spent;
        }

        /// <returns>The amount actually restored, clamped to the maximum.</returns>
        public int Restore(int amount)
        {
            var before = Current;
            Current = Math.Min(Max, Current + amount);
            return Current - before;
        }

        public void RestoreToFull() => Current = Max;
    }
}
