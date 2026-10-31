using System;
using CRPG.Core;

namespace CRPG.Actors
{
    /// <summary>One character's current/maximum for a single <see cref="ResourceDefinition"/>
    /// (current hit points, remaining 3rd-level spell slots, rage uses left, ...).</summary>
    public sealed class ResourceState
    {
        private int unspentBalance;
        private int? effectiveMaximum;
        private int Capacity => effectiveMaximum ?? Max + TemporaryMaximum;

        public ResourceState(DefinitionId resourceId, int max, int? current = null)
        {
            ResourceId = resourceId;
            Max = max;
            Current = current ?? max;
        }

        public DefinitionId ResourceId { get; }
        public int Max { get; set; }
        // A speed loss can make already-spent distance exceed the new allowance. Retain that debt
        // internally so removing the penalty cannot refund movement, while public resources stay nonnegative.
        public int Current { get => Math.Max(0, unspentBalance); set => unspentBalance = value; }

        /// <summary>Additional turn-budget capacity, separate from the permanent maximum. Reset on the next full turn refresh.</summary>
        public int TemporaryMaximum { get; private set; }

        /// <summary>Changes this turn's capacity without changing how much has already been spent.</summary>
        public void SetEffectiveMaximum(int maximum)
        {
            if (maximum < 0) throw new ArgumentOutOfRangeException(nameof(maximum));
            unspentBalance += maximum - Capacity;
            effectiveMaximum = maximum;
        }

        public void GrantTemporaryMaximum(int amount)
        {
            if (amount < 0) throw new ArgumentOutOfRangeException(nameof(amount));
            TemporaryMaximum += amount;
            if (effectiveMaximum.HasValue) effectiveMaximum += amount;
            unspentBalance += amount;
        }

        public void ClearTemporaryMaximum()
        {
            unspentBalance = Math.Max(0, Math.Min(Max, unspentBalance + Max - Capacity));
            TemporaryMaximum = 0;
            effectiveMaximum = null;
        }

        /// <returns>The amount actually spent, clamped to what was available.</returns>
        public int Spend(int amount)
        {
            var spent = Math.Min(amount, Current);
            unspentBalance -= spent;
            return spent;
        }

        /// <returns>The amount actually restored, clamped to the maximum.</returns>
        public int Restore(int amount)
        {
            var before = Current;
            unspentBalance = Math.Min(Capacity, unspentBalance + amount);
            return Current - before;
        }

        public void RestoreToFull()
        {
            TemporaryMaximum = 0;
            effectiveMaximum = null;
            Current = Max;
        }
    }
}
