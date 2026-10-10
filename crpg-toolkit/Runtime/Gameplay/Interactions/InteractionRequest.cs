using CRPG.Core;
using CRPG.Navigation;

namespace CRPG.Gameplay.Interactions
{
    /// <summary>Plain state for one in-flight interaction. Only <see cref="InteractionController"/> mutates
    /// this -- everything else reads it (e.g. tests asserting on <see cref="InteractionController.CurrentRequest"/>).</summary>
    public sealed class InteractionRequest
    {
        public InteractionRequest(RuntimeEntityId performer, WorldInteractable target)
        {
            Performer = performer;
            Target = target;
        }

        public RuntimeEntityId Performer { get; }
        public WorldInteractable Target { get; }

        public InteractionPhase Phase { get; internal set; }

        /// <summary>The MoveRequestId this interaction last established ownership of (its own approach request,
        /// or whatever id remained after its own Stop() call on arrival). A live Snapshot.RequestId that no
        /// longer matches this means an external order has taken over the performer's motion.</summary>
        public MoveRequestId OwnedMotionRequestId { get; internal set; } = MoveRequestId.None;

        /// <summary>Whether this request currently holds FacingPriority.Interaction -- only true from
        /// TurningToFace onward, so cleanup never clears a priority tier this request never actually claimed.</summary>
        public bool FacingClaimed { get; internal set; }

        /// <summary>The adapter's in-progress execution handle, if it hasn't already finished synchronously.</summary>
        public IInteractionExecution Execution { get; internal set; }

        /// <summary>The NPC this request froze the ambient routine of (RuntimeEntityId.None = none), so ending the
        /// request resumes exactly that one.</summary>
        internal RuntimeEntityId SuspendedAmbientNpc { get; set; } = RuntimeEntityId.None;

        /// <summary>Accumulated simulated time spent in Approaching, for the approach-timeout guard.</summary>
        public float ElapsedApproachSeconds { get; internal set; }
    }
}
