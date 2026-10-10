using UnityEngine;

namespace CRPG.Gameplay.Ambient
{
    /// <summary>
    /// Something an NPC carries in its hands during a work loop: the prefab, which humanoid bone holds it and the
    /// grip. The grip belongs to the prop and the rig, not to the stop that picks it up, so it lives here. Purely
    /// cosmetic: the prefab's colliders are stripped when it is attached, and it is never saved.
    /// </summary>
    [CreateAssetMenu(menuName = "CRPG Toolkit/Actors/Carried Prop")]
    public sealed class CarriedPropDefinition : ScriptableObject
    {
        [SerializeField] private GameObject prefab;
        [SerializeField] private HumanBodyBones bone = HumanBodyBones.RightHand;
        [SerializeField] private Vector3 localPosition;
        [SerializeField] private Vector3 localEulerAngles;
        [SerializeField] private float localScale = 1f;

        public GameObject Prefab => prefab;
        public HumanBodyBones Bone => bone;
        public Vector3 LocalPosition => localPosition;
        public Vector3 LocalEulerAngles => localEulerAngles;
        public float LocalScale => localScale <= 0f ? 1f : localScale;

        /// <summary>Code authoring (editor tools, tests) without reflection.</summary>
        public void Configure(GameObject prefab, HumanBodyBones bone, Vector3 localPosition, Vector3 localEulerAngles, float localScale = 1f)
        {
            this.prefab = prefab;
            this.bone = bone;
            this.localPosition = localPosition;
            this.localEulerAngles = localEulerAngles;
            this.localScale = localScale;
        }
    }
}
