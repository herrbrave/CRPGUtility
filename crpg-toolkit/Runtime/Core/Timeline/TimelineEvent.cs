using System;

namespace CRPG.Core
{
    /// <summary>One recorded occurrence in a Timeline. Sequence is what ordering queries (OccurredBefore/
    /// OccurredAfter) compare -- assigned by Timeline itself, strictly increasing.</summary>
    public sealed class TimelineEvent
    {
        public TimelineEvent(string key, int sequence, TimelineValue value, double amount, RuntimeEntityId source)
            : this(key, sequence, value, amount, source, DateTime.UtcNow)
        {
        }

        /// <summary>For save/load only, to restore an event's original Timestamp rather than stamping "now" --
        /// see Timeline.LoadEvent.</summary>
        public TimelineEvent(string key, int sequence, TimelineValue value, double amount, RuntimeEntityId source, DateTime timestamp)
        {
            Key = key;
            Sequence = sequence;
            Value = value;
            Amount = amount;
            Source = source;
            Timestamp = timestamp;
        }

        public string Key { get; }
        public int Sequence { get; }
        public TimelineValue Value { get; }

        /// <summary>Defaults to 1 (see Timeline.Record) so one mechanism covers both "did X happen"
        /// (CountOccurrences) and "how much X accumulated" (Sum) -- a reputation delta, e.g.</summary>
        public double Amount { get; }

        public RuntimeEntityId Source { get; }
        public DateTime Timestamp { get; }
    }
}
