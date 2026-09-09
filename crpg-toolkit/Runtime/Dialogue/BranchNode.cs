using System;
using CRPG.Core;
using UnityEngine;

namespace CRPG.Dialogue
{
    /// <summary>Silent, automatic routing based on a Timeline query -- no player interaction, DialogueRunner
    /// resolves it and moves straight to TrueNodeId/FalseNodeId. This is the direct payoff of Timeline's query
    /// surface: "did the player betray the guild," "has the party bribed this guard 3+ times," "did the
    /// coronation happen before the betrayal" all become one BranchNode each.</summary>
    [Serializable]
    public sealed class BranchNode : DialogueNode
    {
        [SerializeField] private string timelineKey;
        [SerializeField] private string secondTimelineKey;
        [SerializeField] private BranchConditionType conditionType;
        [SerializeField] private double threshold;
        [SerializeField] private string trueNodeId;
        [SerializeField] private string falseNodeId;

        public string TrueNodeId => trueNodeId;
        public string FalseNodeId => falseNodeId;

        public bool Evaluate(ITimeline timeline) =>
            TimelineConditionEvaluator.Evaluate(timeline, conditionType, timelineKey, secondTimelineKey, threshold);
    }
}
