using System;
using CRPG.Core;

namespace CRPG.Gameplay.Interactions
{
    /// <summary>Result of one interaction's execution phase, reported through the callback passed to
    /// <see cref="IInteractionAdapter.BeginExecute"/> -- distinguishes a verb that actually happened from one
    /// that didn't, so a controller/UI can tell the difference instead of always assuming success.</summary>
    public enum InteractionExecutionResult
    {
        Succeeded,
        Failed
    }

    /// <summary>A handle to one in-progress <see cref="IInteractionAdapter.BeginExecute"/> call, letting the
    /// controller tear it down if the request ends for a reason OTHER than the adapter's own completion
    /// callback (cancel, target lost, superseded, session end, host disable).</summary>
    public interface IInteractionExecution
    {
        /// <summary>Called by the controller when the owning request is ending for a reason other than this
        /// execution's own completion. Must be idempotent (safe to call more than once, or after the execution
        /// already finished on its own) and must NEVER invoke the completion callback itself -- the controller
        /// already knows the request is ending and is not expecting a result from it anymore.</summary>
        void Cancel();
    }

    /// <summary>One interactable verb (talk/loot/use-door/teleport/...). A <see cref="WorldInteractable"/>
    /// resolves exactly one sibling adapter -- this phase supports one action per target, not a query/selection
    /// menu (Docs/Design/Production-Foundation.md's "query available actions" step is satisfied by
    /// <see cref="ActionId"/>/<see cref="IsAvailable"/> alone).</summary>
    public interface IInteractionAdapter
    {
        DefinitionId ActionId { get; }

        /// <summary>Whether this verb can currently be performed by performer. Checked before approaching AND
        /// again immediately before executing -- must reflect live state (e.g. an emptied loot container, an
        /// already-open door), not a value cached at authoring time.</summary>
        bool IsAvailable(RuntimeEntityId performer);

        /// <summary>Begins the verb's effect. May complete synchronously -- invoking onFinished before this
        /// method even returns -- or asynchronously (e.g. Talk, waiting on a dialogue to close). Returns a
        /// handle the controller can <see cref="IInteractionExecution.Cancel"/> if the request ends before
        /// onFinished is ever called; return null if already finished synchronously. onFinished must be
        /// invoked at most once.</summary>
        IInteractionExecution BeginExecute(RuntimeEntityId performer, Action<InteractionExecutionResult> onFinished);
    }
}
