using CRPG.Core;
using CRPG.Gameplay.Interactions;
using CRPG.Gameplay.Interactions.Adapters;
using UnityEngine;

namespace BarrowsEnd.Gate
{
    /// <summary>Plays a conversation when a quest completes -- the campaign's ending. It rides on a <see cref="TalkInteractionAdapter"/>
    /// (which <c>ShellBootstrap.WireTalkAdapters</c> already supplies with the live session) on a GameObject with no collider, so it
    /// can never be clicked; the shell calls <see cref="Play"/> from the quest-completed event. Lives in the scene where the quest
    /// ends, and only the ACTIVE scene's epilogue plays.</summary>
    [RequireComponent(typeof(TalkInteractionAdapter))]
    public sealed class EpilogueTrigger : MonoBehaviour
    {
        [SerializeField] private string questId;

        public string QuestId => questId;

        public void Play(RuntimeEntityId performer)
        {
            if (gameObject.scene != UnityEngine.SceneManagement.SceneManager.GetActiveScene()) return;

            var adapter = GetComponent<TalkInteractionAdapter>();
            if (adapter.IsAvailable(performer)) execution = adapter.BeginExecute(performer, _ => execution = null);
        }

        // The conversation owns a modal pointer-capture scope: if this scene goes away mid-epilogue (the player travels, a load
        // replaces the scene) the conversation must end with it, or every later world click stays blocked.
        private void OnDisable()
        {
            execution?.Cancel();
            execution = null;
        }

        private IInteractionExecution execution;
    }
}
