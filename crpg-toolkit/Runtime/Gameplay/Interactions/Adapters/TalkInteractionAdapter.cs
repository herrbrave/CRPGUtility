using System;
using CRPG.Actors;
using CRPG.Core;
using CRPG.Dialogue;
using CRPG.Inventory;
using CRPG.Party;
using CRPG.Rules;
using CRPG.UI;
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

        /// <summary>The dialogue to run. The serialized field is the authoring path; this is for composition roots that attach the adapter in code (see NpcDialogues).</summary>
        public DialogueDefinition Dialogue { get => dialogue; set => dialogue = value; }

        /// <summary>The panel settings the conversation's UIDocument uses. Same dual role as <see cref="Dialogue"/>.</summary>
        public PanelSettings PanelSettings { get => panelSettings; set => panelSettings = value; }

        /// <summary>True shows the conversation in the self-contained dark panel (a game with no USS skin); false keeps the default look a skin paints over.</summary>
        public bool PlainStyle { get; set; }

        /// <summary>Optional game-owned presentation, created for the actual talking performer. Null uses
        /// the toolkit DialogueView. Each call transfers ownership of a fresh view to the controller.</summary>
        public Func<RuntimeEntityId, IConversationView> ViewFactory { get; set; }

        /// <summary>A conversation began: the performer who is talking. Fires before the first line is shown.</summary>
        public event Action<RuntimeEntityId> ConversationStarted;

        /// <summary>A conversation ended, however it ended (completed, cancelled by a superseding order or Escape, scene unload, or it failed to start). Never fires without a matching <see cref="ConversationStarted"/>.</summary>
        public event Action<RuntimeEntityId> ConversationEnded;

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

        /// <summary>Task 6.1: fire-and-forget hook for RecruitToPartyAction -- see DialogueContext.RequestRecruit's
        /// own remarks on why this is keyed by a companion key, not DialogueContext.NpcId.</summary>
        public Action<string> RequestRecruit { get; set; }

        /// <summary>Task 6.1: fire-and-forget hook for StartQuestAction -- see DialogueContext.RequestStartQuest's
        /// own remarks on why this stays a plain delegate rather than a QuestService reference.</summary>
        public Action<string> RequestStartQuest { get; set; }

        public DefinitionId ActionId { get; } = new("interact-talk");

        private bool conversationOpen;
        private Execution activeExecution;
        private bool startupDisablePending;

        private void OnDisable()
        {
            if (activeExecution != null)
            {
                activeExecution.TerminateFromOwner();
            }
            else if (conversationOpen)
            {
                // Startup has claimed this conversation but has not installed its execution yet.
                startupDisablePending = true;
            }
        }

        public bool IsAvailable(RuntimeEntityId performer) => dialogue != null && !conversationOpen;

        public IInteractionExecution BeginExecute(RuntimeEntityId performer, Action<InteractionExecutionResult> onFinished)
        {
            var context = new DialogueContext(performer, Party, Characters, RulesDatabase, Timeline, Inventory, DiceService, SkillCheckRule,
                store: Store, currencyItemId: CurrencyItemId, restService: RestService, requestSave: RequestSave,
                requestRecruit: RequestRecruit, requestStartQuest: RequestStartQuest);

            // Claim before the start notification, so composition callbacks see this NPC as occupied.
            // A startup failure resets admission and the controller releases its own modal scope.
            conversationOpen = true;
            startupDisablePending = false;
            ConversationController controller;
            try
            {
                ConversationStarted?.Invoke(performer);
                var factory = ViewFactory;
                controller = new ConversationController(dialogue, context, panelSettings, PlainStyle,
                    viewFactory: factory == null ? null : () => factory(performer));
            }
            catch
            {
                conversationOpen = false;
                startupDisablePending = false;
                ConversationEnded?.Invoke(performer);
                throw;
            }

            bool cancelOnStart = startupDisablePending;
            startupDisablePending = false;
            return new Execution(this, controller, performer, onFinished, cancelOnStart);
        }

        private sealed class Execution : IInteractionExecution
        {
            private readonly TalkInteractionAdapter owner;
            private readonly ConversationController controller;
            private readonly RuntimeEntityId performer;
            private readonly Action<InteractionExecutionResult> onFinished;
            private bool ended;

            public Execution(TalkInteractionAdapter owner, ConversationController controller, RuntimeEntityId performer, Action<InteractionExecutionResult> onFinished, bool cancelOnStart)
            {
                this.owner = owner;
                this.controller = controller;
                this.performer = performer;
                this.onFinished = onFinished;
                owner.activeExecution = this;
                controller.Completed += HandleCompleted;
                controller.Cancelled += HandleCancelled;
                // End-on-start and a custom view completing inside Initialize both happen before we can
                // subscribe. Observe the terminal state after subscribing instead of deferring events.
                if (controller.IsEnded)
                {
                    HandleCompleted();
                }
                else if (cancelOnStart)
                {
                    // Native disable must finish the waiting interaction before this token escapes.
                    TerminateFromOwner();
                }
            }

            private void HandleCompleted() => Finish(InteractionExecutionResult.Succeeded);

            private void HandleCancelled() => Finish(InteractionExecutionResult.Failed);

            // Owner disable is not controller-requested execution cancellation: keep the completion
            // subscription so Cancelled -> Finish(Failed) notifies the waiting interaction exactly once.
            public void TerminateFromOwner() => controller.Cancel();

            private void Finish(InteractionExecutionResult result)
            {
                if (ended) return;
                ended = true;
                Detach();
                try
                {
                    owner.ConversationEnded?.Invoke(performer);
                }
                finally
                {
                    onFinished(result);
                }
            }

            private void Detach()
            {
                controller.Completed -= HandleCompleted;
                controller.Cancelled -= HandleCancelled;
                owner.activeExecution = null;
                owner.conversationOpen = false;
            }

            public void Cancel()
            {
                if (ended) return;
                ended = true;

                Detach();
                try
                {
                    controller.Cancel();
                }
                finally
                {
                    owner.ConversationEnded?.Invoke(performer);
                }

                // Deliberately does NOT invoke onFinished -- the controller already knows this request is
                // ending (that's why Cancel was called) and isn't expecting a result from it anymore.
            }
        }
    }
}
