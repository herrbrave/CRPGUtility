using System;

namespace CRPG.Dialogue
{
    /// <summary>A DialogueDefinition's authored graph is malformed in a way that would otherwise hang or crash
    /// the runtime -- a dangling node reference, a duplicate node id, or an auto-settle loop (Branch/Action
    /// nodes only) that never reaches a player-facing pause point. DialogueValidator catches these at author
    /// time; this is the runtime's own defense in depth for content that slipped through un-validated.</summary>
    public sealed class DialogueGraphException : Exception
    {
        public DialogueGraphException(string message) : base(message) { }
    }
}
