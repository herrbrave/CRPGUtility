using System;
using CRPG.Actors;
using CRPG.Core;
using UnityEngine;

namespace CRPG.Presentation.Sprites
{
    /// <summary>
    /// 2D campaign Task 2.6: the combat cues of a layered sprite character -- attack (hop + scale punch), hit (red flash), down (tips over),
    /// die (terminal, fades to a corpse) and recover (stands back up). Lives on CharacterRoot so it survives a Model swap. The pure
    /// <see cref="SpriteCueState"/> owns the rules; <see cref="SpriteCueEventBinding"/> feeds it from the event bus; this component ticks
    /// it and applies the pose to the compositor's layers root and tint.
    /// <para><b>What it writes:</b> only the layers root's local position/rotation/scale (a rotation about the character's FEET: the tip
    /// and the attack punch both pivot at the RockPivot origin without touching RockPivot, which the rocking driver owns) and the
    /// compositor's <see cref="SpriteLayerCompositor.TintMultiplier"/>. CharacterRoot, the collider, RockPivot and the sorter anchor are
    /// never touched. The baseline (the layers root's authored local transform) is captured per compositor INSTANCE strictly before the first
    /// pose write to it (a model swap leaves two compositors alive for a frame), and the current pose is reapplied to a new one, so a swap
    /// while Down keeps the corpse down.</para>
    /// <para><b>Subscription:</b> unlike the equipment/appearance binders this subscribes in <see cref="Initialize"/> (there is nothing to
    /// reconcile) and never unsubscribes in OnDisable. A character that is already down when its view binds (a save restored while
    /// unconscious) starts down.</para>
    /// <para>Implements <see cref="IDefeatPresentation"/>: WorldSpawnService.MarkDefeated defers destroying a defeated character until
    /// <see cref="BeginDefeat"/>'s callback fires, after the death fade and a short hold.</para>
    /// </summary>
    [DefaultExecutionOrder(10)]
    public sealed class SpriteActionCues : MonoBehaviour, IDefeatPresentation
    {
        [SerializeField] private SpriteCueSettings settings = new();
        [Tooltip("The ruleset's hit-point resource id (e.g. resource-hp in SRD5). Left empty the engine stays ruleset-agnostic and the character never recovers from Down " +
                 "(Recover is gated on HP above 0) and a save restored at 0 HP is only shown down if it has death-save state.")]
        [SerializeField] private DefinitionId hitPointResourceId;

        private SpriteCueState state;
        private SpriteCueEventBinding binding;
        private RuntimeEntityId character;
        private ICharacterRegistry characters;
        private IGameEventBus eventBus;

        private SpriteLayerCompositor compositor;
        private SpriteLayerCompositor poseCompositor;
        private Vector3 baselinePosition;
        private Vector3 baselineScale;
        private Quaternion baselineRotation;
        private bool poseWritten;
        private float lastTip, lastScale, lastHop;
        private Color lastTint;

        private Action defeatCallback;
        private float holdElapsed;

        public SpriteCueState State => state ??= new SpriteCueState(settings);
        public bool IsBound => eventBus != null;
        public DefinitionId HitPointResourceId { get => hitPointResourceId; set => hitPointResourceId = value; }
        public SpriteCueSettings Settings => settings;

        public void Initialize(RuntimeEntityId character, IGameEventBus eventBus, ICharacterRegistry characters)
        {
            if (this.eventBus != null)
            {
                if (this.character == character && ReferenceEquals(this.eventBus, eventBus) && ReferenceEquals(this.characters, characters)) return;

                throw new InvalidOperationException(
                    $"{name} is already bound to {this.character} -- call Unbind() before binding to a different character/session.");
            }

            if (!character.IsValid || eventBus == null || characters == null)
            {
                Debug.LogWarning($"[SpriteActionCues] Cannot bind '{name}': it needs a valid character, event bus and character registry.", this);
                return;
            }

            this.character = character;
            this.eventBus = eventBus;
            this.characters = characters;
            binding = new SpriteCueEventBinding(character, eventBus, characters, hitPointResourceId, State);

            if (SpriteCueEventBinding.IsDownAtBind(character, characters, hitPointResourceId)) State.SnapDown();
        }

        /// <summary>Stops reacting to events. The pose already applied stays.</summary>
        public void Unbind()
        {
            binding?.Dispose();
            binding = null;
            character = RuntimeEntityId.None;
            eventBus = null;
            characters = null;
        }

        public void PlayAttack() => State.PlayAttack();

        /// <summary>Plays the death (terminal) and reports back once the fade has finished plus the settings' hold, so the caller can destroy
        /// the character. See <see cref="IDefeatPresentation"/>.</summary>
        public void BeginDefeat(Action onFinished)
        {
            if (onFinished == null) return;
            State.Die();
            defeatCallback = onFinished;
            holdElapsed = 0f;
        }

        private void OnDestroy() => Unbind();

        // Deliberately does NOT unsubscribe: missing a CreatureDied while disabled would leave a corpse standing on re-enable.
        private void OnDisable()
        {
        }

        private void LateUpdate()
        {
            var cueState = State;
            cueState.Tick(Time.deltaTime);

            if (compositor == null) compositor = GetComponentInChildren<SpriteLayerCompositor>(true); // Unity null check: also true once destroyed
            if (compositor != null) ApplyPose(cueState);

            if (defeatCallback != null && cueState.IsFadeComplete)
            {
                holdElapsed += Time.deltaTime;
                if (holdElapsed >= settings.Sanitized().defeatHoldSeconds)
                {
                    var callback = defeatCallback;
                    defeatCallback = null;
                    callback();
                }
            }
        }

        private void ApplyPose(SpriteCueState cueState)
        {
            var root = compositor.LayersRoot;

            if (!ReferenceEquals(compositor, poseCompositor))
            {
                // A new compositor instance: capture its authored baseline BEFORE writing any pose to it, and force a reapply.
                poseCompositor = compositor;
                baselinePosition = root.localPosition;
                baselineScale = root.localScale;
                baselineRotation = root.localRotation;
                poseWritten = false;
            }

            var tip = cueState.TipDegrees;
            var scale = cueState.Scale;
            var hop = cueState.HopOffset;
            var tint = cueState.Tint;
            if (poseWritten && tip == lastTip && scale == lastScale && hop == lastHop && tint == lastTint) return;

            var rotation = Quaternion.Euler(0f, 0f, -tip);
            root.localScale = baselineScale * scale;
            root.localRotation = rotation * baselineRotation;
            // Rotate (and scale) the baseline offset about the feet (the RockPivot origin) rather than about the sprite's own centre.
            root.localPosition = rotation * (baselinePosition * scale) + new Vector3(0f, hop, 0f);
            compositor.TintMultiplier = tint;

            lastTip = tip;
            lastScale = scale;
            lastHop = hop;
            lastTint = tint;
            poseWritten = true;
        }
    }
}
