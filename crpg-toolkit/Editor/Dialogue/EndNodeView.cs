using UnityEditor;

namespace CRPG.Editor.Dialogue
{
    public sealed class EndNodeView : DialogueNodeView
    {
        public EndNodeView(SerializedProperty nodeProperty, DialogueGraphView owner) : base(nodeProperty, "End", owner)
        {
            RefreshExpandedState();
            RefreshPorts();
        }
    }
}
