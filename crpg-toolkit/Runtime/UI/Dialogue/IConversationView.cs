using System;

namespace CRPG.UI
{
    /// <summary>
    /// Presentation of one retained dialogue presenter. The controller subscribes before Initialize,
    /// which may synchronously drive or complete the conversation. Each factory call must return a fresh
    /// view; ownership transfers to the controller, including when Initialize throws. Dispose releases
    /// presentation resources and cancels callbacks, but never disposes or restarts the presenter.
    /// </summary>
    public interface IConversationView : IDisposable
    {
        /// <summary>The presenter reached End. The controller handles any pending store request first.</summary>
        event Action Completed;

        /// <summary>A driving command settled and its content was presented, without completing.</summary>
        event Action Driven;

        void Initialize(DialoguePresenter presenter, bool plainStyle = false, IDialogueCheckPresenter checkPresenter = null);
    }
}
