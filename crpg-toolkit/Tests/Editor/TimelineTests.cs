using CRPG.Core;
using NUnit.Framework;

namespace CRPG.Tests.EditMode
{
    public class TimelineTests
    {
        [Test]
        public void RecordAssignsStrictlyIncreasingSequenceNumbers()
        {
            var timeline = new Timeline();

            var first = timeline.Record("bridge-opened", TimelineValue.Flag);
            var second = timeline.Record("spared-captain", TimelineValue.Flag);

            Assert.AreEqual(0, first.Sequence);
            Assert.AreEqual(1, second.Sequence);
        }

        [Test]
        public void HasOccurredIsFalseUntilRecordedThenTrue()
        {
            var timeline = new Timeline();

            Assert.IsFalse(timeline.HasOccurred("bridge-opened"));

            timeline.Record("bridge-opened", TimelineValue.Flag);

            Assert.IsTrue(timeline.HasOccurred("bridge-opened"));
        }

        [Test]
        public void GetLatestReturnsTheMostRecentValueForAKey()
        {
            var timeline = new Timeline();

            timeline.Record("chosen-faction", TimelineValue.OfText("Empire"));
            timeline.Record("chosen-faction", TimelineValue.OfText("Rebellion"));

            var latest = timeline.GetLatest("chosen-faction");

            Assert.IsTrue(latest.HasValue);
            Assert.AreEqual("Rebellion", latest.Value.Text);
        }

        [Test]
        public void GetLatestReturnsNullForAKeyThatNeverOccurred()
        {
            var timeline = new Timeline();

            Assert.IsNull(timeline.GetLatest("never-happened"));
        }

        [Test]
        public void SumAccumulatesAmountsAcrossRecordsOfTheSameKey()
        {
            var timeline = new Timeline();

            timeline.Record("guild-reputation", TimelineValue.OfNumber(5), amount: 5);
            timeline.Record("guild-reputation", TimelineValue.OfNumber(-3), amount: -3);
            timeline.Record("guild-reputation", TimelineValue.OfNumber(10), amount: 10);

            Assert.AreEqual(12, timeline.Sum("guild-reputation"));
        }

        [Test]
        public void CountOccurrencesCountsRecordCallsRegardlessOfAmount()
        {
            var timeline = new Timeline();

            timeline.Record("chose-ruthless-option", TimelineValue.Flag);
            timeline.Record("chose-ruthless-option", TimelineValue.Flag);
            timeline.Record("chose-ruthless-option", TimelineValue.Flag);

            Assert.AreEqual(3, timeline.CountOccurrences("chose-ruthless-option"));
            Assert.AreEqual(3, timeline.Sum("chose-ruthless-option")); // default amount is 1 per call
        }

        [Test]
        public void OccurredBeforeComparesFirstOccurrenceOrder()
        {
            var timeline = new Timeline();
            timeline.Record("betrayal", TimelineValue.Flag);
            timeline.Record("coronation", TimelineValue.Flag);

            Assert.IsTrue(timeline.OccurredBefore("betrayal", "coronation"));
            Assert.IsFalse(timeline.OccurredBefore("coronation", "betrayal"));
            Assert.IsTrue(timeline.OccurredAfter("coronation", "betrayal"));
        }

        [Test]
        public void OccurredBeforeIsFalseWhenEitherKeyNeverOccurred()
        {
            var timeline = new Timeline();
            timeline.Record("betrayal", TimelineValue.Flag);

            Assert.IsFalse(timeline.OccurredBefore("betrayal", "coronation"));
            Assert.IsFalse(timeline.OccurredBefore("coronation", "betrayal"));
        }

        [Test]
        public void OccurredBeforeUsesFirstOccurrenceEvenIfKeyRecordedAgainLater()
        {
            var timeline = new Timeline();
            timeline.Record("betrayal", TimelineValue.Flag); // sequence 0
            timeline.Record("coronation", TimelineValue.Flag); // sequence 1
            timeline.Record("betrayal", TimelineValue.Flag); // sequence 2 -- shouldn't move betrayal's "first" marker

            Assert.IsTrue(timeline.OccurredBefore("betrayal", "coronation"));
        }

        [Test]
        public void AllEventsFiltersToOnlyTheGivenKeyInOrder()
        {
            var timeline = new Timeline();
            timeline.Record("betrayal", TimelineValue.Flag);
            timeline.Record("guild-reputation", TimelineValue.OfNumber(1), amount: 1);
            timeline.Record("guild-reputation", TimelineValue.OfNumber(2), amount: 2);

            var repEvents = timeline.AllEvents("guild-reputation");

            Assert.AreEqual(2, repEvents.Count);
            Assert.AreEqual(1, repEvents[0].Sequence);
            Assert.AreEqual(2, repEvents[1].Sequence);
        }

        [Test]
        public void RecordCapturesSourceAndEntityRefValues()
        {
            var timeline = new Timeline();
            var npc = RuntimeEntityId.NewId();
            var player = RuntimeEntityId.NewId();

            timeline.Record("last-spoke-to", TimelineValue.OfEntity(npc), source: player);

            var latest = timeline.GetLatest("last-spoke-to");
            Assert.AreEqual(npc, latest.Value.Entity);
            Assert.AreEqual(player, timeline.AllEvents()[0].Source);
        }
    }
}
