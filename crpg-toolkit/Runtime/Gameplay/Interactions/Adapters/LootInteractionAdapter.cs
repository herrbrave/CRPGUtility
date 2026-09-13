using System;
using CRPG.Core;
using CRPG.Inventory;
using UnityEngine;

namespace CRPG.Gameplay.Interactions.Adapters
{
    /// <summary>Reference "loot" adapter -- a thin wrapper over the already-existing LootContainerState/
    /// LootService (Runtime/Inventory/); no new inventory logic, just wiring into the shared interaction
    /// lifecycle.</summary>
    [RequireComponent(typeof(WorldInteractable))]
    public sealed class LootInteractionAdapter : MonoBehaviour, IInteractionAdapter
    {
        public PartyInventoryState Inventory { get; set; }

        /// <summary>Task 3.6: settable (was a private readonly-constructed instance) so a location's bootstrap
        /// can bind this to a session-owned, GetOrCreateLootContainer-sourced instance that survives the
        /// location's own scene being unloaded -- defaults to a fresh instance so existing callers that never
        /// explicitly set it (tests, InteractionLabHarness) keep working unchanged.</summary>
        public LootContainerState Container { get; set; } = new();

        public DefinitionId ActionId { get; } = new("interact-loot");

        public bool IsAvailable(RuntimeEntityId performer) => !Container.IsEmpty;

        public IInteractionExecution BeginExecute(RuntimeEntityId performer, Action<InteractionExecutionResult> onFinished)
        {
            // Re-check rather than trusting the IsAvailable result the controller already saw -- the container
            // could have been emptied (e.g. by another interaction) between that check and now.
            if (Container.IsEmpty)
            {
                onFinished(InteractionExecutionResult.Failed);
                return null;
            }

            new LootService(Inventory).TakeAll(Container);
            onFinished(InteractionExecutionResult.Succeeded);
            return null;
        }
    }
}
