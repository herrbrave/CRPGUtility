using System;
using CRPG.Core;
using CRPG.Gameplay;
using CRPG.Gameplay.Interactions;
using UnityEngine;

namespace BarrowsEnd.Gate
{
    /// <summary>A pull-once lever. Pulling records a Timeline key (what a <see cref="PortcullisGate"/> and a quest's Interact
    /// objective both read), so it is idempotent across saves, loads and travel. The visual is flipped once pulled and the
    /// adapter stops being available.</summary>
    [RequireComponent(typeof(WorldInteractable))]
    public sealed class LeverInteractionAdapter : MonoBehaviour, IInteractionAdapter
    {
        [SerializeField] private string leverKey = "deep-lever-pulled";
        [SerializeField] private Transform visual;

        public GameSession Session { get; set; }
        public Action RequestQuestRefresh { get; set; }
        public Action<string> Notify { get; set; }

        /// <summary>Raised after the key is recorded so the shell can re-derive every gate's state.</summary>
        public Action OnPulled { get; set; }

        public string LeverKey => leverKey;

        public DefinitionId ActionId { get; } = new("interact-lever");

        public bool IsAvailable(RuntimeEntityId performer) => Session != null && !Session.Timeline.HasOccurred(leverKey);

        public IInteractionExecution BeginExecute(RuntimeEntityId performer, Action<InteractionExecutionResult> onFinished)
        {
            if (!IsAvailable(performer))
            {
                onFinished(InteractionExecutionResult.Failed);
                return null;
            }

            Session.Timeline.Record(leverKey, TimelineValue.Flag);
            ShowPulled(true);
            Notify?.Invoke("You pull the lever. Somewhere deeper, iron grinds open.");
            onFinished(InteractionExecutionResult.Succeeded);
            OnPulled?.Invoke();
            RequestQuestRefresh?.Invoke();
            return null;
        }

        /// <summary>Re-derives the visual from the persisted key; called whenever a session is wired.</summary>
        public void RefreshState()
        {
            if (Session != null) ShowPulled(Session.Timeline.HasOccurred(leverKey));
        }

        private void ShowPulled(bool pulled)
        {
            if (visual == null) return;
            var scale = visual.localScale;
            scale.x = Mathf.Abs(scale.x) * (pulled ? -1f : 1f);
            visual.localScale = scale;
        }
    }
}
