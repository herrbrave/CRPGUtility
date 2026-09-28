using System;
using CRPG.Core;

namespace CRPG.Dialogue
{
    /// <summary>Task 6.0: triggers a real Long Rest against whatever <see cref="DialogueContext.RestService"/>
    /// this conversation was wired up with -- fire-and-forget, no authored fields, matching
    /// <see cref="OpenStoreAction"/>'s own "acts on whatever this NPC was wired up with" shape. A no-op if
    /// <see cref="DialogueContext.RestService"/> is null (an NPC whose dialogue was never meant to reach this).
    /// <para><b>Does NOT stand up a downed party member</b> -- <see cref="RestService.Rest"/> only restores
    /// resources whose <c>RestTrigger</c> matches; it does not clear an unconscious condition or death-save
    /// state (compare a game's own "heal and stand" glue, if it has one). A Long Rest through this action
    /// restores HP but leaves a knocked-out member exactly as knocked out as before.</para>
    /// </summary>
    [Serializable]
    public sealed class RequestLongRestAction : DialogueAction
    {
        public override void Execute(DialogueContext context) =>
            context.RestService?.Rest(context.Party.Members, GameEvent.LongRest);
    }
}
