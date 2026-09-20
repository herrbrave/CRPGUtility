using UnityEngine;

namespace CRPG.Presentation.Sprites
{
    /// <summary>
    /// Pure logic for a sprite character's combat cues: a small state machine (Neutral / Down / Dead) with two overlays (attack, hit),
    /// producing a pose each tick. It never sees events, HP or the bus; <see cref="SpriteCueEventBinding"/> feeds it and
    /// <see cref="SpriteActionCues"/> applies the pose.
    /// <list type="bullet">
    /// <item><b>Attack</b>: a half-sine hop and scale punch; restarts if retriggered; ignored while Down or Dead.</item>
    /// <item><b>Hit</b>: a tint that fades from <see cref="SpriteCueSettings.hitTint"/> back to white; allowed while Down, ignored once Dead.</item>
    /// <item><b>Down</b>: tips over and stays down until Recover or Die.</item>
    /// <item><b>Die</b>: TERMINAL. Completes the tip, then fades to the corpse alpha and stays; afterwards attack, hit, Down and Recover are all
    /// ignored (so the real event order for massive damage -- CreatureDied then CreatureDroppedToZeroHP -- still ends Dead).</item>
    /// <item><b>Recover</b>: only from Down; tips back up. Ignored otherwise, and never undoes a Die.</item>
    /// </list>
    /// </summary>
    public sealed class SpriteCueState
    {
        private const float MaxDeltaTime = 0.1f;

        private enum Mode { Neutral, Down, Dead }

        private readonly SpriteCueSettings settings;
        private Mode mode;
        private bool attacking;
        private float attackElapsed;
        private bool hitting;
        private float hitElapsed;
        private float tipProgress; // 0 = upright, 1 = fully tipped
        private float fadeElapsed;

        public SpriteCueState(SpriteCueSettings settings = null) =>
            this.settings = (settings ?? new SpriteCueSettings()).Sanitized();

        public bool IsDown => mode == Mode.Down;
        public bool IsDead => mode == Mode.Dead;

        /// <summary>True once a Dead character has finished tipping and fading to the corpse alpha.</summary>
        public bool IsFadeComplete => mode == Mode.Dead && tipProgress >= 1f && fadeElapsed >= settings.dieFadeSeconds;

        public float HopOffset => attacking ? Mathf.Sin(Mathf.PI * (attackElapsed / settings.attackSeconds)) * settings.hopHeight : 0f;

        public float Scale => attacking ? 1f + Mathf.Sin(Mathf.PI * (attackElapsed / settings.attackSeconds)) * settings.attackScaleBoost : 1f;

        public float TipDegrees => tipProgress * settings.tipDegrees;

        /// <summary>The colour multiplier for every layer: the hit flash (if any) times the corpse fade (if Dead).</summary>
        public Color Tint
        {
            get
            {
                var color = hitting ? Color.Lerp(settings.hitTint, Color.white, hitElapsed / settings.hitSeconds) : Color.white;
                if (mode == Mode.Dead && tipProgress >= 1f)
                    color.a *= Mathf.Lerp(1f, settings.deadAlpha, Mathf.Clamp01(fadeElapsed / settings.dieFadeSeconds));
                return color;
            }
        }

        public void PlayAttack()
        {
            if (mode != Mode.Neutral) return;
            attacking = true;
            attackElapsed = 0f;
        }

        public void PlayHit()
        {
            if (mode == Mode.Dead) return;
            hitting = true;
            hitElapsed = 0f;
        }

        public void Down()
        {
            if (mode == Mode.Dead) return;
            mode = Mode.Down;
            attacking = false;
        }

        public void Die()
        {
            if (mode == Mode.Dead) return;
            mode = Mode.Dead;
            attacking = false;
            fadeElapsed = 0f;
        }

        public void Recover()
        {
            if (mode != Mode.Down) return;
            mode = Mode.Neutral;
        }

        /// <summary>Down with the tip already complete -- for a character restored from a save while unconscious.</summary>
        public void SnapDown()
        {
            if (mode == Mode.Dead) return;
            mode = Mode.Down;
            attacking = false;
            tipProgress = 1f;
        }

        public void Reset()
        {
            mode = Mode.Neutral;
            attacking = false;
            attackElapsed = 0f;
            hitting = false;
            hitElapsed = 0f;
            tipProgress = 0f;
            fadeElapsed = 0f;
        }

        public void Tick(float deltaTime)
        {
            if (float.IsNaN(deltaTime) || deltaTime <= 0f) return;
            deltaTime = Mathf.Min(deltaTime, MaxDeltaTime);

            if (attacking)
            {
                attackElapsed += deltaTime;
                if (attackElapsed >= settings.attackSeconds) attacking = false;
            }

            if (hitting)
            {
                hitElapsed += deltaTime;
                if (hitElapsed >= settings.hitSeconds) hitting = false;
            }

            var target = mode == Mode.Neutral ? 0f : 1f;
            tipProgress = Mathf.MoveTowards(tipProgress, target, deltaTime / settings.tipSeconds);

            if (mode == Mode.Dead && tipProgress >= 1f) fadeElapsed = Mathf.Min(fadeElapsed + deltaTime, settings.dieFadeSeconds);
        }
    }
}
