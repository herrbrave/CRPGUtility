using System;
using CRPG.Core;
using CRPG.Navigation;
using UnityEngine;
using UnityEngine.AI;

namespace CRPG.Gameplay.Interactions.Adapters
{
    /// <summary>Reference "use/door" adapter. Toggles passability through an <see cref="IWalkabilityBlocker"/>:
    /// any blocker component on this GameObject (e.g. a grid backend's), else the serialized carving
    /// NavMeshObstacle sized across a doorway gap -- not a bare Collider, since a Collider toggle alone doesn't
    /// prove NavMesh traversability actually changed, which is the whole point of this fixture (see
    /// UseDoorInteractionAdapterTests).</summary>
    [RequireComponent(typeof(WorldInteractable))]
    public sealed class UseDoorInteractionAdapter : MonoBehaviour, IInteractionAdapter
    {
        [SerializeField] private NavMeshObstacle obstacle;
        [SerializeField] private GameObject openVisual;
        [SerializeField] private GameObject closedVisual;

        private IWalkabilityBlocker blocker;

        private IWalkabilityBlocker Blocker =>
            blocker ??= GetComponent<IWalkabilityBlocker>()
                        ?? (obstacle != null ? new NavMeshObstacleWalkabilityBlocker(obstacle) : null);

        public DefinitionId ActionId { get; } = new("interact-use-door");

        /// <summary>True = open (obstacle not carving, passable). Starts closed.</summary>
        public bool IsOpen { get; private set; }

        public bool IsAvailable(RuntimeEntityId performer) => true; // always toggleable, this reference implementation

        public IInteractionExecution BeginExecute(RuntimeEntityId performer, Action<InteractionExecutionResult> onFinished)
        {
            IsOpen = !IsOpen;

            Blocker?.SetBlocking(!IsOpen); // not blocking = passable
            if (openVisual != null) openVisual.SetActive(IsOpen);
            if (closedVisual != null) closedVisual.SetActive(!IsOpen);

            onFinished(InteractionExecutionResult.Succeeded);
            return null;
        }
    }
}
