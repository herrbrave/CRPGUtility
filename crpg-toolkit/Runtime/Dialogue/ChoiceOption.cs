using System;
using CRPG.Core;
using UnityEngine;

namespace CRPG.Dialogue
{
    /// <summary>One player-selectable response on a ChoiceNode. Visibility is optionally gated by the same
    /// Timeline condition BranchNode uses -- e.g. only offer "Tell them about the letter" once "read-letter"
    /// has occurred. A gated option with hasVisibilityCondition false is always shown, matching the prior
    /// always-visible behavior.</summary>
    [Serializable]
    public sealed class ChoiceOption
    {
        [SerializeField] private string text;
        [SerializeField] private SerializableGuid nextNodeId;
        [SerializeField] private bool hasVisibilityCondition;
        [SerializeField] private string timelineKey;
        [SerializeField] private string secondTimelineKey;
        [SerializeField] private TimelineConditionType conditionType;
        [SerializeField] private double threshold;

        public string Text => text;
        public SerializableGuid NextNodeId => nextNodeId;

        public bool IsVisible(ITimeline timeline) =>
            !hasVisibilityCondition || TimelineCondition.Evaluate(timeline, conditionType, timelineKey, secondTimelineKey, threshold);
    }
}
