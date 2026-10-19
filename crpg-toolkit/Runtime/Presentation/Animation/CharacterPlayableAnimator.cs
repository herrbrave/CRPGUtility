using System;
using CRPG.Actors;
using CRPG.Core;
using UnityEngine;
using UnityEngine.AI;

namespace CRPG.Presentation.Animation
{
    /// <summary>
    /// Routes animation requests -- baseline locomotion, stance, exclusive one-shot actions, terminal pose --
    /// onto a <see cref="CharacterPresentationState"/> (what should be showing) and an
    /// <see cref="ICharacterAnimationDriver"/> (how to actually show it, <see cref="PlayableCharacterAnimationDriver"/>
    /// by reference). Task 2.2: this class used to own the PlayableGraph/mixer/port mechanics directly and a
    /// hand-rolled baseline/transient state machine; both are now extracted so the state machine's priority/
    /// exclusivity/token rules are independently unit-testable (Tests/Editor/CharacterPresentationStateTests.cs)
    /// without a GameObject, and the graph mechanics can't accidentally diverge from that state.
    /// <para>Migrated from CRPG.Actors (Task 1.1/1.3) to CRPG.Presentation.Animation here: the design's
    /// dependency direction is Gameplay -&gt; Presentation -&gt; {Actors, Inventory, Core}, never the reverse, and
    /// this component IS fundamentally a presentation concern (it decides how a character LOOKS) that had been
    /// sitting in the wrong assembly since before CRPG.Presentation existed. The move only changes the
    /// namespace/assembly -- the script's own file GUID was preserved (moved, not deleted+recreated), so
    /// already-authored prefab references resolve unchanged; verified by reloading the real migrated
    /// BaseHumanMaleCharacter.prefab afterward.</para>
    /// </summary>
    public sealed class CharacterPlayableAnimator : MonoBehaviour
    {
        [SerializeField] private CharacterAnimationProfile profile;
        [SerializeField] private DefinitionId idleCueId = new("anim-idle");
        [SerializeField] private DefinitionId movingCueId = new("anim-moving");
        [SerializeField] private float movingSpeedThreshold = 0.1f;

        // Task 2.3, purely additive (existing authored values keep meaning "start moving" unchanged). Must
        // stay below movingSpeedThreshold to form a real hysteresis band -- see
        // CharacterPresentationState.SetMovingWithHysteresis, which also clamps defensively if it isn't.
        [SerializeField] private float movingStopSpeedThreshold = 0.05f;

        private RuntimeEntityId character;
        private IGameEventBus eventBus;
        private NavMeshAgent agent;

        // eventBus == null is this component's "unbound" signal; isSubscribed tracks only whether the
        // currently-retained binding is actively listening right now. Keeping these separate is what lets
        // OnDisable/OnEnable merely suspend/resume a binding while Unbind performs full detachment -- see
        // Initialize/Unbind's own remarks.
        private bool isSubscribed;

        private readonly CharacterPresentationState presentationState = new();
        private readonly ICharacterAnimationDriver driver = new PlayableCharacterAnimationDriver();
        private DefinitionId lastPlayedBaselineCueId;
        private bool hasPlayedBaseline;
        private bool wasMaskedLastTick;

        // Remembered so a Model rebind (BindPresentation) can re-establish the terminal pose on the new graph
        // -- CharacterPresentationState only tracks the boolean IsTerminal, not which cue produced the pose.
        private DefinitionId lastTerminalCueId;

        public CharacterPresentationState PresentationState => presentationState;
        public CharacterAnimationProfile Profile => profile;
        /// <summary>The requested held clip has full mixer ownership, not merely an incoming port.</summary>
        public bool IsHeldPoseSettled => presentationState.IsPoseHeld && !presentationState.IsTerminal && driver.IsBound &&
            profile != null && profile.TryGetCue(presentationState.HeldPoseCueId, out var cue) && cue.Clip != null &&
            CurrentClip == cue.Clip && driver is PlayableCharacterAnimationDriver playable && playable.GetPortWeight(playable.ActivePort) >= .999f;

        /// <summary>Looks a cue up in the bound profile (false when there is no profile or no such cue), so a caller
        /// can tell a looping pose from a one-shot and read its clip length before asking for it.</summary>
        public bool TryGetCue(DefinitionId cueId, out AnimationCueClip entry)
        {
            entry = null;
            return profile != null && profile.TryGetCue(cueId, out entry);
        }

        /// <summary>The bound profile's stance locomotion for a stance id (see CharacterAnimationProfile).</summary>
        public bool HasStanceLocomotion(DefinitionId stanceId) => profile != null && profile.TryGetStanceLocomotion(stanceId, out _, out _);

        /// <summary>Whether BindPresentation has bound a live driver graph right now.</summary>
        public bool IsPresentationBound => driver.IsBound;

        /// <summary>Diagnostic: the actual AnimationClip currently playing on the bound driver's active port,
        /// or null if unbound -- lets a test assert WHICH clip the graph is really showing (e.g. "resumed
        /// idle, not still on the attack swing") instead of only inferring it indirectly.</summary>
        public AnimationClip CurrentClip =>
            driver is PlayableCharacterAnimationDriver playableDriver && playableDriver.IsBound
                ? playableDriver.GetPortClip(playableDriver.ActivePort)
                : null;

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

        /// <summary>Fully detaches this animator: unsubscribes, clears the binding identity, and resets
        /// CharacterPresentationState to a neutral baseline (so a previous character's terminal/stance/action
        /// state can never leak into whatever character this component gets rebound to next). Idempotent.
        /// Called automatically on destroy; call directly before rebinding to a different character/session.</summary>
        public void Unbind()
        {
            Unsubscribe();

            character = RuntimeEntityId.None;
            eventBus = null;
            presentationState.ResetToDefault();
            hasPlayedBaseline = false;
            wasMaskedLastTick = false;
            lastTerminalCueId = DefinitionId.None;
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
        /// from. Tears down and recreates the driver's graph against the new Animator -- called by
        /// CharacterView both for the initial bind and for every subsequent Model swap, so there is never more
        /// than one live graph/output driving animation at a time.</summary>
        public void BindPresentation(Animator targetAnimator, NavMeshAgent motionSource)
        {
            agent = motionSource;
            driver.Bind(targetAnimator);
            hasPlayedBaseline = false;

            // Phase 2 correction pass (R4): a Model swap must not strand a request. An in-flight action can't
            // be meaningfully resumed mid-clip on a brand new graph with no elapsed-time continuity, so it is
            // cancelled exactly once here -- giving its caller a real, correct outcome -- rather than left
            // permanently active with no surviving completion route (the old driver's PlayOnce callback was
            // just destroyed along with its graph). A terminal pose, unlike an action, IS meaningfully
            // re-establishable, so its remembered cue is replayed on the new graph instead of being lost.
            if (presentationState.IsActionActive) presentationState.CancelCurrentAction();

            if (presentationState.IsTerminal && lastTerminalCueId.IsValid && profile != null &&
                profile.TryGetCue(lastTerminalCueId, out var terminalEntry))
            {
                driver.PlayLoop(terminalEntry);
            }

            if (!presentationState.IsTerminal && presentationState.IsPoseHeld) PlayHeldPose();
            wasMaskedLastTick = presentationState.IsTerminal || presentationState.IsPoseHeld || presentationState.IsActionActive;
            RefreshBaseline(force: true);
        }

        /// <summary>Tears down the current graph (if any) and forgets the bound Animator/motion source.
        /// Idempotent. Called automatically before <see cref="BindPresentation"/> rebinds to a new Model and
        /// on destroy. Does NOT reset CharacterPresentationState -- unlike a full identity Unbind, a Model
        /// swap must not forget that this character is, say, mid-action or dead; BindPresentation resumes
        /// showing whatever the state already says once the new Model is ready.</summary>
        public void UnbindPresentation()
        {
            driver.Unbind();
            agent = null;
            hasPlayedBaseline = false;
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
            driver.Tick(Time.deltaTime);

            // Baseline locomotion updates continuously, even while an action is masking it visually or the
            // character is terminal (design sec.4.4) -- SetMoving itself no-ops once terminal, but must still
            // be called every tick so it's accurate the instant Recover() is called. Hysteresis (Task 2.3)
            // keeps a velocity hovering near the threshold from chattering idle/moving every frame; the same
            // speed reading also drives the driver's continuous locomotion-speed blending.
            if (agent != null && agent.isActiveAndEnabled && agent.isOnNavMesh)
            {
                var speed = agent.velocity.magnitude;
                presentationState.SetMovingWithHysteresis(speed, movingSpeedThreshold, movingStopSpeedThreshold);
                driver.SetLocomotionSpeed(speed);
            }
            else if (agent != null)
            {
                presentationState.SetMoving(false);
                driver.SetLocomotionSpeed(0f);
            }

            // Phase 2 correction pass (R3): detect the exact tick an action/terminal mask LIFTS (regardless of
            // why -- natural completion, explicit cancel, a superseding request, or Recover) and force a
            // baseline replay on it. Without this, RefreshBaseline's own "already playing this cue, skip"
            // cache still remembers the cue that was active BEFORE the mask started, and if the desired cue
            // happens to be the same identity afterward (e.g. still idle), it wrongly concludes the driver is
            // already showing it -- when the driver's actual active clip is whatever the action/terminal pose
            // left behind (e.g. the attack swing's final frame).
            var isMaskedNow = presentationState.IsTerminal || presentationState.IsPoseHeld || presentationState.IsActionActive;
            var justUnmasked = wasMaskedLastTick && !isMaskedNow;
            wasMaskedLastTick = isMaskedNow;

            RefreshBaseline(force: justUnmasked);
        }

        private void RefreshBaseline(bool force)
        {
            if (!driver.IsBound || profile == null) return;
            if (presentationState.IsTerminal || presentationState.IsPoseHeld || presentationState.IsActionActive) return; // masked/overridden -- do not touch the driver

            var desiredCueId = ResolveBaselineCueId();
            if (!force && hasPlayedBaseline && desiredCueId == lastPlayedBaselineCueId) return;
            if (!profile.TryGetCue(desiredCueId, out var entry)) return;

            lastPlayedBaselineCueId = desiredCueId;
            hasPlayedBaseline = true;
            driver.PlayLoop(entry);
        }

        private DefinitionId ResolveBaselineCueId()
        {
            var stanceId = presentationState.StanceId;
            if (stanceId.IsValid && profile.TryGetStanceLocomotion(stanceId, out var stanceIdle, out var stanceMoving))
                return presentationState.IsMoving ? stanceMoving : stanceIdle;

            return presentationState.IsMoving ? movingCueId : idleCueId;
        }

        /// <summary>Sets the current stance (e.g. "combat") -- an explicit locomotion-set override. Combat
        /// stance chooses a locomotion set; it does not freeze movement animation, so baseline Idle/Moving
        /// keeps updating within whatever stance is active. DefinitionId.None means the default set.</summary>
        public void SetStance(DefinitionId stanceId) => presentationState.SetStance(stanceId);

        /// <summary>Changes authored presentation data without adding another graph or mutating shared assets.</summary>
        public void SetProfile(CharacterAnimationProfile next)
        {
            if (next == null) throw new ArgumentNullException(nameof(next));
            if (presentationState.IsActionActive) throw new InvalidOperationException("Cancel an action before changing its profile.");
            if (presentationState.IsPoseHeld && (!next.TryGetCue(presentationState.HeldPoseCueId, out var held) || !held.Loop || held.Clip == null))
                throw new InvalidOperationException("The new profile does not support the held pose.");
            profile = next;
            hasPlayedBaseline = false;
            if (presentationState.IsTerminal) return;
            if (presentationState.IsPoseHeld) PlayHeldPose();
            else RefreshBaseline(force: true);
        }

        public void HoldPose(DefinitionId cueId)
        {
            if (profile == null || !profile.TryGetCue(cueId, out var entry) || !entry.Loop || entry.Clip == null)
                throw new InvalidOperationException("A held pose must resolve to an authored looping cue.");
            var alreadyHeld = presentationState.IsPoseHeld && presentationState.HeldPoseCueId == cueId;
            presentationState.HoldPose(cueId);
            if (!alreadyHeld && !presentationState.IsTerminal && presentationState.IsPoseHeld) PlayHeldPose();
        }

        public void ReleasePose(DefinitionId cueId)
        {
            presentationState.ReleasePose(cueId);
            hasPlayedBaseline = false;
            RefreshBaseline(force: true);
        }

        private void PlayHeldPose()
        {
            if (!driver.IsBound) return;
            if (profile == null || !profile.TryGetCue(presentationState.HeldPoseCueId, out var entry) || !entry.Loop || entry.Clip == null)
                throw new InvalidOperationException("Held pose lost its looping clip during presentation rebind.");
            driver.PlayLoop(entry);
        }

        /// <summary>Requests an exclusive full-body action (e.g. a melee swing). Returns
        /// AnimationRequestToken.None (and does not touch the driver) if the cue isn't authored in the bound
        /// profile, or if the character is currently terminal. onOutcome is invoked exactly once, with
        /// Completed once the clip's duration elapses naturally or Cancelled if superseded/explicitly
        /// cancelled first -- never both, and never for a stale/superseded token.</summary>
        public AnimationRequestToken RequestAction(DefinitionId cueId, Action<AnimationOutcome> onOutcome = null)
        {
            if (profile == null || !profile.TryGetCue(cueId, out var entry)) return AnimationRequestToken.None;

            var token = presentationState.RequestAction(cueId, onOutcome);
            if (!token.IsValid) return token; // terminal -- rejected, driver untouched

            if (driver.IsBound) driver.PlayOnce(entry, () => presentationState.ReportActionFinished(token));
            return token;
        }

        /// <summary>Cancels the current action, if any (its onOutcome fires with Cancelled). A no-op if
        /// nothing is active.</summary>
        public void CancelCurrentAction() => presentationState.CancelCurrentAction();

        /// <summary>Enters the terminal (death/incapacitated) pose, optionally crossfading to a specific
        /// authored cue first. Cancels any in-flight action first. Nothing -- a move, a stance change, another
        /// action request -- can displace this once set; only <see cref="Recover"/> can.</summary>
        public void RequestTerminal(DefinitionId terminalCueId = default)
        {
            lastTerminalCueId = terminalCueId; // remembered so a later Model rebind can restore this pose (R4)
            presentationState.EnterTerminal();

            if (!driver.IsBound || profile == null || !terminalCueId.IsValid) return;
            if (profile.TryGetCue(terminalCueId, out var entry)) driver.PlayLoop(entry);
        }

        /// <summary>Explicit gameplay-driven recovery from the terminal pose (e.g. a revive). Does not itself
        /// resume any particular clip -- the next Update() tick's baseline resolution picks that up.</summary>
        public void Recover()
        {
            presentationState.Recover();
            lastTerminalCueId = DefinitionId.None;
            if (presentationState.IsPoseHeld) PlayHeldPose();
        }

        // Legacy entrypoint, kept as a compatibility adapter until every AnimationOrderService.RequestAnimation
        // caller migrates to the typed RequestAction/SetStance/RequestTerminal API above -- both paths route
        // through the SAME CharacterPresentationState/driver, so there is exactly one owner of what's playing
        // regardless of which entrypoint was used. A looping cue that isn't the profile's own idle/moving id
        // is treated as "enter this stance" (the old baselineIsExplicit rule); a non-looping cue is a
        // fire-and-forget action request (the old system had no completion callback for this path either).
        private void OnAnimationCueChanged(GameEventContext context)
        {
            if (context.Target != character || !TryGetCueId(context, out var cueId)) return;
            if (profile == null || !profile.TryGetCue(cueId, out var entry)) return;

            if (entry.Loop)
            {
                var isBaselineCue = cueId == idleCueId || cueId == movingCueId;
                SetStance(isBaselineCue ? DefinitionId.None : cueId);
            }
            else
            {
                RequestAction(cueId);
            }
        }

        private static bool TryGetCueId(GameEventContext context, out DefinitionId cueId)
        {
            if (context.Data.TryGetValue("CueId", out var value) && value is DefinitionId id) { cueId = id; return true; }
            cueId = default;
            return false;
        }
    }
}
