using CRPG.AI;
using NUnit.Framework;

namespace CRPG.Tests.EditMode
{
    public class UtilityMathTests
    {
        [Test]
        public void CombineScoresReturnsZeroForAnEmptyList()
        {
            Assert.AreEqual(0f, UtilityMath.CombineScores(new float[0]));
        }

        [Test]
        public void CombineScoresLeavesASingleScoreUnchanged()
        {
            // n=1 -> modificationFactor is 0, so the compensation step is a no-op.
            Assert.AreEqual(0.7f, UtilityMath.CombineScores(new[] { 0.7f }), 0.0001f);
        }

        [Test]
        public void CombineScoresAppliesTheCompensationFactorForMultipleConsiderations()
        {
            // product = 0.64, modFactor = 1 - 1/2 = 0.5, makeUp = 0.36 * 0.5 = 0.18, final = 0.64 + 0.18*0.64
            var result = UtilityMath.CombineScores(new[] { 0.8f, 0.8f });

            Assert.AreEqual(0.7552f, result, 0.0001f);
            Assert.Greater(result, 0.64f); // compensation genuinely pulled the naive product back up
        }

        [Test]
        public void CombineScoresTreatsAZeroScoreAsAHardVeto()
        {
            Assert.AreEqual(0f, UtilityMath.CombineScores(new[] { 0.9f, 0f, 0.9f }));
        }
    }
}
