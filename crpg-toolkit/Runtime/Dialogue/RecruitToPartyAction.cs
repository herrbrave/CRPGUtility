using System;
using UnityEngine;

namespace CRPG.Dialogue
{
    /// <summary>Task 6.1: triggers whatever <see cref="DialogueContext.RequestRecruit"/> delegate this
    /// conversation was wired up with, passing this action's own authored <see cref="companionKey"/> -- NOT
    /// <see cref="DialogueContext.NpcId"/>, which is the PARTY MEMBER doing the talking, not the NPC being
    /// recruited (a real design error caught by review: <c>context.Party.AddMember(context.NpcId)</c> would have
    /// silently re-added whoever's already in the party). Fire-and-forget, matching <see cref="RequestSaveAction"/>'s
    /// own "acts on whatever this NPC was wired up with" shape. A no-op if <see cref="DialogueContext.RequestRecruit"/>
    /// is null (an NPC whose dialogue was never meant to reach this).</summary>
    [Serializable]
    public sealed class RecruitToPartyAction : DialogueAction
    {
        [SerializeField] private string companionKey;

        public override void Execute(DialogueContext context) => context.RequestRecruit?.Invoke(companionKey);
    }
}
