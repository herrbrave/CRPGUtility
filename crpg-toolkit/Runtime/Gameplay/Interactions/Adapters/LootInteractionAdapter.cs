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
        private readonly LootContainerState container = new();

        public PartyInventoryState Inventory { get; set; }
        public LootContainerState Container => container;

        public DefinitionId ActionId { get; } = new("interact-loot");

        public bool IsAvailable(RuntimeEntityId performer) => !container.IsEmpty;

        public IInteractionExecution BeginExecute(RuntimeEntityId performer, Action<InteractionExecutionResult> onFinished)
        {
            // Re-check rather than trusting the IsAvailable result the controller already saw -- the container
            // could have been emptied (e.g. by another interaction) between that check and now.
            if (container.IsEmpty)
            {
                onFinished(InteractionExecutionResult.Failed);
                return null;
            }

            new LootService(Inventory).TakeAll(container);
            onFinished(InteractionExecutionResult.Succeeded);
            return null;
        }
    }
}
