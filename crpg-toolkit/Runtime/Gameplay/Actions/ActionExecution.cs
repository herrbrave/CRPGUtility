using System;
using CRPG.Core;

namespace CRPG.Gameplay.Actions
{
    /// <summary>Identifies one <see cref="ActionExecution"/> instance so a stale completion/interruption signal
    /// (a superseded reaction callback, an old motion request, ...) can be told apart from a current one --
    /// same process-lifetime-counter pattern as <see cref="CRPG.Core.MoveRequestId"/> and
    /// <see cref="CRPG.Presentation.Animation.AnimationRequestToken"/>.</summary>
    public readonly struct ActionToken : IEquatable<ActionToken>
    {
        private readonly long value;
        private ActionToken(long value) => this.value = value;

        public static readonly ActionToken None = default;

        private static long counter;
        public static ActionToken NewToken() => new(++counter);

        public bool IsValid => value != 0;

        public bool Equals(ActionToken other) => value == other.value;
        public override bool Equals(object obj) => obj is ActionToken other && Equals(other);
        public override int GetHashCode() => value.GetHashCode();
        public override string ToString() => value == 0 ? "ActionToken.None" : $"ActionToken({value})";

        public static bool operator ==(ActionToken a, ActionToken b) => a.Equals(b);
        public static bool operator !=(ActionToken a, ActionToken b) => !a.Equals(b);
    }

    /// <summary>Docs/Design/Production-Foundation.md sec.4.4: "Semantic action phases: Prepare -> Commit ->
    /// Recover -> Complete." Preparing is the only phase in which an ordinary caller-initiated
    /// <see cref="ActionExecution.Cancel"/> has any effect for an ability (see
    /// <see cref="AbilityActionExecution"/>'s own remarks) -- once Committing has begun, gameplay results may
    /// already be applied and cannot be un-applied. Movement's own phase usage is different (its "Committing"
    /// IS the whole multi-segment traversal, and Cancel does work throughout it) -- see
    /// <see cref="MoveActionExecution"/>.</summary>
    public enum ActionPhase
    {
        Preparing,
        Committing,
        Recovering,
        Complete,
    }

    /// <summary>How an <see cref="ActionExecution"/> ended. <see cref="Rejected"/>/<see cref="Faulted"/> reuse
    /// <see cref="CRPG.Abilities.AbilityExecutionResult"/>'s own vocabulary 1:1 (never a second competing
    /// meaning for "why didn't this happen"). <see cref="Cancelled"/> is pre-commit, caller-initiated.
    /// <see cref="Interrupted"/> is a post-commit-start invalidation that isn't an ordinary rejection -- the
    /// source or a participant died, a motion request was superseded by something else, or a live navigation
    /// signal (Blocked/Invalid) arrived mid-traversal.</summary>
    public enum ActionOutcome
    {
        Succeeded,
        Rejected,
        Faulted,
        Cancelled,
        Interrupted,
    }

    /// <summary>Task 4.3: the shared base for one in-flight, time-based action (an ability cast or a real-
    /// navigation move) driven by <see cref="ActionScheduler"/>. "Simulation time and authoritative commit
    /// schedule belong to action orchestration" (design sec.4.4) -- this class and its subclasses are the
    /// authority; any attached presentation (animation) is a fire-and-forget hint that can never gate a phase
    /// transition (see <see cref="IActionPresentationHook"/>), so missing/broken art can never soft-lock an
    /// action.</summary>
    public abstract class ActionExecution
    {
        private Action<ActionOutcome> ended;
        private bool finished;

        protected ActionExecution(RuntimeEntityId source)
        {
            Token = ActionToken.NewToken();
            Source = source;
        }

        public ActionToken Token { get; }
        public RuntimeEntityId Source { get; }
        public ActionPhase Phase { get; protected set; }

        /// <summary>Null while still in flight; set exactly once, at the same moment <see cref="Ended"/> fires.</summary>
        public ActionOutcome? Outcome { get; private set; }

        /// <summary>Fires exactly once, whenever this action reaches <see cref="ActionPhase.Complete"/> --
        /// guarded by <see cref="Finish"/> so neither subclass has to reimplement single-fire discipline
        /// (mirrors <see cref="CRPG.Presentation.Animation.CharacterPresentationState"/>'s own established
        /// "notifies the callback with an outcome exactly once" pattern).</summary>
        public event Action<ActionOutcome> Ended
        {
            add => ended += value;
            remove => ended -= value;
        }

        /// <summary>Driven by <see cref="ActionScheduler"/>, once per frame, with the SCALED
        /// <c>Time.deltaTime</c> -- pausing the game via <c>Time.timeScale = 0</c> is what makes a 0 delta here
        /// a true no-op with no separate pause flag needed, and simultaneously freezes any real NavMeshAgent
        /// backing a <see cref="MoveActionExecution"/>'s motor (see that class's own remarks). A no-op once
        /// already <see cref="ActionPhase.Complete"/>. Public (not internal), matching
        /// <c>CRPG.Gameplay.Interactions.InteractionController.Tick</c>'s own established convention -- a test
        /// (or any other direct owner) can drive an execution without needing a full ActionScheduler.</summary>
        public abstract void Tick(float deltaTime);

        /// <summary>Caller-initiated early termination. Each subclass documents exactly which phases this does
        /// anything in -- it is never assumed to be able to undo an already-applied gameplay result.</summary>
        public abstract void Cancel();

        /// <summary>Marks this action Complete with the given outcome and fires <see cref="Ended"/> -- safe to
        /// call more than once (every call after the first is a no-op), so a subclass never has to guard
        /// against a natural-completion/cancel race itself.</summary>
        protected void Finish(ActionOutcome outcome)
        {
            if (finished) return;
            finished = true;

            Phase = ActionPhase.Complete;
            Outcome = outcome;
            ended?.Invoke(outcome);
        }
    }
}
