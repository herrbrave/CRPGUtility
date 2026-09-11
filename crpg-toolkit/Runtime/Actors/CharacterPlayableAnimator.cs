using System;
using CRPG.Core;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.Animations;
using UnityEngine.Playables;

namespace CRPG.Actors
{
    /// <summary>
    /// Turns AnimationCueChanged into an actual crossfaded PlayableGraph transition -- built directly on
    /// Playables (no AnimatorController/state machine), per the project's preference for lower-level control
    /// over Unity's animation system. Owns a 2-port AnimationMixerPlayable: a new cue connects to whichever
    /// port isn't currently active and crossfades in while the old one crossfades out over the SAME
    /// interpolation (there's no separate fade-in/fade-out step -- one is just 1 minus the other), then the old
    /// port's clip is destroyed and that port becomes free for the next transition.
    /// <para>Two tiers, kept deliberately minimal: a <b>baseline</b> (either locomotion-derived Idle/Moving,
    /// polled continuously from NavMeshAgent speed and never touching CharacterState -- see the project's
    /// "locomotion is a visual-only, continuous concern" decision -- or an explicit looping cue like a combat
    /// stance, which suppresses the locomotion poll until something explicitly hands control back) and an
    /// optional <b>transient</b> one-shot layered on top (e.g. a melee swing) that automatically reverts to
    /// the current baseline once its clip finishes playing. This is NOT a general state machine -- no
    /// interruption priority, no animation events, no partial-body blending (deliberately deferred -- see
    /// project notes on Universal Animation Library) -- just enough that a one-shot cue doesn't freeze on its
    /// last frame forever. A rapid interrupt mid-crossfade snaps to whichever port currently has the higher
    /// weight rather than attempting true N-way blending -- a deliberate simplicity trade-off, not an oversight.
    /// </para>
    /// <para>Task 1.3: no longer assumes the Animator/motion source live on this same GameObject -- the
    /// gameplay root (where this component lives) and the replaceable art Model (where the Animator lives)
    /// are separate GameObjects (Docs/Design/Production-Foundation.md sec.4.1). <see cref="BindPresentation"/>
    /// receives both explicitly, called by <see cref="CRPG.Presentation.CharacterView"/> whenever Model is
    /// bound or swapped -- a concern entirely separate from <see cref="Initialize"/>'s character/session
    /// identity binding, which is unaffected by a Model swap.</para>
    /// </summary>
    public sealed class CharacterPlayableAnimator : MonoBehaviour
    {
        [SerializeField] private CharacterAnimationProfile profile;
        [SerializeField] private DefinitionId idleCueId = new("anim-idle");
        [SerializeField] private DefinitionId movingCueId = new("anim-moving");
        [SerializeField] private float movingSpeedThreshold = 0.1f;

        private RuntimeEntityId character;
        private IGameEventBus eventBus;
        private NavMeshAgent agent;

        // eventBus == null is this component's "unbound" signal; isSubscribed tracks only whether the
        // currently-retained binding is actively listening right now. Keeping these separate is what lets
        // OnDisable/OnEnable merely suspend/resume a binding while Unbind performs full detachment -- see
        // Initialize/Unbind's own remarks.
        private bool isSubscribed;

        private PlayableGraph graph;
        private AnimationMixerPlayable mixer;
        private readonly Playable[] portPlayables = new Playable[2];
        private int activePort;
        private int fadingOutPort = -1;
        private float crossfadeElapsed;
        private float crossfadeDuration;

        private DefinitionId currentBaselineCueId;
        private bool baselineIsExplicit;
        private DefinitionId? transientCueId;
        private float transientElapsed;
        private float transientClipLength;

        /// <summary>Binds this animator to a character/event bus. Rebinding the identical (character,
        /// eventBus) tuple is a no-op (repeat spawn-time wiring must not disturb an already-live binding).
        /// Binding to a different character/session while already bound is an error -- callers must call
        /// <see cref="Unbind"/> first, since silently replacing a binding is exactly what let a rebind leak
        /// the previous character's subscriptions/visual state (Task 1.1 code review). Actually subscribing
        /// is deferred to <see cref="OnEnable"/> if this component is inactive/disabled right now, so an
        /// object configured before activation doesn't briefly violate the "disabled means unsubscribed"
        /// policy <see cref="OnDisable"/> establishes.</summary>
        public void Initialize(RuntimeEntityId character, IGameEventBus eventBus)
        {
            if (this.eventBus != null)
            {
                if (this.character == character && ReferenceEquals(this.eventBus, eventBus)) return;

                throw new InvalidOperationException(
                    $"{name} is already bound to {this.character} -- call Unbind() before binding to a different character/session.");
            }

            this.character = character;
            this.eventBus = eventBus;

            if (isActiveAndEnabled) Subscribe();
        }

        /// <summary>Fully detaches this animator: unsubscribes, clears the binding identity, and forgets the
        /// previous character's baseline/transient animation state (so a later rebind starts from a clean
        /// slate instead of a stray Update() reusing stale state). Idempotent. Called automatically on
        /// destroy; call directly before rebinding to a different character/session.</summary>
        public void Unbind()
        {
            Unsubscribe();

            character = RuntimeEntityId.None;
            eventBus = null;
            currentBaselineCueId = default;
            baselineIsExplicit = false;
            transientCueId = null;
        }

        private void Subscribe()
        {
            if (isSubscribed) return;
            eventBus.Subscribe(GameEvent.AnimationCueChanged, OnAnimationCueChanged);
            isSubscribed = true;
        }

        private void Unsubscribe()
        {
            if (!isSubscribed) return;
            eventBus?.Unsubscribe(GameEvent.AnimationCueChanged, OnAnimationCueChanged);
            isSubscribed = false;
        }

        /// <summary>Binds this component to the Animator its currently-attached Model actually owns, and to
        /// the motion source (typically CharacterRoot's NavMeshAgent) baseline locomotion reads velocity
        /// from. Tears down and recreates the PlayableGraph against the new Animator -- called by
        /// CharacterView both for the initial bind and for every subsequent Model swap, so there is never more
        /// than one live graph/output driving animation at a time.</summary>
        public void BindPresentation(Animator targetAnimator, NavMeshAgent motionSource)
        {
            if (targetAnimator == null) throw new ArgumentNullException(nameof(targetAnimator));

            UnbindPresentation();

            agent = motionSource;

            graph = PlayableGraph.Create($"{name} Animation");
            mixer = AnimationMixerPlayable.Create(graph, 2);
            var output = AnimationPlayableOutput.Create(graph, "Animation", targetAnimator);
            output.SetSourcePlayable(mixer);
            portPlayables[0] = Playable.Null;
            portPlayables[1] = Playable.Null;
            activePort = 0;
            fadingOutPort = -1;

            if (profile != null && profile.TryGetCue(idleCueId, out var idleEntry))
            {
                currentBaselineCueId = idleCueId;
                baselineIsExplicit = false;
                PlayImmediate(idleEntry);
            }

            graph.Play();
        }

        /// <summary>Tears down the current PlayableGraph (if any) and forgets the bound Animator/motion
        /// source/baseline-transient animation state. Idempotent. Called automatically before
        /// <see cref="BindPresentation"/> rebinds to a new Model and on destroy.</summary>
        public void UnbindPresentation()
        {
            if (graph.IsValid()) graph.Destroy();

            agent = null;
            portPlayables[0] = Playable.Null;
            portPlayables[1] = Playable.Null;
            activePort = 0;
            fadingOutPort = -1;
            currentBaselineCueId = default;
            baselineIsExplicit = false;
            transientCueId = null;
        }

        private void OnDestroy()
        {
            Unbind();
            UnbindPresentation();
        }

        // Suspends only -- unlike Unbind, character/eventBus stay set so OnEnable can resume the same
        // binding. This is what makes an explicit Unbind() (which DOES clear them) stick through a later
        // disable/enable cycle instead of silently reviving.
        private void OnDisable() => Unsubscribe();

        // Resumes a retained binding; a no-op if there's no binding (never Initialize()'d, or explicitly
        // Unbind()'d) or if Initialize already subscribed because this component was active/enabled then.
        private void OnEnable()
        {
            if (eventBus != null) Subscribe();
        }

        private void Update()
        {
            // No Model bound yet (or explicitly unbound) -- e.g. a bare CharacterRoot template with no art
            // attached, or the brief window before CharacterView's own Awake() runs BindPresentation.
            if (!graph.IsValid()) return;

            UpdateCrossfade();

            if (transientCueId.HasValue)
            {
                transientElapsed += Time.deltaTime;
                if (transientElapsed >= transientClipLength)
                {
                    transientCueId = null;
                    if (profile != null && profile.TryGetCue(currentBaselineCueId, out var baselineEntry)) PlayCue(baselineEntry);
                }

                return;
            }

            if (baselineIsExplicit || agent == null || profile == null) return;

            var moving = agent.velocity.sqrMagnitude > movingSpeedThreshold * movingSpeedThreshold;
            var desiredBaseline = moving ? movingCueId : idleCueId;
            if (desiredBaseline == currentBaselineCueId) return;

            currentBaselineCueId = desiredBaseline;
            if (profile.TryGetCue(desiredBaseline, out var entry)) PlayCue(entry);
        }

        private void OnAnimationCueChanged(GameEventContext context)
        {
            if (context.Target != character || !TryGetCueId(context, out var cueId)) return;
            if (profile == null || !profile.TryGetCue(cueId, out var entry)) return;

            if (entry.Loop)
            {
                currentBaselineCueId = cueId;
                baselineIsExplicit = cueId != idleCueId && cueId != movingCueId;
                transientCueId = null;
                PlayCue(entry);
            }
            else
            {
                transientCueId = cueId;
                transientElapsed = 0f;
                transientClipLength = entry.Clip.length;
                PlayCue(entry);
            }
        }

        private void PlayCue(AnimationCueClip entry)
        {
            // Interrupting mid-crossfade: snap the outgoing port to fully-weighted-out immediately (whichever
            // port currently holds more weight effectively becomes "current") rather than blending from a
            // fractional three-way state -- see the class doc comment on why this is a deliberate simplification.
            var incomingPort = 1 - activePort;
            if (portPlayables[incomingPort].IsValid()) portPlayables[incomingPort].Destroy();

            var clipPlayable = AnimationClipPlayable.Create(graph, entry.Clip);
            portPlayables[incomingPort] = clipPlayable;
            graph.Connect(clipPlayable, 0, mixer, incomingPort);
            mixer.SetInputWeight(incomingPort, 0f);

            fadingOutPort = activePort;
            activePort = incomingPort;
            crossfadeElapsed = 0f;
            crossfadeDuration = Mathf.Max(entry.CrossfadeDuration, 0.0001f);
        }

        // Only for the very first clip -- nothing to fade from yet, so it starts at full weight with no
        // crossfade in progress.
        private void PlayImmediate(AnimationCueClip entry)
        {
            var clipPlayable = AnimationClipPlayable.Create(graph, entry.Clip);
            portPlayables[activePort] = clipPlayable;
            graph.Connect(clipPlayable, 0, mixer, activePort);
            mixer.SetInputWeight(activePort, 1f);
        }

        private void UpdateCrossfade()
        {
            if (fadingOutPort < 0) return;

            crossfadeElapsed += Time.deltaTime;
            var t = Mathf.Clamp01(crossfadeElapsed / crossfadeDuration);
            mixer.SetInputWeight(activePort, t);
            mixer.SetInputWeight(fadingOutPort, 1f - t);

            if (t < 1f) return;

            if (portPlayables[fadingOutPort].IsValid()) portPlayables[fadingOutPort].Destroy();
            portPlayables[fadingOutPort] = Playable.Null;
            fadingOutPort = -1;
        }

        private static bool TryGetCueId(GameEventContext context, out DefinitionId cueId)
        {
            if (context.Data.TryGetValue("CueId", out var value) && value is DefinitionId id) { cueId = id; return true; }
            cueId = default;
            return false;
        }
    }
}
