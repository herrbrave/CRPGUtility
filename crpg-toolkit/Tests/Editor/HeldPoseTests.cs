using System;
using CRPG.Core;
using CRPG.Presentation.Animation;
using NUnit.Framework;

namespace CRPG.Tests
{
    public sealed class HeldPoseTests
    {
        private static readonly DefinitionId Pose = new("test-restraint-pose");
        private static readonly DefinitionId Action = new("test-action");
        [Test] public void HeldPoseIsNotTerminalAndBlocksOrdinaryActions()
        {
            var state = new CharacterPresentationState(); state.HoldPose(Pose);
            Assert.IsTrue(state.IsPoseHeld); Assert.IsFalse(state.IsTerminal); Assert.AreEqual(Pose, state.HeldPoseCueId);
            Assert.IsFalse(state.RequestAction(Action).IsValid);
        }
        [Test] public void EnteringPoseCancelsActionExactlyOnceAndRejectsReentrantAction()
        {
            var state = new CharacterPresentationState(); var cancellations = 0;
            state.RequestAction(Action, onOutcome: outcome => { Assert.AreEqual(AnimationOutcome.Cancelled, outcome); cancellations++; Assert.IsFalse(state.RequestAction(Action).IsValid); });
            state.HoldPose(Pose); state.HoldPose(Pose);
            Assert.AreEqual(1, cancellations); Assert.IsFalse(state.IsActionActive);
        }
        [Test] public void ReleasingPoseRestoresActionsWithoutChangingLocomotionOrStance()
        {
            var state = new CharacterPresentationState(); var stance = new DefinitionId("test-stance");
            state.SetMoving(true); state.SetStance(stance); state.HoldPose(Pose); state.ReleasePose(Pose);
            Assert.IsFalse(state.IsPoseHeld); Assert.IsTrue(state.IsMoving); Assert.AreEqual(stance, state.StanceId);
            Assert.IsTrue(state.RequestAction(Action).IsValid);
        }
        [Test] public void TerminalOverridesPoseAndRecoveryDoesNotReleaseRestraints()
        {
            var state = new CharacterPresentationState(); state.HoldPose(Pose); state.EnterTerminal(); state.Recover();
            Assert.IsTrue(state.IsPoseHeld); Assert.IsFalse(state.IsTerminal); Assert.IsFalse(state.RequestAction(Action).IsValid);
        }
        [Test] public void ResetClearsThePersistentPose()
        {
            var state = new CharacterPresentationState(); state.HoldPose(Pose); state.ResetToDefault();
            Assert.IsFalse(state.IsPoseHeld); Assert.IsTrue(state.RequestAction(Action).IsValid);
        }
        [Test] public void InvalidPoseTransitionsFailLoudly()
        {
            var state = new CharacterPresentationState();
            Assert.Throws<ArgumentException>(() => state.HoldPose(DefinitionId.None));
            state.HoldPose(Pose);
            Assert.Throws<InvalidOperationException>(() => state.HoldPose(Action));
            Assert.Throws<InvalidOperationException>(() => state.ReleasePose(Action));
            state.ReleasePose(Pose); state.EnterTerminal();
            Assert.Throws<InvalidOperationException>(() => state.HoldPose(Pose));
        }
    }
}
