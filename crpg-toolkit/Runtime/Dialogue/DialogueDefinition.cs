using System.Collections.Generic;
using CRPG.Core;
using UnityEngine;

namespace CRPG.Dialogue
{
    [CreateAssetMenu(menuName = "CRPG Toolkit/Dialogue/Dialogue Definition")]
    public sealed class DialogueDefinition : DefinitionAsset
    {
        [SerializeField] private string displayName;
        [SerializeField] private string startNodeId;
        [SerializeReference] private List<DialogueNode> nodes = new();

        public string DisplayName => displayName;
        public string StartNodeId => startNodeId;
        public IReadOnlyList<DialogueNode> Nodes => nodes;
    }
}
