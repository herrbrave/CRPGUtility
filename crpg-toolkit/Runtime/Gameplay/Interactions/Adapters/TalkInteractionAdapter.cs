using System;
using CRPG.Actors;
using CRPG.Core;
using CRPG.Dialogue;
using CRPG.Inventory;
using CRPG.Party;
using CRPG.Rules;
using UnityEngine;
using UnityEngine.UIElements;

namespace CRPG.Gameplay.Interactions.Adapters
{
    /// <summary>Reference "talk" adapter -- delegates the whole conversation lifecycle (DialogueRunner identity,
    /// modal input capture, cancellation, and store hand-off) to ConversationController (Task 3.4), which
    /// replaces what this adapter used to hand-roll inline. Runtime dependencies are public settable properties
    /// wired by whichever scene composition root builds this fixture, matching the project's "no Manager.Instance"
    /// convention.</summary>
    [RequireComponent(typeof(WorldInteractable))]
    public sealed class TalkInteractionAdapter : MonoBehaviour, IInteractionAdapter
    {
        [SerializeField] private DialogueDefinition dialogue;
        [SerializeField] private PanelSettings panelSettings;

        public PartyState Party { get; set; }
        public ICharacterRegistry Characters { get; set; }
        public IRulesDatabase RulesDatabase { get; set; }
        public ITimeline Timeline { get; set; }
        public PartyInventoryState Inventory { get; set; }
        public IDiceService DiceService { get; set; }
        public StoreInventoryState Store { get; set; }
        public DefinitionId CurrencyItemId { get; set; }

        /// <summary>Task 4.1: null is valid for a dialogue that never reaches a CheckNode; whichever composition
        /// root wires this adapter's other session dependencies is expected to also supply this when its content
        /// might include a check (see DialogueContext.SkillCheckRule's own remarks on why there's no fallback).</summary>
        public ISkillCheckRule SkillCheckRule { get; set; }

        /// <summary>Task 6.0: whatever this conversation's Long Rest should actually run against -- null for an
        /// NPC whose dialogue never reaches a RequestLongRestAction, matching SkillCheckRule's own "supplied only
        /// when the content might need it" convention above.</summary>
        public RestService RestService { get; set; }

        /// <summary>Task 6.0: fire-and-forget hook for RequestSaveAction -- see DialogueContext.RequestSave's own
        /// remarks on why this is a plain delegate, not a typed save port.</summary>
        public Action RequestSave { get; set; }

        public DefinitionId ActionId { get; } = new("interact-talk");

        private bool conversationOpen;

        public bool IsAvailable(RuntimeEntityId performer) => dialogue != null && !conversationOpen;

        public IInteractionExecution BeginExecute(RuntimeEntityId performer, Action<InteractionExecutionResult> onFinished)
        {
            conversationOpen = true;

            var context = new DialogueContext(performer, Party, Characters, RulesDatabase, Timeline, Inventory, DiceService, SkillCheckRule,
                store: Store, currencyItemId: CurrencyItemId, restService: RestService, requestSave: RequestSave);
            var controller = new ConversationController(dialogue, context, panelSettings);

            return new Execution(this, controller, onFinished);
        }

        private sealed class Execution : IInteractionExecution
        {
            private readonly TalkInteractionAdapter owner;
            private readonly ConversationController controller;
            private readonly Action<InteractionExecutionResult> onFinished;
            private bool ended;

            public Execution(TalkInteractionAdapter owner, ConversationController controller, Action<InteractionExecutionResult> onFinished)
            {
                this.owner = owner;
                this.controller = controller;
                this.onFinished = onFinished;
                controller.Completed += HandleCompleted;
            }

            private void HandleCompleted()
            {
                if (ended) return;
                ended = true;

                controller.Completed -= HandleCompleted;
                owner.conversationOpen = false;

                onFinished(InteractionExecutionResult.Succeeded);
            }

            public void Cancel()
            {
                if (ended) return;
                ended = true;

                controller.Completed -= HandleCompleted;
                owner.conversationOpen = false;
                controller.Cancel();

                // Deliberately does NOT invoke onFinished -- the controller already knows this request is
                // ending (that's why Cancel was called) and isn't expecting a result from it anymore.
            }
        }
    }
}
