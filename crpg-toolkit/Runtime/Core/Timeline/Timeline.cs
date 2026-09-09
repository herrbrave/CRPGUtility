using System.Collections.Generic;
using System.Linq;

namespace CRPG.Core
{
    public sealed class Timeline : ITimeline
    {
        private readonly List<TimelineEvent> events = new();
        private readonly Dictionary<string, TimelineEvent> latestByKey = new();
        private readonly Dictionary<string, double> sumByKey = new();
        private readonly Dictionary<string, int> countByKey = new();
        private readonly Dictionary<string, int> firstSequenceByKey = new();

        public TimelineEvent Record(string key, TimelineValue value, double amount = 1, RuntimeEntityId source = default)
        {
            var timelineEvent = new TimelineEvent(key, events.Count, value, amount, source);
            AddToProjections(timelineEvent);
            return timelineEvent;
        }

        /// <summary>For save/load only: re-adds a previously-recorded event exactly as it was (same Sequence
        /// and Timestamp, unlike Record which always assigns both fresh) so a restored Timeline's history and
        /// ordering queries are indistinguishable from the original. Events must be loaded in their original
        /// Sequence order.</summary>
        public void LoadEvent(TimelineEvent savedEvent) => AddToProjections(savedEvent);

        private void AddToProjections(TimelineEvent timelineEvent)
        {
            events.Add(timelineEvent);

            var key = timelineEvent.Key;
            latestByKey[key] = timelineEvent;
            sumByKey[key] = sumByKey.TryGetValue(key, out var currentSum) ? currentSum + timelineEvent.Amount : timelineEvent.Amount;
            countByKey[key] = countByKey.TryGetValue(key, out var currentCount) ? currentCount + 1 : 1;
            if (!firstSequenceByKey.ContainsKey(key)) firstSequenceByKey[key] = timelineEvent.Sequence;
        }

        public bool HasOccurred(string key) => countByKey.ContainsKey(key);

        public TimelineValue? GetLatest(string key) => latestByKey.TryGetValue(key, out var e) ? e.Value : null;

        public double Sum(string key) => sumByKey.TryGetValue(key, out var sum) ? sum : 0;

        public int CountOccurrences(string key) => countByKey.TryGetValue(key, out var count) ? count : 0;

        public bool OccurredBefore(string keyA, string keyB)
        {
            if (!firstSequenceByKey.TryGetValue(keyA, out var seqA)) return false;
            if (!firstSequenceByKey.TryGetValue(keyB, out var seqB)) return false;
            return seqA < seqB;
        }

        public bool OccurredAfter(string keyA, string keyB) => OccurredBefore(keyB, keyA);

        public IReadOnlyList<TimelineEvent> AllEvents() => events;

        // Not projected/indexed -- a filtered scan over the full log. Lower-traffic than the scalar queries
        // above (mainly debug/recap tooling); add a per-key index later if profiling says otherwise.
        public IReadOnlyList<TimelineEvent> AllEvents(string key) => events.Where(e => e.Key == key).ToList();
    }
}
