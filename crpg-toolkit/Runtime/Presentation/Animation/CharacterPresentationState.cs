using System;
using CRPG.Core;

namespace CRPG.Presentation.Animation
{
    /// <summary>Identifies one action request so a completion/cancellation callback can be told apart from a
    /// stale one belonging to an already-superseded request -- the animation-side counterpart to
    /// CRPG.Core.MoveRequestId, same reasoning: a process-lifetime counter is enough, this never needs to be
    /// globally unique or survive a save.</summary>
    public readonly struct AnimationRequestToken : IEquatable<AnimationRequestToken>
    {
        private readonly long value;
        private AnimationRequestToken(long value) => this.value = value;

        public static readonly AnimationRequestToken None = default;

        private static long counter;
        public static AnimationRequestToken NewToken() => new(++counter);

        public bool IsValid => value != 0;

        public bool Equals(AnimationRequestToken other) => value == other.value;
        public override bool Equals(object obj) => obj is AnimationRequestToken other && Equals(other);
        public override int GetHashCode() => value.GetHashCode();
        public override string ToString() => value == 0 ? "AnimationRequestToken.None" : $"AnimationRequestToken({value})";

        public static bool operator ==(AnimationRequestToken a, AnimationRequestToken b) => a.Equals(b);
        public static bool operator !=(AnimationRequestToken a, AnimationRequestToken b) => !a.Equals(b);
    }

    /// <summary>How an action request ended -- delivered to its callback exactly once, never both.</summary>
    public enum AnimationOutcome
    {
        Completed,
        Cancelled,
    }

    /// <summary>
    /// Tracks WHAT this character's animation should currently be showing -- baseline locomotion, stance,
    /// the one in-flight exclusive action (if any), and terminal/death override -- entirely independent of
    /// Playables/graph mechanics, so its priority and exclusivity rules are unit-testable without an Animator,
    /// a PlayableGraph, or even a GameObject. Docs/Design/Production-Foundation.md sec.4.4: "Baseline
    /// locomotion updates continuously even while temporarily masked by an action. Combat stance chooses a
    /// locomotion set; it does not freeze movement animation... Death/terminal pose cannot be displaced by a
    /// move or cosmetic hit request... A late callback for a previous token cannot finish the current action."
    /// </summary>
    /// <remarks>
    /// A driver (<see cref="ICharacterAnimationDriver"/>) consumes this state to decide what to actually play;
    /// this class never touches an Animator/PlayableGraph itself. CharacterPlayableAnimator owns one instance
    /// of each and wires them together.
    /// </remarks>
    public sealed class CharacterPresentationState
    {
        private bool isMoving;
        private DefinitionId stanceId = DefinitionId.None;
        private AnimationRequestToken currentActionToken = AnimationRequestToken.None;
        private DefinitionId currentActionCueId = DefinitionId.None;
        private Action<AnimationOutcome> currentActionCallback;
        private bool isTerminal;
        private DefinitionId heldPoseCueId = DefinitionId.None;

        public bool IsMoving => isMoving;
        public DefinitionId StanceId => stanceId;
        public bool IsActionActive => currentActionToken.IsValid;
        public AnimationRequestToken CurrentActionToken => currentActionToken;
        public DefinitionId CurrentActionCueId => currentActionCueId;
        public bool IsTerminal => isTerminal;
        public bool IsPoseHeld => heldPoseCueId.IsValid;
        public DefinitionId HeldPoseCueId => heldPoseCueId;

        /// <summary>Fires only when the stance actually changes -- setting the identical stance again is a
        /// no-op and does not raise this (repeating a stance request must not restart anything).</summary>
        public event Action<DefinitionId> StanceChanged;

        /// <summary>Baseline locomotion updates continuously, even while an action is masking it visually
        /// (design sec.4.4) -- callers should call this every tick regardless of IsActionActive. Ignored once
        /// terminal (death overrides everything, including what would otherwise just be a visual-only flag).</summary>
        public void SetMoving(bool moving)
        {
            if (isTerminal) return;
            isMoving = moving;
        }

        /// <summary>Task 2.3: same contract as <see cref="SetMoving"/> (continuous, ignored once terminal),
        /// but derives the boolean from a real speed reading using a two-threshold hysteresis band instead of
        /// a single cutoff -- a velocity hovering right at one fixed threshold would otherwise chatter the
        /// baseline loop between idle/moving every tick. Once moving, stays moving until speed drops below
        /// stopThreshold; once idle, only starts moving once speed exceeds startThreshold. stopThreshold is
        /// clamped to never exceed startThreshold, so an inverted/misconfigured pair can't flip the band's
        /// meaning.</summary>
        public void SetMovingWithHysteresis(float speed, float startThreshold, float stopThreshold)
        {
            var effectiveStopThreshold = Math.Min(stopThreshold, startThreshold);
            var shouldMove = isMoving ? speed > effectiveStopThreshold : speed > startThreshold;
            SetMoving(shouldMove);
        }

        /// <summary>Sets the current stance (an explicit locomotion-set override, e.g. "combat" -- resolving
        /// that id to actual clips is CharacterAnimationProfile's job, not this class's). DefinitionId.None
        /// means the default locomotion set. Ignored once terminal.</summary>
        public void SetStance(DefinitionId newStanceId)
        {
            if (isTerminal || newStanceId == stanceId) return;
            stanceId = newStanceId;
            StanceChanged?.Invoke(stanceId);
        }

        /// <summary>Requests an exclusive full-body action. Implicitly cancels whatever action was previously
        /// in flight first (notifying ITS callback with Cancelled exactly once) before starting the new one --
        /// "retargeting cancels only the replaced request" applies to actions just as much as movement.
        /// Ignored (returns AnimationRequestToken.None, leaving any current action untouched) once terminal --
        /// death cannot be displaced by a cosmetic hit request.</summary>
        public AnimationRequestToken RequestAction(DefinitionId cueId, Action<AnimationOutcome> onOutcome = null)
        {
            if (isTerminal || IsPoseHeld) return AnimationRequestToken.None;

            // Commit the NEW token/callback before invoking the OLD one's callback (below), not after -- a
            // reentrant RequestAction called synchronously from within that old callback must see and extend
            // the state THIS call already established, not have its own assignment silently clobbered once
            // this call resumes and (with the old ordering) overwrote currentActionToken again. Every accepted
            // token gets exactly one outcome even under arbitrary reentrancy this way.
            var previousToken = currentActionToken;
            var previousCallback = currentActionCallback;

            // Captured locally, not re-read from the field after the callback below -- a reentrant call from
            // inside that callback can (legitimately) supersede THIS token before this method even returns;
            // the caller must still get back the token that actually identifies the request it just made, not
            // whatever happens to be current by the time control returns.
            var newToken = AnimationRequestToken.NewToken();
            currentActionToken = newToken;
            currentActionCueId = cueId;
            currentActionCallback = onOutcome;

            if (previousToken.IsValid) previousCallback?.Invoke(AnimationOutcome.Cancelled);

            return newToken;
        }

        /// <summary>Persistent gameplay pose, below terminal and above actions/locomotion. Not incapacitation.</summary>
        public void HoldPose(DefinitionId cueId)
        {
            if (!cueId.IsValid) throw new ArgumentException("A held pose needs a valid cue.", nameof(cueId));
            if (isTerminal) throw new InvalidOperationException("Cannot enter a held pose while terminal.");
            if (heldPoseCueId == cueId) return;
            if (IsPoseHeld) throw new InvalidOperationException("Release the existing held pose before replacing it.");
            heldPoseCueId = cueId; // Commit before cancellation callbacks: reentrant actions are rejected.
            CancelCurrentAction();
        }

        public void ReleasePose(DefinitionId cueId)
        {
            if (!cueId.IsValid) throw new ArgumentException("A pose release needs its owning cue.", nameof(cueId));
            if (!IsPoseHeld) return;
            if (heldPoseCueId != cueId) throw new InvalidOperationException("Cannot release another owner's held pose.");
            heldPoseCueId = DefinitionId.None;
        }

        /// <summary>Cancels the current action and reports Cancelled exactly once.</summary>
        public void CancelCurrentAction()
        {
            if (!currentActionToken.IsValid) return;
            Complete(currentActionToken, AnimationOutcome.Cancelled);
        }

        /// <summary>Reports that the clip behind <paramref name="token"/> finished playing naturally. A stale
        /// token -- one that's been superseded by a newer RequestAction, explicitly cancelled, or already
        /// reported -- is silently ignored: "a late callback for a previous token cannot finish the current
        /// action" (design sec.4.4). Notifies the callback with Completed exactly once.</summary>
        public void ReportActionFinished(AnimationRequestToken token)
        {
            if (!token.IsValid || token != currentActionToken) return;
            Complete(token, AnimationOutcome.Completed);
        }

        private void Complete(AnimationRequestToken token, AnimationOutcome outcome)
        {
            var callback = currentActionCallback;
            currentActionToken = AnimationRequestToken.None;
            currentActionCueId = DefinitionId.None;
            currentActionCallback = null;
            callback?.Invoke(outcome);
        }

        /// <summary>Enters the terminal (death/incapacitated) pose. Cancels any in-flight action first
        /// (notifying Cancelled). Idempotent. Nothing -- SetMoving/SetStance/RequestAction -- can displace it
        /// once set; only an explicit <see cref="Recover"/> can.</summary>
        public void EnterTerminal()
        {
            if (isTerminal) return;

            // Commit isTerminal BEFORE cancelling the in-flight action, not after: CancelCurrentAction
            // synchronously invokes the action's callback, and a callback that reentrantly calls RequestAction
            // must see isTerminal already true (so RequestAction's own guard correctly rejects it) -- the old
            // ordering left a window where a reentrant request during that callback was accepted, leaving
            // IsTerminal and IsActionActive true simultaneously.
            isTerminal = true;
            CancelCurrentAction();
        }

        /// <summary>Explicit gameplay-driven recovery from the terminal pose (e.g. a revive). "Incapacitation,
        /// recovery and resume come from gameplay intent" (design sec.4.4) -- never inferred automatically by
        /// this class. Idempotent.</summary>
        public void Recover() => isTerminal = false;

        /// <summary>Hard reset to a neutral state, bypassing the normal terminal guard (unlike SetStance/
        /// SetMoving/RequestAction, this works even while terminal). For a full identity unbind (a GameObject
        /// no longer representing the character it did) -- without this, a character that died and then had
        /// this component rebound to a completely different character would start that new character
        /// permanently stuck in the previous one's terminal pose.</summary>
        public void ResetToDefault()
        {
            CancelCurrentAction();
            isTerminal = false;
            heldPoseCueId = DefinitionId.None;
            isMoving = false;
            if (!stanceId.IsValid) return;
            stanceId = DefinitionId.None;
            StanceChanged?.Invoke(stanceId);
        }
    }
}
