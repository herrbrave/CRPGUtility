using CRPG.Navigation;
using CRPG.Presentation.Sprites;
using UnityEngine;

namespace CRPG.Gameplay
{
    /// <summary>
    /// Drives a sprite character's rock: reads the motor's <see cref="CRPG.Core.MotionSnapshot"/> velocity (never its
    /// status -- a claim wait reports Moving with zero velocity) and tilts a pivot at the character's feet about the
    /// card's normal. Backend-agnostic: it only touches <see cref="IMovable"/>, so NavMesh and grid motors both work.
    /// <para>Writes ONLY the pivot's local rotation. CharacterRoot and its collider are never moved, so click targeting
    /// cannot mis-hit a tilted sprite. Scaled time only: timeScale 0 freezes the rock.</para>
    /// </summary>
    public sealed class RockingMotionDriver : MonoBehaviour
    {
        [Tooltip("The transform to tilt, positioned at the character's feet. Child of the billboarded card.")]
        [SerializeField] private Transform rockPivot;
        [SerializeField] private RockingMotionSettings settings = new();

        private RockingMotionState motion;
        private IMovable movable;
        private bool warnedNoMovable;
        private bool warnedNoPivot;
        private float appliedTilt;

        public RockingMotionState Motion => motion ??= new RockingMotionState(settings);
        public Transform RockPivot { get => rockPivot; set => rockPivot = value; }
        public RockingMotionSettings Settings => settings;

        private void Awake()
        {
            movable = GetComponentInParent<IMovable>(true);
            _ = Motion;
        }

        private void OnValidate()
        {
            if (settings == null) settings = new RockingMotionSettings();
            if (!settings.IsValid())
            {
                Debug.LogWarning($"[RockingMotionDriver] Out-of-range rocking settings on '{name}' will be clamped.", this);
                settings = settings.Sanitized();
            }
        }

        private void Update()
        {
            if (rockPivot == null)
            {
                if (!warnedNoPivot)
                {
                    warnedNoPivot = true;
                    Debug.LogWarning($"[RockingMotionDriver] No rockPivot assigned on '{name}'.", this);
                }
                return;
            }

            if (movable == null)
            {
                movable = GetComponentInParent<IMovable>(true);
                if (movable == null)
                {
                    if (!warnedNoMovable)
                    {
                        warnedNoMovable = true;
                        Debug.LogWarning($"[RockingMotionDriver] No IMovable found on '{name}' or its parents; not rocking.", this);
                    }
                    return;
                }
            }

            var velocity = movable.Snapshot.Velocity; // read once: GridCharacterMotor.Snapshot recomputes per get
            var speed = new Vector2(velocity.x, velocity.z).magnitude; // horizontal only
            Motion.Tick(speed, Time.deltaTime);

            // Compare the float, not the quaternions: Quaternion == treats sub-0.1 degree differences as equal, which would
            // leave the pivot stuck at a tiny residual tilt instead of exactly level.
            var tilt = Motion.TiltDegrees;
            if (tilt != appliedTilt)
            {
                rockPivot.localRotation = tilt == 0f ? Quaternion.identity : Quaternion.Euler(0f, 0f, tilt);
                appliedTilt = tilt;
            }
        }

        private void OnDisable()
        {
            Motion.Reset();
            appliedTilt = 0f;
            if (rockPivot != null) rockPivot.localRotation = Quaternion.identity;
        }
    }
}
