using CRPG.Core;
using CRPG.Presentation.Animation;
using NUnit.Framework;

namespace CRPG.Tests.EditMode
{
    /// <summary>Task 2.2 (Production Foundation): CharacterPresentationState's priority/exclusivity/token
    /// rules, entirely independent of Playables/GameObjects -- exactly the scenarios the task named: attack
    /// while moving, movement changing during an attack, cancelled token callback, repeated stance, death
    /// override.</summary>
    public class CharacterPresentationStateTests
    {
        private static readonly DefinitionId AttackCue = new("anim-attack-melee");
        private static readonly DefinitionId SecondActionCue = new("anim-attack-heavy");
        private static readonly DefinitionId CombatStance = new("stance-combat");
        private static readonly DefinitionId DeathCue = new("anim-death");

        [Test]
        public void SetMovingUpdatesIsMoving()
        {
            var state = new CharacterPresentationState();

            state.SetMoving(true);
            Assert.IsTrue(state.IsMoving);

            state.SetMoving(false);
            Assert.IsFalse(state.IsMoving);
        }

        [Test]
        public void SetMovingContinuesToUpdateWhileAnActionIsActive()
        {
            // "Attack while moving" / "movement changing during an attack": baseline locomotion must keep
            // tracking real motion even while an action masks it visually (design sec.4.4).
            var state = new CharacterPresentationState();
            state.SetMoving(true);

            var token = state.RequestAction(AttackCue);
            Assert.IsTrue(token.IsValid);
            Assert.IsTrue(state.IsActionActive);

            Assert.IsTrue(state.IsMoving, "Baseline must still read the true motion state while an action is active.");

            state.SetMoving(false);
            Assert.IsFalse(state.IsMoving, "Motion changes during an action must still be tracked, not frozen at the action's start.");
            Assert.IsTrue(state.IsActionActive, "The action itself must be unaffected by the motion change.");
        }

        [Test]
        public void SetMovingWithHysteresisStartsMovingOnlyAboveStartThreshold()
        {
            var state = new CharacterPresentationState();

            state.SetMovingWithHysteresis(0.05f, startThreshold: 0.1f, stopThreshold: 0.05f);
            Assert.IsFalse(state.IsMoving, "Speed at/below the start threshold must not begin movement.");

            state.SetMovingWithHysteresis(0.2f, startThreshold: 0.1f, stopThreshold: 0.05f);
            Assert.IsTrue(state.IsMoving);
        }

        [Test]
        public void SetMovingWithHysteresisStaysMovingThroughTheBandWithoutChattering()
        {
            // Task 2.3: a speed sitting between stop (0.05) and start (0.1) must not flip state on every call
            // once already moving -- the exact "chatter at the boundary" scenario hysteresis exists to fix.
            var state = new CharacterPresentationState();
            state.SetMovingWithHysteresis(0.2f, startThreshold: 0.1f, stopThreshold: 0.05f);
            Assert.IsTrue(state.IsMoving);

            for (var i = 0; i < 5; i++)
            {
                state.SetMovingWithHysteresis(0.07f, startThreshold: 0.1f, stopThreshold: 0.05f);
                Assert.IsTrue(state.IsMoving, "Speed within the hysteresis band must not stop movement once already moving.");
            }

            state.SetMovingWithHysteresis(0.02f, startThreshold: 0.1f, stopThreshold: 0.05f);
            Assert.IsFalse(state.IsMoving, "Speed below the stop threshold must end movement.");
        }

        [Test]
        public void SetMovingWithHysteresisClampsAnInvertedThresholdPair()
        {
            // A misconfigured stopThreshold > startThreshold must not invert the band's meaning.
            var state = new CharacterPresentationState();

            state.SetMovingWithHysteresis(0.15f, startThreshold: 0.1f, stopThreshold: 0.3f);
            Assert.IsTrue(state.IsMoving);

            state.SetMovingWithHysteresis(0.15f, startThreshold: 0.1f, stopThreshold: 0.3f);
            Assert.IsTrue(state.IsMoving, "The effective stop threshold must be clamped to at most startThreshold, not the misconfigured 0.3.");
        }

        [Test]
        public void RequestActionReturnsAValidTokenAndMarksActionActive()
        {
            var state = new CharacterPresentationState();

            var token = state.RequestAction(AttackCue);

            Assert.IsTrue(token.IsValid);
            Assert.IsTrue(state.IsActionActive);
            Assert.AreEqual(AttackCue, state.CurrentActionCueId);
            Assert.AreEqual(token, state.CurrentActionToken);
        }

        [Test]
        public void RequestActionWhileAnotherIsActiveCancelsThePreviousOneExactlyOnce()
        {
            var state = new CharacterPresentationState();
            var firstOutcomes = new System.Collections.Generic.List<AnimationOutcome>();
            var firstToken = state.RequestAction(AttackCue, outcome => firstOutcomes.Add(outcome));

            var secondToken = state.RequestAction(SecondActionCue);

            CollectionAssert.AreEqual(new[] { AnimationOutcome.Cancelled }, firstOutcomes);
            Assert.AreNotEqual(firstToken, secondToken);
            Assert.AreEqual(secondToken, state.CurrentActionToken);
            Assert.AreEqual(SecondActionCue, state.CurrentActionCueId);
        }

        [Test]
        public void ReportActionFinishedWithTheCurrentTokenCompletesItExactlyOnce()
        {
            var state = new CharacterPresentationState();
            var outcomes = new System.Collections.Generic.List<AnimationOutcome>();
            var token = state.RequestAction(AttackCue, outcome => outcomes.Add(outcome));

            state.ReportActionFinished(token);

            CollectionAssert.AreEqual(new[] { AnimationOutcome.Completed }, outcomes);
            Assert.IsFalse(state.IsActionActive);

            // A second, late report for the SAME (now-stale, already-completed) token must not fire again.
            state.ReportActionFinished(token);
            CollectionAssert.AreEqual(new[] { AnimationOutcome.Completed }, outcomes);
        }

        [Test]
        public void ReportActionFinishedWithAStaleSupersededTokenIsIgnored()
        {
            // "A late callback for a previous token cannot finish the current action" (design sec.4.4).
            var state = new CharacterPresentationState();
            var staleToken = state.RequestAction(AttackCue);

            var currentOutcomes = new System.Collections.Generic.List<AnimationOutcome>();
            var currentToken = state.RequestAction(SecondActionCue, outcome => currentOutcomes.Add(outcome));

            state.ReportActionFinished(staleToken); // late completion for the superseded first request

            Assert.IsEmpty(currentOutcomes, "A stale token's completion must not finish the current action.");
            Assert.IsTrue(state.IsActionActive);
            Assert.AreEqual(currentToken, state.CurrentActionToken);
        }

        [Test]
        public void CancelCurrentActionFiresCallbackWithCancelledExactlyOnce()
        {
            var state = new CharacterPresentationState();
            var outcomes = new System.Collections.Generic.List<AnimationOutcome>();
            state.RequestAction(AttackCue, outcome => outcomes.Add(outcome));

            state.CancelCurrentAction();
            state.CancelCurrentAction(); // idempotent -- nothing left to cancel

            CollectionAssert.AreEqual(new[] { AnimationOutcome.Cancelled }, outcomes);
            Assert.IsFalse(state.IsActionActive);
        }

        [Test]
        public void SettingTheSameStanceTwiceDoesNotRaiseStanceChanged()
        {
            var state = new CharacterPresentationState();
            var raisedCount = 0;
            state.StanceChanged += _ => raisedCount++;

            state.SetStance(CombatStance);
            state.SetStance(CombatStance); // repeated -- must not restart/re-raise anything

            Assert.AreEqual(1, raisedCount);
            Assert.AreEqual(CombatStance, state.StanceId);
        }

        [Test]
        public void SettingADifferentStanceRaisesStanceChanged()
        {
            var state = new CharacterPresentationState();
            var seen = new System.Collections.Generic.List<DefinitionId>();
            state.StanceChanged += seen.Add;

            state.SetStance(CombatStance);
            state.SetStance(DefinitionId.None);

            CollectionAssert.AreEqual(new[] { CombatStance, DefinitionId.None }, seen);
        }

        [Test]
        public void EnterTerminalCancelsAnyInFlightAction()
        {
            var state = new CharacterPresentationState();
            var outcomes = new System.Collections.Generic.List<AnimationOutcome>();
            state.RequestAction(AttackCue, outcome => outcomes.Add(outcome));

            state.EnterTerminal();

            CollectionAssert.AreEqual(new[] { AnimationOutcome.Cancelled }, outcomes);
            Assert.IsTrue(state.IsTerminal);
            Assert.IsFalse(state.IsActionActive);
        }

        [Test]
        public void ReentrantRequestActionDuringACancellationCallbackGetsItsOwnUnclobberedOutcome()
        {
            // A code review finding (Phase 2 correction pass): the old RequestAction assigned the new
            // token/callback AFTER invoking the previous one's (now-cancelled) callback, so a callback that
            // reentrantly called RequestAction again had ITS assignment silently overwritten once the
            // original call resumed and made its own assignment. Every accepted token must get exactly one
            // outcome even when a callback re-enters the state machine synchronously.
            var state = new CharacterPresentationState();
            var firstOutcomes = new System.Collections.Generic.List<AnimationOutcome>();
            var reentrantOutcomes = new System.Collections.Generic.List<AnimationOutcome>();
            AnimationRequestToken reentrantToken = default;

            state.RequestAction(AttackCue, outcome =>
            {
                firstOutcomes.Add(outcome);
                // Reentrant: fires synchronously from inside the callback the OUTER RequestAction call below
                // triggers when it cancels this one.
                reentrantToken = state.RequestAction(SecondActionCue, o => reentrantOutcomes.Add(o));
            });

            var outerToken = state.RequestAction(new DefinitionId("anim-attack-heavy-2"));

            Assert.AreEqual(new[] { AnimationOutcome.Cancelled }, firstOutcomes);
            Assert.AreNotEqual(reentrantToken, outerToken, "Each call must get back the token for the request IT made, not whichever happened to be current by the time control returned.");
            Assert.IsTrue(outerToken.IsValid);
            Assert.IsTrue(reentrantToken.IsValid);

            // Chronologically, the reentrant request happens INSIDE the outer call's own cancellation step --
            // i.e. strictly after outerToken was minted -- so it legitimately supersedes outerToken before
            // RequestAction even returns to its caller. state.CurrentActionToken reflects that real, final
            // ordering; outerToken itself stays a valid, stable identifier for the (now-superseded) request
            // the outer caller actually made.
            Assert.AreEqual(reentrantToken, state.CurrentActionToken, "The reentrant request, being chronologically last, is the one that ends up current.");
            Assert.IsTrue(state.IsActionActive);
            CollectionAssert.IsEmpty(reentrantOutcomes, "The reentrant request is still genuinely active -- nothing has resolved IT yet, so its own callback must not have fired.");

            // Now resolve the reentrant (currently-active) token and confirm it gets exactly one outcome too --
            // the actual point of the fix: neither request's callback was silently dropped by the reentrancy.
            state.ReportActionFinished(reentrantToken);
            CollectionAssert.AreEqual(new[] { AnimationOutcome.Completed }, reentrantOutcomes);
        }

        [Test]
        public void ReentrantRequestActionDuringEnterTerminalsCancellationCallbackIsRejected()
        {
            // Same review finding: EnterTerminal used to cancel the in-flight action BEFORE setting
            // isTerminal, so a callback that reentrantly requested a new action during that cancellation slid
            // in successfully, leaving IsTerminal and IsActionActive both true afterward.
            var state = new CharacterPresentationState();
            AnimationRequestToken reentrantToken = default;
            state.RequestAction(AttackCue, _ => { reentrantToken = state.RequestAction(SecondActionCue); });

            state.EnterTerminal();

            Assert.IsTrue(state.IsTerminal);
            Assert.IsFalse(state.IsActionActive, "Terminal must win -- a reentrant action request during EnterTerminal's own cancellation must be rejected.");
            Assert.AreEqual(AnimationRequestToken.None, reentrantToken);
        }

        [Test]
        public void TerminalStateRejectsFurtherActionStanceAndMovingRequests()
        {
            // "Death override": nothing can displace the terminal pose except an explicit Recover.
            var state = new CharacterPresentationState();
            state.SetMoving(true);
            state.SetStance(CombatStance);
            state.EnterTerminal();

            var rejectedToken = state.RequestAction(DeathCue);
            state.SetStance(DefinitionId.None);
            state.SetMoving(false);

            Assert.AreEqual(AnimationRequestToken.None, rejectedToken);
            Assert.IsFalse(state.IsActionActive);
            Assert.AreEqual(CombatStance, state.StanceId, "Stance must not change once terminal.");
            Assert.IsTrue(state.IsMoving, "Motion tracking must freeze once terminal -- a move request cannot displace death.");
            Assert.IsTrue(state.IsTerminal);
        }

        [Test]
        public void RecoverClearsTerminalStateAndAllowsRequestsAgain()
        {
            var state = new CharacterPresentationState();
            state.EnterTerminal();

            state.Recover();

            Assert.IsFalse(state.IsTerminal);
            var token = state.RequestAction(AttackCue);
            Assert.IsTrue(token.IsValid);
        }

        [Test]
        public void ResetToDefaultWorksEvenWhileTerminalAndClearsEverything()
        {
            var state = new CharacterPresentationState();
            state.SetMoving(true);
            state.SetStance(CombatStance);
            var outcomes = new System.Collections.Generic.List<AnimationOutcome>();
            state.RequestAction(AttackCue, outcome => outcomes.Add(outcome));
            state.EnterTerminal();

            state.ResetToDefault();

            Assert.IsFalse(state.IsTerminal);
            Assert.IsFalse(state.IsMoving);
            Assert.AreEqual(DefinitionId.None, state.StanceId);
            Assert.IsFalse(state.IsActionActive);
        }
    }
}
