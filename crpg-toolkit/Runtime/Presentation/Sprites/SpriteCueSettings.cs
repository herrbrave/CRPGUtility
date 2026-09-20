using System;
using UnityEngine;

namespace CRPG.Presentation.Sprites
{
    /// <summary>
    /// Tunables for <see cref="SpriteCueState"/> (the combat cues of a layered sprite character). A class with field initializers so a
    /// freshly added component has real defaults; <see cref="SpriteCueState"/> sanitizes a copy, so a bad inspector value can never
    /// produce a NaN pose.
    /// </summary>
    [Serializable]
    public sealed class SpriteCueSettings
    {
        public const float DefaultHopHeight = 0.15f;
        public const float DefaultAttackSeconds = 0.25f;
        public const float DefaultAttackScaleBoost = 0.2f;
        public const float DefaultHitSeconds = 0.20f;
        public const float DefaultTipDegrees = 90f;
        public const float DefaultTipSeconds = 0.25f;
        public const float DefaultDieFadeSeconds = 0.6f;
        public const float DefaultDeadAlpha = 0.4f;
        public const float DefaultDefeatHoldSeconds = 0.5f;
        public static readonly Color DefaultHitTint = new(1f, 0.35f, 0.35f, 1f);

        [Tooltip("Attack: how far the sprite hops (world units, along the card's up).")]
        public float hopHeight = DefaultHopHeight;

        [Tooltip("Attack: duration of the hop and scale punch.")]
        public float attackSeconds = DefaultAttackSeconds;

        [Tooltip("Attack: extra scale at the peak of the punch (0.2 = 20 percent larger).")]
        public float attackScaleBoost = DefaultAttackScaleBoost;

        [Tooltip("Hit: how long the flash takes to fade back to normal.")]
        public float hitSeconds = DefaultHitSeconds;

        [Tooltip("Hit: the colour multiplied in at the start of the flash. A multiplier cannot brighten, so this is a tint (red), not white.")]
        public Color hitTint = DefaultHitTint;

        [Tooltip("Down/Die: how far the sprite tips over, in degrees.")]
        public float tipDegrees = DefaultTipDegrees;

        [Tooltip("Down/Die/Recover: time to tip over or stand back up.")]
        public float tipSeconds = DefaultTipSeconds;

        [Tooltip("Die: time to fade to the corpse alpha, once tipped over.")]
        public float dieFadeSeconds = DefaultDieFadeSeconds;

        [Tooltip("Die: the alpha the corpse fades to (0 = invisible, 1 = opaque).")]
        [Range(0f, 1f)] public float deadAlpha = DefaultDeadAlpha;

        [Tooltip("Die: how long the finished corpse lingers before a deferred defeat (WorldSpawnService.MarkDefeated) destroys it.")]
        public float defeatHoldSeconds = DefaultDefeatHoldSeconds;

        public SpriteCueSettings Clone() => (SpriteCueSettings)MemberwiseClone();

        /// <summary>A copy with every field forced into a safe range. Never returns the receiver.</summary>
        public SpriteCueSettings Sanitized()
        {
            var s = Clone();
            s.hopHeight = NonNegative(hopHeight, DefaultHopHeight);
            s.attackSeconds = Positive(attackSeconds, DefaultAttackSeconds);
            s.attackScaleBoost = NonNegative(attackScaleBoost, DefaultAttackScaleBoost);
            s.hitSeconds = Positive(hitSeconds, DefaultHitSeconds);
            s.hitTint = IsFinite(hitTint) ? hitTint : DefaultHitTint;
            s.tipDegrees = float.IsFinite(tipDegrees) ? Mathf.Clamp(tipDegrees, 0f, 180f) : DefaultTipDegrees;
            s.tipSeconds = Positive(tipSeconds, DefaultTipSeconds);
            s.dieFadeSeconds = Positive(dieFadeSeconds, DefaultDieFadeSeconds);
            s.deadAlpha = float.IsFinite(deadAlpha) ? Mathf.Clamp01(deadAlpha) : DefaultDeadAlpha;
            s.defeatHoldSeconds = NonNegative(defeatHoldSeconds, DefaultDefeatHoldSeconds);
            return s;
        }

        private static float Positive(float v, float fallback) => float.IsFinite(v) && v > 0f ? v : fallback;
        private static float NonNegative(float v, float fallback) => float.IsFinite(v) && v >= 0f ? v : fallback;
        private static bool IsFinite(Color c) => float.IsFinite(c.r) && float.IsFinite(c.g) && float.IsFinite(c.b) && float.IsFinite(c.a);
    }
}
