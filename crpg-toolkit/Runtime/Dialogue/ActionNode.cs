using System;
using CRPG.Core;
using UnityEngine;

namespace CRPG.Dialogue
{
    /// <summary>A silent node: DialogueRunner executes Action and moves straight to NextNodeId with no
    /// player interaction, as part of its automatic "settle" pass.</summary>
    [Serializable]
    public sealed class ActionNode : DialogueNode
    {
        [SerializeReference] private DialogueAction action;
        [SerializeField] private SerializableGuid nextNodeId;

        public DialogueAction Action => action;
        public SerializableGuid NextNodeId => nextNodeId;
    }
}
