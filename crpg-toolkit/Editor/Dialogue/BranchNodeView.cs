using UnityEditor;

namespace CRPG.Editor.Dialogue
{
    public sealed class BranchNodeView : DialogueNodeView
    {
        public BranchNodeView(SerializedProperty nodeProperty, DialogueGraphView owner) : base(nodeProperty, "Branch", owner)
        {
            AddField("timelineKey", "Timeline Key");
            AddField("secondTimelineKey", "Second Key (OccurredBefore)");
            AddField("conditionType", "Condition");
            AddField("threshold", "Threshold");
            AddOutputPort("True", "trueNodeId");
            AddOutputPort("False", "falseNodeId");

            RefreshExpandedState();
            RefreshPorts();
        }
    }
}
