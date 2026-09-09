using System;
using UnityEngine;

namespace CRPG.Dialogue
{
    /// <summary>
    /// One node in a conversation graph. Nodes reference each other by a stable string id rather than a
    /// direct C# object reference, the same way ChoiceRequirement/DefinitionId resolution works elsewhere in
    /// the toolkit -- it keeps branches/loops trivial to author and sidesteps any question of whether Unity's
    /// polymorphic [SerializeReference] serialization handles reference cycles between nodes.
    /// </summary>
    [Serializable]
    public abstract class DialogueNode
    {
        [SerializeField] private string nodeId;

        /// <summary>Layout-only: where this node sits on the graph editor's canvas. Never read by DialogueRunner
        /// or anything else at runtime -- purely so the graph editor can remember node positions between
        /// sessions instead of re-laying-out the graph every time it's opened.</summary>
        [SerializeField] private Vector2 editorPosition;

        public string NodeId => nodeId;
        public Vector2 EditorPosition => editorPosition;
    }
}
