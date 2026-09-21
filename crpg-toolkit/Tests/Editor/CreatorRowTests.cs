using CRPG.Core;
using CRPG.Presentation.Creator;
using NUnit.Framework;

namespace CRPG.Tests.EditMode
{
    /// <summary>2D campaign Task 4.1: the wrap-around, selection and change-notification rules of one creator row.</summary>
    public class CreatorRowTests
    {
        private static CreatorRow Row(int count)
        {
            var options = new CreatorOption[count];
            for (var i = 0; i < count; i++) options[i] = new CreatorOption(new DefinitionId("opt-" + i), "Option " + i);
            return new CreatorRow("hair", "Hair", options);
        }

        [Test]
        public void AdvanceWrapsForwardAndBackward()
        {
            var row = Row(3);
            Assert.AreEqual(0, row.Index);

            Assert.IsTrue(row.Advance(1));
            Assert.IsTrue(row.Advance(1));
            Assert.AreEqual(2, row.Index);
            Assert.IsTrue(row.Advance(1));
            Assert.AreEqual(0, row.Index, "forward past the end wraps to the start");

            Assert.IsTrue(row.Advance(-1));
            Assert.AreEqual(2, row.Index, "backward past the start wraps to the end");
        }

        [Test]
        public void AdvanceByMoreThanOneLapWrapsAndAFullLapIsANoOp()
        {
            var row = Row(3);
            Assert.IsTrue(row.Advance(7));
            Assert.AreEqual(1, row.Index);
            Assert.IsTrue(row.Advance(-8));
            Assert.AreEqual(2, row.Index);

            var changes = 0;
            row.Changed += () => changes++;
            Assert.IsFalse(row.Advance(3), "a full lap lands where it started");
            Assert.IsFalse(row.Advance(-6));
            Assert.AreEqual(0, changes);
            Assert.AreEqual(2, row.Index);
        }

        [Test]
        public void AOneOptionRowWrapsToItselfWithoutAChange()
        {
            var row = Row(1);
            var changes = 0;
            row.Changed += () => changes++;

            Assert.IsFalse(row.Advance(1));
            Assert.IsFalse(row.Advance(-1));
            Assert.AreEqual(0, row.Index);
            Assert.AreEqual("opt-0", row.Selected.Id.Value);
            Assert.AreEqual(0, changes);
        }

        [Test]
        public void AnEmptyRowIsAQuietNoOp()
        {
            var row = Row(0);
            var changes = 0;
            row.Changed += () => changes++;

            Assert.AreEqual(0, row.Count);
            Assert.IsFalse(row.Advance(1));
            Assert.IsFalse(row.Advance(-3));
            Assert.IsFalse(row.SelectIndex(0));
            Assert.IsFalse(row.Select(new DefinitionId("opt-0")));
            Assert.IsFalse(row.Selected.Id.IsValid, "the default option");
            Assert.AreEqual(0, changes);
        }

        [Test]
        public void SelectFindsAKnownIdAndIgnoresAnUnknownOne()
        {
            var row = Row(4);
            Assert.IsTrue(row.Select(new DefinitionId("opt-2")));
            Assert.AreEqual(2, row.Index);

            Assert.IsFalse(row.Select(new DefinitionId("opt-9")), "unknown id");
            Assert.IsFalse(row.SelectIndex(9), "out of range");
            Assert.IsFalse(row.SelectIndex(-1));
            Assert.AreEqual(2, row.Index, "an ignored selection leaves the row alone");
        }

        [Test]
        public void ChangedFiresOncePerRealChangeAndNotOnANoOp()
        {
            var row = Row(3);
            var changes = 0;
            row.Changed += () => changes++;

            row.Advance(1);
            Assert.AreEqual(1, changes);
            row.Select(new DefinitionId("opt-1")); // already selected
            row.SelectIndex(1);
            Assert.AreEqual(1, changes, "re-selecting the current option is not a change");
            row.Select(new DefinitionId("opt-2"));
            Assert.AreEqual(2, changes);
        }
    }
}
