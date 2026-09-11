using System;
using CRPG.Core;
using UnityEngine;

namespace CRPG.Dialogue
{
    /// <summary>
    /// One node in a conversation graph. Nodes reference each other by a stable SerializableGuid id rather than
    /// a direct C# object reference -- it keeps branches/loops trivial to author and sidesteps any question of
    /// whether Unity's polymorphic [SerializeReference] serialization handles reference cycles between nodes.
    /// A GUID rather than a free-form string (this used to be one): comparing two SerializableGuids is a fixed-
    /// cost struct comparison, not a length-dependent character-by-character string compare, which matters for
    /// an id compared as often as a node link is walked during a conversation. Generated once when a node is
    /// created (the graph editor's "Create Node" does this); never reassigned afterward except deliberately via
    /// the Inspector's "Regenerate" button (see SerializableGuidDrawer), since anything already pointing at
    /// this node by id would otherwise silently break.
    /// </summary>
    [Serializable]
    public abstract class DialogueNode
    {
        [SerializeField] private SerializableGuid nodeId;

        /// <summary>Layout-only: where this node sits on the graph editor's canvas. Never read by DialogueRunner
        /// or anything else at runtime -- purely so the graph editor can remember node positions between
        /// sessions instead of re-laying-out the graph every time it's opened.</summary>
        [SerializeField] private Vector2 editorPosition;

        public SerializableGuid NodeId => nodeId;
        public Vector2 EditorPosition => editorPosition;
    }
}
