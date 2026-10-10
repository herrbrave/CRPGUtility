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

        [Tooltip("A click on this object's character (an NPC body) is an Interact, not a Select.")]
        [SerializeField] private bool takesClickOverSelection;

        [Tooltip("The approach is complete as soon as the performer is within the interaction range, instead of when its motor reports arrival: for a target the performer can never reach exactly (a character).")]
        [SerializeField] private bool completeApproachInRange;

        /// <summary>The shortest range used when <see cref="CompleteApproachInRange"/> is on: two agent radii plus a margin, so the performer stops short of the target instead of pressing against it.</summary>
        public const float MinCharacterRange = 1f;

        private static readonly HashSet<WorldInteractable> liveInteractables = new();
        public static IReadOnlyCollection<WorldInteractable> AllLive => liveInteractables;

        public RuntimeEntityId EntityId { get; private set; } = RuntimeEntityId.None;
        public IInteractionAdapter Adapter { get; private set; }
        public float InteractionRange => completeApproachInRange ? Mathf.Max(interactionRange, MinCharacterRange) : interactionRange;

        /// <summary>The router resolves a click on a character carrying this as Interact (default false: a character is Select).</summary>
        public bool TakesClickOverSelection => takesClickOverSelection;

        /// <summary>See the tooltip: in-range completes the approach (default false: the motor must report arrival).</summary>
        public bool CompleteApproachInRange => completeApproachInRange;

        /// <summary>True only when exactly one IInteractionAdapter was found and interactionAnchor is assigned.
        /// An unconfigured interactable is treated as unavailable everywhere it's checked, never as a crash.</summary>
        public bool IsConfigured => Adapter != null && interactionAnchor != null;

        public Vector3 InteractionPoint => interactionAnchor != null ? interactionAnchor.position : transform.position;

        /// <summary>Assigns the required anchor/range in code -- for test fixtures and editor authoring scripts
        /// alike, so neither needs reflection or SerializedObject just to configure this component. May be called
        /// before or after Awake: if no adapter was found yet (the adapter is added after this component), it is
        /// looked up again here.</summary>
        public void Configure(Transform anchor, float range)
        {
            interactionAnchor = anchor;
            interactionRange = range;
            if (Adapter == null) ResolveAdapter();
        }

        /// <summary>Configures a talkable CHARACTER: the anchor is normally the character's own transform, a click on it is an Interact, and the approach completes in range.</summary>
        public void ConfigureForCharacter(Transform anchor, float range)
        {
            takesClickOverSelection = true;
            completeApproachInRange = true;
            Configure(anchor, range);
        }

        private bool multipleAdaptersReported;

        private void Awake()
        {
            EntityId = RuntimeEntityId.NewId();
            ResolveAdapter();
        }

        // The missing-anchor error is reported from Start, not Awake: Configure may legitimately run after Awake
        // (a component added to a live object, before its adapter), so an unconfigured Awake is not yet a mistake.
        private void Start()
        {
            if (interactionAnchor == null)
            {
                Debug.LogError($"{name}: WorldInteractable requires an explicit interactionAnchor -- there is no " +
                                "implicit fallback to this object's own transform, since that could place the " +
                                "approach point inside the object itself. Treating as unconfigured.", this);
            }
        }

        private void ResolveAdapter()
        {
            var adapters = GetComponents<IInteractionAdapter>();
            if (adapters.Length == 1)
            {
                Adapter = adapters[0];
            }
            else if (adapters.Length > 1 && !multipleAdaptersReported)
            {
                multipleAdaptersReported = true;
                Debug.LogError($"{name}: WorldInteractable supports exactly one IInteractionAdapter this phase " +
                                $"(found {adapters.Length}) -- a multi-action query/selection contract is out of " +
                                "scope. Treating as unconfigured.", this);
            }
        }

        private void OnEnable() => liveInteractables.Add(this);
        private void OnDisable() => liveInteractables.Remove(this);
    }
}
