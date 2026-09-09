using System;

namespace CRPG.Dialogue
{
    /// <summary>Signals that this is the moment to show the conversation's Store screen. Needs no authored
    /// fields -- it opens whatever DialogueContext.Store this NPC was wired up with, the same way GiveItemAction
    /// always targets DialogueContext.PartyInventory rather than referencing an inventory of its own.</summary>
    [Serializable]
    public sealed class OpenStoreAction : DialogueAction
    {
        public override void Execute(DialogueContext context) => context.RequestOpenStore();
    }
}
