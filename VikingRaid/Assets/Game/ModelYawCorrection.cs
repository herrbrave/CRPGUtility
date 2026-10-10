using CRPG.Presentation.Animation;
using UnityEngine;

namespace VikingRaid.Game
{
    /// <summary>
    /// Cancels the yaw the borrowed locomotion and idle clips carry when they are retargeted onto these rigs. MEASURED in the running game (hip line against the root's forward):
    /// the UAL Jog turns a hero's whole body about +30 degrees off the direction of travel, the War Camp goblin idle about +19, the goblin run about +31 (+34 on the Sidekick rig); the combat clips
    /// (attack, hit) are within -4..-10 and need nothing. The user saw it as "they don't face the right direction while running". Importer offsets on the Synty clips would live in a gitignored
    /// folder, so the correction is data on the character instead: while idle or moving the model root is turned by minus the measured offset; during an action or a terminal pose it is
    /// turned back to zero. Eased, so a state change never pops. Pure presentation: nothing else reads it.
    /// </summary>
    [DefaultExecutionOrder(20)]
    public sealed class ModelYawCorrection : MonoBehaviour
    {
        [SerializeField] private float idleYaw, movingYaw, turnSpeedDegreesPerSecond = 420f;

        private CharacterPlayableAnimator animator;
        private Transform modelRoot;
        private Quaternion baseRotation;
        private float current;

        public float IdleYaw { get => idleYaw; set => idleYaw = value; }
        public float MovingYaw { get => movingYaw; set => movingYaw = value; }
        public float Current => current;

        /// <summary>The rotation (degrees about Y) the model should have right now. Actions and terminal poses (death, down) use the combat clips, which carry no offset.</summary>
        public static float TargetYaw(bool actionActive, bool terminal, bool moving, float idleYaw, float movingYaw) =>
            actionActive || terminal ? 0f : moving ? -movingYaw : -idleYaw;

        private void LateUpdate()
        {
            if (animator == null) animator = GetComponentInChildren<CharacterPlayableAnimator>(true) ?? GetComponent<CharacterPlayableAnimator>();
            if (animator == null) return;
            if (modelRoot == null)
            {
                foreach (var candidate in GetComponentsInChildren<Animator>(true))
                    if (candidate.avatar != null) { modelRoot = candidate.transform; baseRotation = modelRoot.localRotation; break; }
                if (modelRoot == null) return;
            }

            var state = animator.PresentationState;
            var target = TargetYaw(state.IsActionActive, state.IsTerminal, state.IsMoving, idleYaw, movingYaw);
            current = Mathf.MoveTowardsAngle(current, target, turnSpeedDegreesPerSecond * Time.deltaTime);
            modelRoot.localRotation = Quaternion.Euler(0f, current, 0f) * baseRotation;
        }
    }
}
