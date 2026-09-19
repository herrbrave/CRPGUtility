using System;
using System.Linq;
using CRPG.Core;
using UnityEngine;

namespace CRPG.Gameplay.Interactions.Adapters
{
    /// <summary>Reference same-scene teleport adapter -- deliberately named for what it actually does, not
    /// "scene travel": <see cref="CRPG.Navigation.IMovable.Warp"/> is instant relocation within the current
    /// scene, exactly the distinction TeleportTriggerZone's own comment already draws. Cross-scene travel is
    /// TravelInteractionAdapter's job.</summary>
    [RequireComponent(typeof(WorldInteractable))]
    public sealed class TeleportInteractionAdapter : MonoBehaviour, IInteractionAdapter
    {
        [SerializeField] private Transform destination;

        public DefinitionId ActionId { get; } = new("interact-teleport");

        /// <summary>Reserved, unconsumed this task: a future Exploration/Camera-layer listener could snap the
        /// camera (PartyCameraFollowBehaviour.SnapToActiveCharacter) after a successful warp. Not wired here --
        /// doing so would need a new CRPG.Gameplay -> CRPG.Exploration/CRPG.Camera reference that doesn't exist
        /// and points the wrong direction in the current layering (Exploration already depends on Gameplay).</summary>
        public event Action<RuntimeEntityId> Teleported;

        public bool IsAvailable(RuntimeEntityId performer) => destination != null;

        public IInteractionExecution BeginExecute(RuntimeEntityId performer, Action<InteractionExecutionResult> onFinished)
        {
            var agent = CharacterAgent.AllLive.FirstOrDefault(a => a != null && a.EntityId == performer);
            if (agent == null)
            {
                onFinished(InteractionExecutionResult.Failed);
                return null;
            }

            var succeeded = agent.Motor != null && agent.Motor.Warp(destination.position);

            if (succeeded) Teleported?.Invoke(performer);
            onFinished(succeeded ? InteractionExecutionResult.Succeeded : InteractionExecutionResult.Failed);
            return null;
        }
    }
}
