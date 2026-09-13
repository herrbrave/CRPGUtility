using System;
using CRPG.Core;
using CRPG.Gameplay.Scenes;
using UnityEngine;

namespace CRPG.Gameplay.Interactions.Adapters
{
    /// <summary>Reference "travel" adapter -- real cross-scene travel (as opposed to TeleportInteractionAdapter's
    /// same-scene NavMeshAgent.Warp). Runtime dependencies are public settable properties, matching every other
    /// adapter's "no Manager.Instance" convention. Runs the actual transition on Runner (a Core-scene-owned
    /// SceneTravelRunner), never on this adapter's own GameObject -- see SceneTravelRunner's own doc comment
    /// for why.</summary>
    [RequireComponent(typeof(WorldInteractable))]
    public sealed class TravelInteractionAdapter : MonoBehaviour, IInteractionAdapter
    {
        [SerializeField] private string targetScenePath;
        [SerializeField] private string destinationId;

        public SceneTravelRunner Runner { get; set; }
        public IWorldTransitionGate Gate { get; set; }

        public DefinitionId ActionId { get; } = new("interact-travel");

        public bool IsAvailable(RuntimeEntityId performer) =>
            !string.IsNullOrEmpty(targetScenePath) && Runner != null && Gate is { IsBusy: false };

        public IInteractionExecution BeginExecute(RuntimeEntityId performer, Action<InteractionExecutionResult> onFinished)
        {
            var execution = new Execution(onFinished);
            Runner.Travel(targetScenePath, destinationId, execution.HandleTravelFinished);
            return execution;
        }

        /// <summary>Cancel() is a documented, deliberate no-op once the underlying scene load has actually
        /// begun -- Unity cannot cleanly abort an in-progress additive scene load past activation. The travel
        /// completes normally regardless of the cancel signal and reports its REAL eventual outcome via
        /// onFinished exactly once; InteractionController's own "a fresh Interact supersedes the current one"
        /// path already tolerates this correctly (OnExecutionFinished's ReferenceEquals(CurrentRequest, request)
        /// guard silently discards a late/stale completion for a request that already ended some other way) --
        /// no InteractionController change was needed for this. The PlayerCommandRouter transition-gate check
        /// is what actually prevents a superseding command from being issued in practice while this is
        /// in-flight, since the gate goes busy at the very start of TravelCoroutine.</summary>
        private sealed class Execution : IInteractionExecution
        {
            private readonly Action<InteractionExecutionResult> onFinished;
            private bool finished;

            public Execution(Action<InteractionExecutionResult> onFinished)
            {
                this.onFinished = onFinished;
            }

            public void HandleTravelFinished(SceneTravelResult result)
            {
                if (finished) return;
                finished = true;
                onFinished(result.Success ? InteractionExecutionResult.Succeeded : InteractionExecutionResult.Failed);
            }

            public void Cancel()
            {
                // Deliberately does nothing -- see the class doc comment above.
            }
        }
    }
}
