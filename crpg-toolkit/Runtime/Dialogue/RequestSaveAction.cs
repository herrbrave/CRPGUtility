using System;

namespace CRPG.Dialogue
{
    /// <summary>Task 6.0: triggers whatever <see cref="DialogueContext.RequestSave"/> delegate this conversation
    /// was wired up with -- fire-and-forget, no authored fields, matching <see cref="OpenStoreAction"/>'s own
    /// "acts on whatever this NPC was wired up with" shape. A plain delegate rather than a typed save port: a
    /// consumer's own save entrypoint (single-file, multi-slot, whatever it is) doesn't need to implement any
    /// particular interface just to let dialogue trigger it. A no-op if <see cref="DialogueContext.RequestSave"/>
    /// is null (an NPC whose dialogue was never meant to reach this).</summary>
    [Serializable]
    public sealed class RequestSaveAction : DialogueAction
    {
        public override void Execute(DialogueContext context) => context.RequestSave?.Invoke();
    }
}
