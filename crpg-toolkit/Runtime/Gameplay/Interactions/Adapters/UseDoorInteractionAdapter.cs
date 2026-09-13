using System;
using CRPG.Core;
using UnityEngine;
using UnityEngine.AI;

namespace CRPG.Gameplay.Interactions.Adapters
{
    /// <summary>Reference "use/door" adapter. Toggles a real carving NavMeshObstacle sized across a doorway
    /// gap, not a bare Collider -- a Collider toggle alone doesn't prove NavMesh traversability actually
    /// changed, which is the whole point of this fixture (see UseDoorInteractionAdapterTests).</summary>
    [RequireComponent(typeof(WorldInteractable))]
    public sealed class UseDoorInteractionAdapter : MonoBehaviour, IInteractionAdapter
    {
        [SerializeField] private NavMeshObstacle obstacle;
        [SerializeField] private GameObject openVisual;
        [SerializeField] private GameObject closedVisual;

        public DefinitionId ActionId { get; } = new("interact-use-door");

        /// <summary>True = open (obstacle not carving, passable). Starts closed.</summary>
        public bool IsOpen { get; private set; }

        public bool IsAvailable(RuntimeEntityId performer) => true; // always toggleable, this reference implementation

        public IInteractionExecution BeginExecute(RuntimeEntityId performer, Action<InteractionExecutionResult> onFinished)
        {
            IsOpen = !IsOpen;

            if (obstacle != null) obstacle.enabled = !IsOpen; // disabled = not carving = passable
            if (openVisual != null) openVisual.SetActive(IsOpen);
            if (closedVisual != null) closedVisual.SetActive(!IsOpen);

            onFinished(InteractionExecutionResult.Succeeded);
            return null;
        }
    }
}
