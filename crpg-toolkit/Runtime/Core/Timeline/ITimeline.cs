using System.Collections.Generic;

namespace CRPG.Core
{
    /// <summary>
    /// The single source of truth for world/story state (spec's WorldState idea, generalized): an ordered,
    /// append-only log of narratively-tagged events, queryable both for "current value" (latest-wins) and
    /// "history" (sum/count/ordering) -- the conditionals a branching dialogue/quest system needs.
    /// </summary>
    public interface ITimeline
    {
        TimelineEvent Record(string key, TimelineValue value, double amount = 1, RuntimeEntityId source = default);

        bool HasOccurred(string key);
        TimelineValue? GetLatest(string key);
        double Sum(string key);
        int CountOccurrences(string key);

        /// <summary>True only if both keys have occurred at least once and keyA's first occurrence precedes
        /// keyB's first occurrence.</summary>
        bool OccurredBefore(string keyA, string keyB);
        bool OccurredAfter(string keyA, string keyB);

        IReadOnlyList<TimelineEvent> AllEvents();
        IReadOnlyList<TimelineEvent> AllEvents(string key);
    }
}
