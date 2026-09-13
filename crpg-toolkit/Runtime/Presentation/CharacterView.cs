using System;
using CRPG.Presentation.Animation;
using CRPG.Presentation.Equipment;
using UnityEngine;
using UnityEngine.AI;

namespace CRPG.Presentation
{
    /// <summary>
    /// Lives on VisualRoot and manages the replaceable art child ("Model") beneath it -- the art-independent
    /// wrapper Docs/Design/Production-Foundation.md sec.4.1 describes. Rebinds the rig-dependent parts of
    /// <see cref="CharacterPlayableAnimator"/>/<see cref="Equipment.EquipmentVisualBinder"/> (which stay on
    /// CharacterRoot, not here -- they're gameplay-adjacent per-character-instance scripts, not replaceable
    /// art) whenever Model changes, without ever touching CharacterRoot's NavMeshAgent/collider/rigidbody or
    /// the character's entity/session identity.
    /// </summary>
    /// <remarks>
    /// Deliberately never references CRPG.Gameplay (CharacterAgent, GameSession, ...): the design's dependency
    /// direction is Gameplay -&gt; Presentation -&gt; {Actors, Inventory, Core}, never the reverse. CharacterAgent
    /// looks this component up (see its own <c>View</c> property), not the other way around.
    /// </remarks>
    [DisallowMultipleComponent]
    public sealed class CharacterView : MonoBehaviour
    {
        [Tooltip("Optional -- identifies which skeleton family this wrapper's Model(s) belong to. Passed to " +
                 "EquipmentVisualBinder for Skinned-profile rig-family validation (Task 2.4); a Skinned " +
                 "profile with no rig family authored on either side simply skips the check.")]
        [SerializeField] private CharacterRigDefinition rigDefinition;

        private CharacterPlayableAnimator playableAnimator;
        private EquipmentVisualBinder visualEquipment;
        private NavMeshAgent motionSource;

        public GameObject Model { get; private set; }
        public Animator Animator { get; private set; }
        public CharacterRigDefinition RigDefinition => rigDefinition;

        private void Awake()
        {
            playableAnimator = GetComponentInParent<CharacterPlayableAnimator>();
            visualEquipment = GetComponentInParent<EquipmentVisualBinder>();
            motionSource = GetComponentInParent<NavMeshAgent>();

            TryBindInitialModel();
        }

        // Fallback for TryBindInitialModel: confirmed empirically that when Model is a nested PrefabInstance
        // (the authored case -- e.g. BaseHumanMaleCharacter.prefab nests its FBX instance under VisualRoot),
        // it is NOT guaranteed attached yet during Awake() of sibling/ancestor components in the same outer
        // instantiate -- transform.childCount can read 0 in Awake even though the same child is unambiguously
        // present the instant InstantiatePrefab/Instantiate returns to the caller. Start() runs a full frame
        // later, by which point instantiation is always complete.
        private void Start() => TryBindInitialModel();

        private void TryBindInitialModel()
        {
            // A Model already authored as a child (the common case) is bound automatically; a bare
            // CharacterRoot template with none yet simply has no Model until SetModel is called. The
            // Model != null guard makes this safe to call from both Awake and Start without double-binding.
            if (Model != null || transform.childCount == 0) return;
            BindModel(transform.GetChild(0).gameObject);
        }

        /// <summary>Replaces the current Model with a freshly-instantiated copy of <paramref name="modelPrefab"/>,
        /// destroying the old one and rebinding CharacterPlayableAnimator's Animator/motion-provider reference
        /// and EquipmentVisualBinder's bone map to the new skeleton. Only Model changes -- CharacterRoot's
        /// gameplay components and the character's identity are never touched, and no second
        /// motor/Animator/physics writer is created (CharacterRoot's NavMeshAgent/collider/rigidbody are
        /// untouched; the old Model's Animator/PlayableGraph is torn down before the new one is bound).</summary>
        public void SetModel(GameObject modelPrefab)
        {
            if (modelPrefab == null) throw new ArgumentNullException(nameof(modelPrefab));

            if (Model != null) Destroy(Model);

            var instance = Instantiate(modelPrefab, transform);
            instance.name = "Model";
            instance.transform.localPosition = Vector3.zero;
            instance.transform.localRotation = Quaternion.identity;
            BindModel(instance);
        }

        private void BindModel(GameObject model)
        {
            Model = model;
            Animator = model.GetComponentInChildren<Animator>(true);

            if (Animator != null) playableAnimator?.BindPresentation(Animator, motionSource);
            visualEquipment?.RefreshBoneMap(rigDefinition);
        }
    }
}
