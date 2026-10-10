using System;
using UnityEngine;

namespace CRPG.Dialogue
{
    /// <summary>Task 6.1: triggers whatever <see cref="DialogueContext.RequestStartQuest"/> delegate this
    /// conversation was wired up with, passing this action's own authored <see cref="questKey"/> -- a plain
    /// string id, not a <c>QuestDefinition</c> asset reference, since resolving and starting the quest is left to
    /// whatever built this context (see <see cref="DialogueContext.RequestStartQuest"/>'s own doc comment for why
    /// this stays a delegate rather than a direct <c>QuestService</c> reference). Fire-and-forget, matching
    /// <see cref="RequestSaveAction"/>'s own shape. A no-op if <see cref="DialogueContext.RequestStartQuest"/> is
    /// null (an NPC whose dialogue was never meant to reach this).</summary>
    [Serializable]
    public sealed class StartQuestAction : DialogueAction
    {
        [SerializeField] private string questKey;

        /// <summary>The authored quest key: the quest's <c>DefinitionId</c>.</summary>
        public string QuestKey => questKey;

        public override void Execute(DialogueContext context) => context.RequestStartQuest?.Invoke(questKey);
    }
}
