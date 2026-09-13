using System;
using CRPG.Dialogue;
using CRPG.Inventory;
using CRPG.UI;
using UnityEngine;
using UnityEngine.UIElements;

namespace CRPG.Gameplay
{
    /// <summary>
    /// Task 3.4 (Production Foundation): owns one conversation's full lifecycle -- the single DialogueRunner
    /// instance (constructed once, never restarted), the modal pointer-capture lease, and swapping between the
    /// DialogueView and StoreView presentation. Lives in CRPG.Gameplay alongside InteractionController/
    /// PlayerCommandRouter -- the layer that composes Dialogue/UI/Inventory rather than any one of them owning
    /// the others. Replaces what TalkInteractionAdapter used to hand-roll inline.
    /// <para>
    /// Store hand-off is a genuine round-trip: opening the store pauses the conversation; closing it resumes
    /// the SAME DialogueRunner wherever it already left off (not necessarily where the store was entered --
    /// DialogueRunner may have auto-settled further while executing the OpenStoreAction). AdvancePresentation
    /// is re-entered after every driving call and after the store closes, so this works for any graph shape
    /// without special-casing "store immediately followed by End" versus "more dialogue follows the shop."
    /// </para>
    /// </summary>
    public sealed class ConversationController : IDisposable
    {
        private readonly DialogueContext context;
        private readonly DialogueRunner runner;
        private readonly PanelSettings panelSettings;

        private GameObject uiGo;
        private DialogueView dialogueView;
        private StoreView storeView;
        private IDisposable modalScope;
        private bool ended;

        /// <summary>The conversation reached its End node with no further store hand-off pending.</summary>
        public event Action Completed;

        /// <summary>Cancelled from outside (e.g. the interaction was superseded) -- never fires alongside Completed.</summary>
        public event Action Cancelled;

        /// <summary>Diagnostics -- whichever view is currently shown, if any (mirrors PlayableCharacterAnimationDriver's
        /// ActivePort/GetPortClip precedent: exposed specifically so tests can assert against the real live view
        /// instead of reflection or fragile UI event simulation).</summary>
        public DialogueView CurrentDialogueView => dialogueView;
        public StoreView CurrentStoreView => storeView;

        public ConversationController(DialogueDefinition dialogue, DialogueContext context, PanelSettings panelSettings)
        {
            this.context = context;
            this.panelSettings = panelSettings;
            runner = new DialogueRunner(dialogue, context);
            modalScope = UIToolkitPointerCapture.BeginModal();
            AdvancePresentation();
        }

        public void Cancel()
        {
            if (ended) return;
            ended = true;
            TearDownCurrentView();
            modalScope.Dispose();
            Cancelled?.Invoke();
        }

        public void Dispose() => Cancel();

        private void AdvancePresentation()
        {
            if (ended) return;

            if (context.ConsumeStoreOpenRequest())
            {
                OpenStoreView();
                return;
            }

            if (runner.IsComplete)
            {
                ended = true;
                TearDownCurrentView();
                modalScope.Dispose();
                Completed?.Invoke();
                return;
            }

            OpenDialogueView();
        }

        private void EnsureUiGameObject()
        {
            if (uiGo != null) return;
            uiGo = new GameObject("ConversationUI", typeof(UIDocument));
            uiGo.GetComponent<UIDocument>().panelSettings = panelSettings;
        }

        private void TearDownCurrentView()
        {
            if (dialogueView != null)
            {
                dialogueView.Completed -= HandleDialogueViewCompleted;
                dialogueView = null;
            }

            if (storeView != null)
            {
                storeView.Closed -= HandleStoreClosed;
                storeView = null;
            }

            if (uiGo != null)
            {
                UnityEngine.Object.Destroy(uiGo);
                uiGo = null;
            }
        }

        private void OpenDialogueView()
        {
            TearDownCurrentView();
            EnsureUiGameObject();

            dialogueView = uiGo.AddComponent<DialogueView>();
            dialogueView.Completed += HandleDialogueViewCompleted;
            dialogueView.Initialize(new DialoguePresenter(runner, context));
        }

        private void HandleDialogueViewCompleted() => AdvancePresentation();

        private void OpenStoreView()
        {
            TearDownCurrentView();
            EnsureUiGameObject();

            var storeService = new StoreService(context.RulesDatabase, context.PartyInventory, context.CurrencyItemId);
            storeView = uiGo.AddComponent<StoreView>();
            storeView.Closed += HandleStoreClosed;
            storeView.Initialize(new StorePresenter(context.Store, context.PartyInventory, context.RulesDatabase, storeService));
        }

        private void HandleStoreClosed() => AdvancePresentation();
    }
}
