using CRPG.Presentation.Sprites;
using NUnit.Framework;

namespace CRPG.Tests.EditMode
{
    /// <summary>2D campaign Task 2.2: the shared world-Z to draw-order contract (characters, props, tall walls).</summary>
    public class SpriteSortingContractTests
    {
        private const int Cap = SpriteSortingContract.MaxSlotsPerObject;

        [Test]
        public void BaseOrderAnchors()
        {
            Assert.AreEqual(0, SpriteSortingContract.BaseOrder(0f));
            Assert.AreEqual(-128, SpriteSortingContract.BaseOrder(1f));
            Assert.AreEqual(128, SpriteSortingContract.BaseOrder(-1f));
            Assert.AreEqual(-16, SpriteSortingContract.BaseOrder(0.125f));
        }

        [Test]
        public void RoundingIsToTheNearestEighthAwayFromMidpoints()
        {
            // Not 0.0625: Math.Round is banker's rounding, so a midpoint case would pin an accident.
            Assert.AreEqual(0, SpriteSortingContract.BaseOrder(0.06f));
            Assert.AreEqual(-16, SpriteSortingContract.BaseOrder(0.07f));
        }

        [Test]
        public void LowerZStacksNeverInterleaveWithTheRowBehindEvenAtTheSlotCap()
        {
            for (var a = -400; a < 400; a++)
            for (var gap = 1; gap <= 3; gap++)
            {
                var front = a / 8f;
                var back = (a + gap) / 8f; // higher Z = behind
                var frontMin = SpriteSortingContract.SlotOrder(SpriteSortingContract.BaseOrder(front), 0);
                var backMax = SpriteSortingContract.SlotOrder(SpriteSortingContract.BaseOrder(back), Cap - 1);
                Assert.Greater(frontMin, backMax, $"z {front} vs {back}");
            }
        }

        [Test]
        public void ThePropLaneSitsBetweenItsOwnRowAndTheRowBehindAtTheSlotCap()
        {
            for (var k = -400; k < 400; k++)
            {
                var row = k / 8f;
                var rowBehind = (k + 1) / 8f;
                var propOrder = SpriteSortingContract.OrderFor(SpriteSortingContract.BaseOrder(row), SpriteSortingContract.PropBias);
                var characterMin = SpriteSortingContract.SlotOrder(SpriteSortingContract.BaseOrder(row), 0);
                var behindCharacterMax = SpriteSortingContract.SlotOrder(SpriteSortingContract.BaseOrder(rowBehind), Cap - 1);
                var behindPropOrder = SpriteSortingContract.OrderFor(SpriteSortingContract.BaseOrder(rowBehind), SpriteSortingContract.PropBias);

                Assert.Less(propOrder, characterMin, $"row {row}: a prop draws behind a character in its own row");
                Assert.Greater(propOrder, behindCharacterMax, $"row {row}: a prop draws in front of every layer of a full character in the row behind");
                Assert.Greater(propOrder, behindPropOrder, $"row {row}: and in front of a prop in the row behind");
            }
        }

        [Test]
        public void SlotOrdersStayInsideTheirBand()
        {
            var band = SpriteSortingContract.BaseOrder(3.5f);
            for (var slot = 0; slot < Cap; slot++)
            {
                var order = SpriteSortingContract.SlotOrder(band, slot);
                Assert.Greater(order, band);
                Assert.Less(order, band + SpriteSortingContract.OrdersPerRow);
            }
        }

        [TestCase(1e9f)]
        [TestCase(float.MaxValue)]
        [TestCase(-1e9f)]
        [TestCase(-float.MaxValue)]
        [TestCase(257f)]
        [TestCase(-257f)]
        [TestCase(float.PositiveInfinity)]
        [TestCase(float.NegativeInfinity)]
        [TestCase(float.NaN)]
        public void OutOfRangeAndNonFiniteZAreClampedIntoInt16(float z)
        {
            var band = SpriteSortingContract.BaseOrder(z, out var clamped);

            Assert.IsTrue(clamped);
            Assert.GreaterOrEqual(band, short.MinValue);
            Assert.LessOrEqual(band, short.MaxValue - (SpriteSortingContract.OrdersPerRow - 1));
            Assert.LessOrEqual(SpriteSortingContract.SlotOrder(band, Cap - 1), short.MaxValue);
        }

        [Test]
        public void InRangeZIsNotReportedAsClamped()
        {
            SpriteSortingContract.BaseOrder(255.9f, out var clamped);
            Assert.IsFalse(clamped);
            SpriteSortingContract.BaseOrder(-256f, out clamped);
            Assert.IsFalse(clamped);
        }

        [Test]
        public void OrderForNeverExceedsInt16WhateverTheInputs()
        {
            Assert.AreEqual(short.MinValue, SpriteSortingContract.OrderFor(short.MinValue, -1000));
            Assert.AreEqual(short.MaxValue, SpriteSortingContract.OrderFor(short.MaxValue, 1000));
            Assert.AreEqual(short.MaxValue, SpriteSortingContract.OrderFor(int.MaxValue, int.MaxValue));
            Assert.AreEqual(short.MinValue, SpriteSortingContract.OrderFor(int.MinValue, int.MinValue));
        }
    }
}
