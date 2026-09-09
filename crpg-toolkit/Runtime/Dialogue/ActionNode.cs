using System;
using UnityEngine;

namespace CRPG.Dialogue
{
    /// <summary>A silent node: DialogueRunner executes Action and moves straight to NextNodeId with no
    /// player interaction, as part of its automatic "settle" pass.</summary>
    [Serializable]
    public sealed class ActionNode : DialogueNode
    {
        [SerializeReference] private DialogueAction action;
        [SerializeField] private string nextNodeId;

        public DialogueAction Action => action;
        public string NextNodeId => nextNodeId;
    }
}
