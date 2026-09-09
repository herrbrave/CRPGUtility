using UnityEditor;

namespace CRPG.Editor.Dialogue
{
    public sealed class LineNodeView : DialogueNodeView
    {
        public LineNodeView(SerializedProperty nodeProperty, DialogueGraphView owner) : base(nodeProperty, "Line", owner)
        {
            AddField("speakerName", "Speaker");
            AddField("text", "Text");
            AddOutputPort("Next", "nextNodeId");

            RefreshExpandedState();
            RefreshPorts();
        }
    }
}
