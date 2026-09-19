using UnityEngine;

namespace CRPG.Presentation.Sprites
{
    /// <summary>
    /// Pure logic for the rocking tilt of a camera-facing sprite: feed it the character's speed each frame, read the
    /// tilt to apply. No scene objects, no clock -- unit-testable in EditMode.
    /// <para>The phase advances with DISTANCE travelled (a faster walker rocks faster), the amplitude eases in and out,
    /// and "moving" is decided from speed with hysteresis plus a coast: tactical movement is issued one cell at a time
    /// and the motor reports exactly zero velocity for 2-3 frames between legs, so a zero-speed gap is bridged (phase
    /// carried at the last real speed, amplitude held) until it has lasted both
    /// <see cref="RockingMotionSettings.coastSeconds"/> and <see cref="RockingMotionSettings.coastMinFrames"/>. The state
    /// sees only a float speed, never a move status, so a claim wait (status Moving, zero velocity) can never start a
    /// rock.</para>
    /// </summary>
    public sealed class RockingMotionState
    {
        private const float MaxDeltaTime = 0.1f;

        private readonly RockingMotionSettings settings;
        private bool isMoving;
        private float lastSpeed;
        private float coastTimer;
        private int gapFrames;

        public RockingMotionState(RockingMotionSettings settings = null) =>
            this.settings = (settings ?? new RockingMotionSettings()).Sanitized();

        /// <summary>Position in the rock cycle, 0 (inclusive) to 1 (exclusive).</summary>
        public float Phase { get; private set; }

        /// <summary>0 = level and at rest, 1 = full rock.</summary>
        public float Amplitude { get; private set; }

        /// <summary>Signed tilt to apply about the card's normal, in degrees.</summary>
        public float TiltDegrees { get; private set; }

        /// <summary>The hysteresis/coast decision: true while the character is moving OR bridging a short zero-speed gap.
        /// Not the same as Amplitude &gt; 0, which stays positive for a while after a real stop.</summary>
        public bool IsMoving => isMoving;

        /// <summary>True while moving but currently in a zero-speed gap (bridged by the coast).</summary>
        public bool IsCoasting => isMoving && gapFrames > 0;

        public void Reset()
        {
            isMoving = false;
            lastSpeed = 0f;
            coastTimer = 0f;
            gapFrames = 0;
            Phase = 0f;
            Amplitude = 0f;
            TiltDegrees = 0f;
        }

        public void Tick(float speed, float deltaTime)
        {
            // Sanitize inputs first. NaN must be caught before the clamp: Mathf.Min(NaN, 0.1f) returns 0.1.
            if (float.IsNaN(deltaTime) || deltaTime <= 0f) return;
            deltaTime = Mathf.Min(deltaTime, MaxDeltaTime);
            if (!float.IsFinite(speed) || speed < 0f) speed = 0f;

            if (!isMoving)
            {
                if (speed >= settings.startSpeed)
                {
                    // Not-moving -> moving edge. From complete rest a rock always begins level; if the amplitude has
                    // not finished easing out yet, continue from the current phase instead of popping to zero.
                    isMoving = true;
                    if (Amplitude <= 0f) Phase = 0f;
                    lastSpeed = speed;
                    coastTimer = 0f;
                    gapFrames = 0;
                }
            }
            else if (speed >= settings.stopSpeed)
            {
                lastSpeed = speed;
                coastTimer = 0f;
                gapFrames = 0;
            }
            else
            {
                coastTimer += deltaTime;
                gapFrames++;
                if (coastTimer >= settings.coastSeconds && gapFrames >= settings.coastMinFrames)
                {
                    isMoving = false;
                    lastSpeed = 0f;
                    coastTimer = 0f;
                    gapFrames = 0;
                }
            }

            if (isMoving)
            {
                var effectiveSpeed = speed >= settings.stopSpeed ? speed : lastSpeed;
                var phase = Phase + effectiveSpeed * deltaTime / settings.strideLength;
                Phase = phase - Mathf.Floor(phase);
            }

            var rate = isMoving ? settings.amplitudeRiseRate : settings.amplitudeFallRate;
            Amplitude = Mathf.MoveTowards(Amplitude, isMoving ? 1f : 0f, rate * deltaTime);
            TiltDegrees = Mathf.Sin(2f * Mathf.PI * Phase) * settings.peakDegrees * Amplitude;
        }
    }
}
