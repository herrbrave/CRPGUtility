using System;
using CRPG.Core;

namespace CRPG.Saving
{
    /// <summary>Flat mirror of one TimelineEvent (Kind/Number/Text/EntityId cover TimelineValue's four kinds --
    /// unused fields for a given Kind just stay at their default). Events must be restored in ascending
    /// Sequence order for Timeline.LoadEvent to reproduce the original history correctly.</summary>
    [Serializable]
    public sealed class TimelineEventSaveData
    {
        public string Key;
        public int Sequence;
        public TimelineValueKind ValueKind;
        public double ValueNumber;
        public string ValueText;
        public string ValueEntityId;
        public double Amount;
        public string Source;
        public string TimestampUtc;
    }
}
