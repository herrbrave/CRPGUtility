using System;
using CRPG.Dialogue;
using CRPG.Inventory;
using CRPG.UI;
using UnityEngine;
using UnityEngine.UIElements;

namespace CRPG.Gameplay
{
    /// <summary>
    /// Owns one retained DialoguePresenter/runner, the modal pointer lease, and dialogue/store presentation.
    /// A store round-trip rebuilds only the view, never the graph or its retained spoken prompt.
    /// </summary>
    public sealed class ConversationController : IDisposable
    {
        private readonly DialogueContext context;
        private readonly PanelSettings panelSettings;
        private readonly bool plainStyle;
        private readonly IDialogueCheckPresenter checkPresenter;
        private readonly Func<IConversationView> viewFactory;
        private readonly IDisposable modalScope;

        private GameObject uiGo;
        private IConversationView currentView;
        private Action viewChanged;
        private StoreView storeView;
        private bool ended;

        /// <summary>The conversation reached End with no further store hand-off pending.</summary>
        public event Action Completed;

        /// <summary>Cancelled externally or after a presentation failure; never fires alongside Completed.</summary>
        public event Action Cancelled;

        public DialoguePresenter Presenter { get; }

        /// <summary>Includes synchronous completion during construction, before callers can subscribe.</summary>
        public bool IsEnded => ended;

        /// <summary>Diagnostics for the live presentation; the concrete view is null on the custom path.</summary>
        public IConversationView CurrentView => currentView;
        public DialogueView CurrentDialogueView => currentView as DialogueView;
        public StoreView CurrentStoreView => storeView;

        public ConversationController(DialogueDefinition dialogue, DialogueContext context, PanelSettings panelSettings, bool plainStyle = false,
            IDialogueCheckPresenter checkPresenter = null, Func<IConversationView> viewFactory = null)
        {
            this.context = context;
            this.panelSettings = panelSettings;
            this.plainStyle = plainStyle;
            this.checkPresenter = checkPresenter;
            this.viewFactory = viewFactory;
            Presenter = new DialoguePresenter(dialogue, context);
            modalScope = UIToolkitPointerCapture.BeginModal();
            AdvancePresentation();
        }

        public void Cancel() => EndConversation(Cancelled);

        public void Dispose() => Cancel();

        private void EndConversation(Action notification)
        {
            if (ended) return;
            ended = true;
            try
            {
                TearDownCurrentView();
            }
            finally
            {
                modalScope.Dispose();
                notification?.Invoke();
            }
        }

        private void AdvancePresentation()
        {
            if (ended) return; // A late view/check callback after cancellation owns no presentation.

            try
            {
                if (TryOpenRequestedStore()) return;

                if (Presenter.IsComplete)
                {
                    EndConversation(Completed);
                }
                else if (currentView == null)
                {
                    OpenDialogueView();
                }
            }
            catch
            {
                // Also covers factory/Initialize failures after returning from a store, not only startup.
                Cancel();
                throw;
            }
        }

        // An authored store request without a configured store is skipped with a warning.
        private bool TryOpenRequestedStore()
        {
            if (!context.ConsumeStoreOpenRequest()) return false;
            if (context.Store != null)
            {
                OpenStoreView();
                return true;
            }

            Debug.LogWarning("[ConversationController] the dialogue asked to open a store but this conversation has none; skipping it.");
            return false;
        }

        private void EnsureUiGameObject()
        {
            if (uiGo != null) return;
            uiGo = new GameObject("ConversationUI", typeof(UIDocument));
            uiGo.GetComponent<UIDocument>().panelSettings = panelSettings;
        }

        private void TearDownCurrentView()
        {
            // Detach ownership BEFORE external Dispose: callbacks/reentrant initialization must not clear a
            // newer view or destroy its document. The controller disposes each custom view exactly once.
            var oldView = currentView;
            var oldChanged = viewChanged;
            var oldStore = storeView;
            var oldGo = uiGo;
            currentView = null;
            viewChanged = null;
            storeView = null;
            uiGo = null;

            if (oldView != null)
            {
                oldView.Completed -= oldChanged;
                oldView.Driven -= oldChanged;
            }

            if (oldStore != null) oldStore.Closed -= HandleStoreClosed;

            try
            {
                oldView?.Dispose();
            }
            finally
            {
                if (oldGo != null)
                {
                    oldGo.SetActive(false);
                    UnityEngine.Object.Destroy(oldGo);
                }
            }
        }

        private void OpenDialogueView()
        {
            TearDownCurrentView();

            IConversationView view;
            if (viewFactory != null)
            {
                // Custom presentation creates no default UIDocument or DialogueView.
                view = viewFactory() ?? throw new InvalidOperationException("The conversation view factory returned null.");
            }
            else
            {
                EnsureUiGameObject();
                view = uiGo.AddComponent<DialogueView>();
            }

            currentView = view;
            viewChanged = () =>
            {
                if (ReferenceEquals(currentView, view)) AdvancePresentation();
            };
            view.Completed += viewChanged;
            view.Driven += viewChanged;
            // Initialize may synchronously complete or open a store. Do not touch view ownership afterwards.
            view.Initialize(Presenter, plainStyle, checkPresenter);
        }

        private void OpenStoreView()
        {
            TearDownCurrentView();
            EnsureUiGameObject();

            var storeService = new StoreService(context.RulesDatabase, context.PartyInventory, context.CurrencyItemId);
            storeView = uiGo.AddComponent<StoreView>();
            storeView.Closed += HandleStoreClosed;
            storeView.Initialize(new StorePresenter(context.Store, context.PartyInventory, context.RulesDatabase, storeService), plainStyle);
        }

        private void HandleStoreClosed() => AdvancePresentation();
    }
}
