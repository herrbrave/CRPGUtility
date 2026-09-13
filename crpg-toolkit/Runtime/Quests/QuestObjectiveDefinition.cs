using System;
using CRPG.Core;
using UnityEngine;

namespace CRPG.Quests
{
    /// <summary>One thing to do as part of a quest -- e.g. "Kill 5 Wolves" or "Talk to the Elder". Completion
    /// is always one of QuestObjectiveProgressMode's three generic checks, so authoring one is just picking a
    /// TrackingKey plus, for a count mode, a TargetCount, or for CurrentStatePredicate, a TimelineCondition
    /// (ConditionType/SecondTimelineKey/Threshold -- same fields BranchNode has, always present but only
    /// meaningful in that mode).</summary>
    [Serializable]
    public sealed class QuestObjectiveDefinition
    {
        [SerializeField] private string id;
        [SerializeField] private string displayName;
        [SerializeField] private QuestObjectiveType type;
        [SerializeField] private string trackingKey;
        [SerializeField] private int targetCount = 1;
        [SerializeField] private QuestObjectiveProgressMode progressMode = QuestObjectiveProgressMode.HistoricalCount;
        [SerializeField] private TimelineConditionType conditionType;
        [SerializeField] private string secondTimelineKey;
        [SerializeField] private double threshold;

        /// <summary>Stable key used to match runtime progress back to this objective.</summary>
        public string Id => id;
        public string DisplayName => displayName;
        public QuestObjectiveType Type => type;

        /// <summary>The Timeline key whose occurrence count (or, in CurrentStatePredicate mode, condition) this
        /// objective watches.</summary>
        public string TrackingKey => trackingKey;
        public int TargetCount => targetCount;

        /// <summary>Defaults to HistoricalCount -- every existing/migrated asset keeps today's exact behavior
        /// until deliberately re-authored to one of the other modes.</summary>
        public QuestObjectiveProgressMode ProgressMode => progressMode;

        /// <summary>Only meaningful when ProgressMode is CurrentStatePredicate.</summary>
        public TimelineConditionType ConditionType => conditionType;
        public string SecondTimelineKey => secondTimelineKey;
        public double Threshold => threshold;
    }
}
