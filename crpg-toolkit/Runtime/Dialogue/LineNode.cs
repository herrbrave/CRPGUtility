using System;
using UnityEngine;

namespace CRPG.Dialogue
{
    /// <summary>A line of dialogue text shown to the player, who then advances to the next node. `speakerName`
    /// is free-form authored text (e.g. "Grimjaw", "You") rather than resolved from a RuntimeEntityId, since
    /// CharacterState doesn't carry a display name -- dialogue is normally authored with names typed directly.</summary>
    [Serializable]
    public sealed class LineNode : DialogueNode
    {
        [SerializeField] private string speakerName;
        [SerializeField, TextArea] private string text;
        [SerializeField] private string nextNodeId;

        public string SpeakerName => speakerName;
        public string Text => text;
        public string NextNodeId => nextNodeId;
    }
}
