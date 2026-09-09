using CRPG.Core;
using NUnit.Framework;

namespace CRPG.Tests.EditMode
{
    public class DiceServiceTests
    {
        [Test]
        public void FixedRollProducesExpectedTotal()
        {
            var random = new TestRandomSource();
            random.EnqueueResult(14);
            var dice = new DiceService(random);

            var result = dice.Roll(new RollRequest(DiceExpression.Parse("1d20+5")));

            Assert.AreEqual(19, result.Total);
        }

        [Test]
        public void AdvantageRollsTwiceAndKeepsHigherTotal()
        {
            var random = new TestRandomSource();
            random.EnqueueResults(8, 17);
            var dice = new DiceService(random);

            var result = dice.Roll(new RollRequest(DiceExpression.Parse("1d20"), advantage: AdvantageState.Advantage));

            Assert.AreEqual(17, result.DiceTotal);
        }

        [Test]
        public void DisadvantageRollsTwiceAndKeepsLowerTotal()
        {
            var random = new TestRandomSource();
            random.EnqueueResults(8, 17);
            var dice = new DiceService(random);

            var result = dice.Roll(new RollRequest(DiceExpression.Parse("1d20"), advantage: AdvantageState.Disadvantage));

            Assert.AreEqual(8, result.DiceTotal);
        }

        [Test]
        public void DiceExpressionParsesStandardNotation()
        {
            var expression = DiceExpression.Parse("2d6+3");

            Assert.AreEqual(2, expression.Count);
            Assert.AreEqual(6, expression.Sides);
            Assert.AreEqual(3, expression.Modifier);
        }

        [Test]
        public void CombineCancelsAdvantageAndDisadvantage()
        {
            var combined = AdvantageStateExtensions.Combine(new[] { AdvantageState.Advantage, AdvantageState.Disadvantage });

            Assert.AreEqual(AdvantageState.Normal, combined);
        }
    }
}
