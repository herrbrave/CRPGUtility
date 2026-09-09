using System;
using UnityEngine;

namespace CRPG.Quests
{
    /// <summary>One thing to do as part of a quest -- e.g. "Kill 5 Wolves" or "Talk to the Elder". Completion
    /// is always the same generic check (see QuestObjectiveType) so authoring one is just picking a
    /// TrackingKey and, for anything countable, a TargetCount.</summary>
    [Serializable]
    public sealed class QuestObjectiveDefinition
    {
        [SerializeField] private string id;
        [SerializeField] private string displayName;
        [SerializeField] private QuestObjectiveType type;
        [SerializeField] private string trackingKey;
        [SerializeField] private int targetCount = 1;

        /// <summary>Stable key used to match runtime progress back to this objective.</summary>
        public string Id => id;
        public string DisplayName => displayName;
        public QuestObjectiveType Type => type;

        /// <summary>The Timeline key whose occurrence count this objective watches.</summary>
        public string TrackingKey => trackingKey;
        public int TargetCount => targetCount;
    }
}
