using System;
using UnityEngine;

namespace CRPG.Presentation.Sprites
{
    /// <summary>
    /// Tunables for <see cref="RockingMotionState"/> (the South Park style side-to-side rock of a camera-facing
    /// sprite card). A class with field initializers on purpose: a serialized struct would deserialize all-zero on a
    /// freshly added component, and Unity never calls OnValidate on a nested serializable, so
    /// <see cref="RockingMotionState"/> sanitizes every field itself.
    /// </summary>
    [Serializable]
    public sealed class RockingMotionSettings
    {
        public const float DefaultPeakDegrees = 6f;
        public const float DefaultStrideLength = 1f;
        public const float DefaultStartSpeed = 0.10f;
        public const float DefaultStopSpeed = 0.05f;
        public const float DefaultCoastSeconds = 0.20f;
        public const int DefaultCoastMinFrames = 4;
        public const float DefaultAmplitudeRiseRate = 8f;
        public const float DefaultAmplitudeFallRate = 6f;

        [Tooltip("Largest tilt from level, in degrees, at full amplitude.")]
        public float peakDegrees = DefaultPeakDegrees;

        [Tooltip("World units travelled per full rock cycle. The rock is distance-driven, so a faster walker rocks faster.")]
        public float strideLength = DefaultStrideLength;

        [Tooltip("Speed at or above which a stopped character starts rocking (world units/s).")]
        public float startSpeed = DefaultStartSpeed;

        [Tooltip("Speed below which a rocking character starts to count as stopped (world units/s). Not above startSpeed.")]
        public float stopSpeed = DefaultStopSpeed;

        [Tooltip("How long a zero-speed gap is bridged before the rock is allowed to stop. Tactical movement is one cell per leg and reports zero velocity between legs. Keep below GridCharacterMotor.ClaimWaitSeconds (0.5).")]
        public float coastSeconds = DefaultCoastSeconds;

        [Tooltip("A gap must also last at least this many frames before the rock stops, so a slow frame rate cannot break a 2-3 frame leg gap.")]
        public int coastMinFrames = DefaultCoastMinFrames;

        [Tooltip("Amplitude gained per second while moving (amplitude runs 0..1).")]
        public float amplitudeRiseRate = DefaultAmplitudeRiseRate;

        [Tooltip("Amplitude lost per second once stopped.")]
        public float amplitudeFallRate = DefaultAmplitudeFallRate;

        public RockingMotionSettings Clone() => (RockingMotionSettings)MemberwiseClone();

        /// <summary>A copy with every field forced into a safe range. Never returns the receiver.</summary>
        public RockingMotionSettings Sanitized()
        {
            var s = Clone();
            s.peakDegrees = Finite(peakDegrees, DefaultPeakDegrees, 0f);
            s.strideLength = PositiveOr(strideLength, DefaultStrideLength);
            s.startSpeed = PositiveOr(startSpeed, DefaultStartSpeed);
            s.stopSpeed = Mathf.Clamp(Finite(stopSpeed, DefaultStopSpeed, 0f), 0f, s.startSpeed);
            s.coastSeconds = Finite(coastSeconds, DefaultCoastSeconds, 0f);
            s.coastMinFrames = Mathf.Max(0, coastMinFrames);
            s.amplitudeRiseRate = PositiveOr(amplitudeRiseRate, DefaultAmplitudeRiseRate);
            s.amplitudeFallRate = PositiveOr(amplitudeFallRate, DefaultAmplitudeFallRate);
            return s;
        }

        /// <summary>True when <see cref="Sanitized"/> would change nothing (the driver's OnValidate logs once when not).</summary>
        public bool IsValid()
        {
            var s = Sanitized();
            return s.peakDegrees == peakDegrees && s.strideLength == strideLength && s.startSpeed == startSpeed
                   && s.stopSpeed == stopSpeed && s.coastSeconds == coastSeconds && s.coastMinFrames == coastMinFrames
                   && s.amplitudeRiseRate == amplitudeRiseRate && s.amplitudeFallRate == amplitudeFallRate;
        }

        private static float Finite(float value, float fallback, float min) =>
            float.IsFinite(value) ? Mathf.Max(min, value) : fallback;

        private static float PositiveOr(float value, float fallback) =>
            float.IsFinite(value) && value > 0f ? value : fallback;
    }
}
