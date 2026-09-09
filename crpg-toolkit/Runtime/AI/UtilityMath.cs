using System.Collections.Generic;
using UnityEngine;

namespace CRPG.AI
{
    /// <summary>Pure IAUS scoring math, kept separate from anything stateful so it's trivially unit-testable.</summary>
    public static class UtilityMath
    {
        /// <summary>Multiplies every score together (a 0 vetoes the whole Decision), then applies the standard
        /// IAUS compensation factor so stacking many good-but-not-perfect scores doesn't decay toward zero just
        /// from multiplying enough fractions together. A genuine 0 still stays exactly 0.</summary>
        public static float CombineScores(IReadOnlyList<float> scores)
        {
            if (scores.Count == 0) return 0f;

            var product = 1f;
            foreach (var score in scores) product *= Mathf.Clamp01(score);

            if (product <= 0f) return 0f;

            var modificationFactor = 1f - 1f / scores.Count;
            var makeUpValue = (1f - product) * modificationFactor;
            return product + makeUpValue * product;
        }
    }
}
