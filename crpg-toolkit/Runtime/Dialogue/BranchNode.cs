using System;
using CRPG.Core;
using CRPG.Inventory;
using UnityEngine;

namespace CRPG.Dialogue
{
    /// <summary>Silent, automatic routing based on a Timeline query -- no player interaction, DialogueRunner
    /// resolves it and moves straight to TrueNodeId/FalseNodeId. This is the direct payoff of Timeline's query
    /// surface: "did the player betray the guild," "has the party bribed this guard 3+ times," "did the
    /// coronation happen before the betrayal" all become one BranchNode each.</summary>
    /// <summary>What a <see cref="BranchNode"/> asks. Separate from <see cref="TimelineConditionType"/> on purpose: that enum lives in Core and is shared with choice visibility and quest objectives, which must not learn about inventory.</summary>
    public enum BranchSource
    {
        /// <summary>A query on the Timeline (the original and default behaviour).</summary>
        Timeline = 0,

        /// <summary>True when the party carries at least <c>quantity</c> of <c>item</c> in its shared inventory (an inn: "if you have 5 gold ...").</summary>
        PartyItemCount = 1,
    }

    [Serializable]
    public sealed class BranchNode : DialogueNode
    {
        [SerializeField] private BranchSource source;
        [SerializeField] private ItemDefinition item;
        [SerializeField] private int quantity = 1;
        [SerializeField] private string timelineKey;
        [SerializeField] private string secondTimelineKey;
        [SerializeField] private TimelineConditionType conditionType;
        [SerializeField] private double threshold;
        [SerializeField] private SerializableGuid trueNodeId;
        [SerializeField] private SerializableGuid falseNodeId;

        public BranchSource Source => source;
        public ItemDefinition Item => item;
        public int Quantity => quantity;

        public SerializableGuid TrueNodeId => trueNodeId;
        public SerializableGuid FalseNodeId => falseNodeId;

        public bool Evaluate(DialogueContext context)
        {
            if (source == BranchSource.PartyItemCount)
                return item != null && context.PartyInventory != null && context.PartyInventory.CountShared(item.Id) >= quantity;

            return TimelineCondition.Evaluate(context.Timeline, conditionType, timelineKey, secondTimelineKey, threshold);
        }
    }
}
