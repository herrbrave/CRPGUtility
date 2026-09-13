using System.Collections.Generic;
using CRPG.Core;
using UnityEngine;

namespace CRPG.Gameplay.Interactions
{
    /// <summary>A static world object a character can approach and interact with (a chest, a door, an NPC
    /// conversation prompt, a teleport point). Resolves exactly one sibling <see cref="IInteractionAdapter"/> --
    /// this phase supports one action per target, not a query/selection menu. Requires an explicitly authored
    /// <see cref="interactionAnchor"/>; there is no implicit fallback to this object's own transform, since that
    /// could place the approach point inside the object itself (a chest, an NPC capsule, a closed door).</summary>
    [DisallowMultipleComponent]
    public sealed class WorldInteractable : MonoBehaviour
    {
        [SerializeField] private Transform interactionAnchor;
        [SerializeField] private float interactionRange = 1.5f;

        private static readonly HashSet<WorldInteractable> liveInteractables = new();
        public static IReadOnlyCollection<WorldInteractable> AllLive => liveInteractables;

        public RuntimeEntityId EntityId { get; private set; } = RuntimeEntityId.None;
        public IInteractionAdapter Adapter { get; private set; }
        public float InteractionRange => interactionRange;

        /// <summary>True only when exactly one IInteractionAdapter was found and interactionAnchor is assigned.
        /// An unconfigured interactable is treated as unavailable everywhere it's checked, never as a crash.</summary>
        public bool IsConfigured => Adapter != null && interactionAnchor != null;

        public Vector3 InteractionPoint => interactionAnchor != null ? interactionAnchor.position : transform.position;

        /// <summary>Assigns the required anchor/range in code -- for test fixtures and editor authoring scripts
        /// alike, so neither needs reflection or SerializedObject just to configure this component. Must be
        /// called before Awake runs (i.e. while the GameObject is still inactive), since Awake resolves
        /// IsConfigured immediately on an active object.</summary>
        public void Configure(Transform anchor, float range)
        {
            interactionAnchor = anchor;
            interactionRange = range;
        }

        private void Awake()
        {
            EntityId = RuntimeEntityId.NewId();

            var adapters = GetComponents<IInteractionAdapter>();
            if (adapters.Length == 1)
            {
                Adapter = adapters[0];
            }
            else if (adapters.Length > 1)
            {
                Debug.LogError($"{name}: WorldInteractable supports exactly one IInteractionAdapter this phase " +
                                $"(found {adapters.Length}) -- a multi-action query/selection contract is out of " +
                                "scope. Treating as unconfigured.", this);
            }

            if (interactionAnchor == null)
            {
                Debug.LogError($"{name}: WorldInteractable requires an explicit interactionAnchor -- there is no " +
                                "implicit fallback to this object's own transform, since that could place the " +
                                "approach point inside the object itself. Treating as unconfigured.", this);
            }
        }

        private void OnEnable() => liveInteractables.Add(this);
        private void OnDisable() => liveInteractables.Remove(this);
    }
}
