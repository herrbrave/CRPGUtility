using UnityEditor;

namespace CRPG.Editor.Dialogue
{
    public sealed class CheckNodeView : DialogueNodeView
    {
        public CheckNodeView(SerializedProperty nodeProperty, DialogueGraphView owner) : base(nodeProperty, "Check", owner)
        {
            AddField("queryKey", "Query Key");
            AddField("difficultyClass", "DC");
            AddField("performer", "Performer");
            AddOutputPort("Success", "successNodeId");
            AddOutputPort("Failure", "failureNodeId");

            RefreshExpandedState();
            RefreshPorts();
        }
    }
}
