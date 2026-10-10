using System;
using System.Reflection;
using CRPG.Presentation.Animation;
using UnityEngine;
using UnityEngine.Playables;

namespace VikingRaid.Game
{
    /// <summary>
    /// Gives each character its own point in the idle and run cycles. Every loop starts at time zero, so four heroes sent off by one click jog in perfect lockstep, and a camp full of goblins
    /// breathes in unison. The toolkit's playable driver keeps its playables private and has no phase option, so this reaches the active port's playable by reflection (the one fragile spot,
    /// guarded by a test that fails loudly if the toolkit changes) and offsets its time a few frames after a loop starts. Phases are spaced by the golden ratio in spawn order, so any group is
    /// well spread: deterministic, not a dice roll that could line two heroes up.
    /// </summary>
    [DefaultExecutionOrder(30)]
    public sealed class AnimationPhaseOffset : MonoBehaviour
    {
        private const double GoldenRatioConjugate = 0.6180339887498949;
        private static int spawned;
        private static FieldInfo driverField, playablesField;

        private CharacterPlayableAnimator animator;
        private float phase01 = -1f;
        private bool lastMoving, hadState;
        private int applyAfterFrames;

        /// <summary>This character's offset into its loops, 0..1 of a cycle.</summary>
        public float Phase01 => phase01;

        public static float PhaseForIndex(int index) => (float)((0.15 + index * GoldenRatioConjugate) % 1.0);

        private void Awake() => phase01 = PhaseForIndex(spawned++);

        private void Update()
        {
            if (animator == null) animator = GetComponentInChildren<CharacterPlayableAnimator>(true) ?? GetComponent<CharacterPlayableAnimator>();
            if (animator == null || !animator.IsPresentationBound) return;

            var moving = animator.PresentationState.IsMoving;
            if (!hadState || moving != lastMoving) { applyAfterFrames = 3; hadState = true; lastMoving = moving; }   // a new loop just started: let the driver switch ports, then offset it
            if (applyAfterFrames > 0 && --applyAfterFrames == 0) Apply();
        }

        /// <summary>The normalised position (0..1) in the clip the active port is playing, or -1 when it cannot be read.</summary>
        public static float CurrentCyclePosition(CharacterPlayableAnimator animator)
        {
            if (!TryGetDriver(animator, out var driver)) return -1f;
            var clip = driver.GetPortClip(driver.ActivePort);
            return clip == null || clip.length <= 0f ? -1f : (float)((driver.GetPortTime(driver.ActivePort) % clip.length) / clip.length);
        }

        private void Apply()
        {
            if (!TryGetDriver(animator, out var driver)) { Debug.LogError("[AnimationPhaseOffset] the toolkit animator no longer exposes its driver this way: run-cycle phases cannot be offset"); enabled = false; return; }
            var playables = (Playable[])playablesField.GetValue(driver);
            var port = driver.ActivePort;
            var clip = driver.GetPortClip(port);
            if (clip == null || clip.length <= 0f || port < 0 || port >= playables.Length || !playables[port].IsValid()) return;
            playables[port].SetTime(phase01 * clip.length);
        }

        private static bool TryGetDriver(CharacterPlayableAnimator animator, out PlayableCharacterAnimationDriver driver)
        {
            driver = null;
            if (animator == null) return false;
            driverField ??= typeof(CharacterPlayableAnimator).GetField("driver", BindingFlags.Instance | BindingFlags.NonPublic);
            playablesField ??= typeof(PlayableCharacterAnimationDriver).GetField("portPlayables", BindingFlags.Instance | BindingFlags.NonPublic);
            if (driverField == null || playablesField == null) return false;
            driver = driverField.GetValue(animator) as PlayableCharacterAnimationDriver;
            return driver != null;
        }
    }
}
