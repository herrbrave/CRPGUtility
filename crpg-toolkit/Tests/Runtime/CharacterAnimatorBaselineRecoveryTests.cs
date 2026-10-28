using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using CRPG.Actors;
using CRPG.Core;
using CRPG.Presentation.Animation;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace CRPG.Tests.PlayMode
{
    /// <summary>
    /// Phase 2 correction pass, R3 + R4: a code review of Task 2.5's work found that RefreshBaseline's
    /// "already playing this cue" cache doesn't get invalidated when an action/terminal mask LIFTS, so the
    /// graph can stay stuck on an action's last frame indefinitely even though presentation state correctly
    /// reports IsActionActive == false; and that rebinding to a new Animator (a Model swap) strands an
    /// in-flight action -- the presentation state keeps IsActionActive true forever with no surviving
    /// completion route, permanently masking baseline. These tests reproduce both against the REAL
    /// CharacterPlayableAnimator/PlayableCharacterAnimationDriver pair, not a synthetic stand-in.
    /// </summary>
    public class CharacterAnimatorBaselineRecoveryTests
    {
        private static readonly DefinitionId IdleCue = new("anim-idle");
        private static readonly DefinitionId MovingCue = new("anim-moving");
        private static readonly DefinitionId AttackCue = new("anim-attack-melee");
        private static readonly DefinitionId DeathCue = new("anim-death");

        private GameObject rootGo;
        private GameObject modelGoA;
        private GameObject modelGoB;
        private AnimationClip idleClip;
        private AnimationClip attackClip;
        private AnimationClip deathClip;

        [TearDown]
        public void TearDown()
        {
            if (rootGo != null) UnityEngine.Object.DestroyImmediate(rootGo);
            if (modelGoA != null) UnityEngine.Object.DestroyImmediate(modelGoA);
            if (modelGoB != null) UnityEngine.Object.DestroyImmediate(modelGoB);
        }

        private static void SetField(object target, string fieldName, object value)
        {
            var type = target.GetType();
            while (type != null)
            {
                var field = type.GetField(fieldName, BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.DeclaredOnly);
                if (field != null) { field.SetValue(target, value); return; }
                type = type.BaseType;
            }

            throw new System.Exception($"Field '{fieldName}' not found on {target.GetType().Name} or its base types.");
        }

        // A real clip with a real, short, specific length -- new AnimationClip() alone actually defaults to
        // length 1 (confirmed empirically, not 0), which is usable but not a controlled/short value for
        // these timing-sensitive tests.
        private static AnimationClip CreateClipWithLength(float seconds)
        {
            var clip = new AnimationClip();
            clip.SetCurve(string.Empty, typeof(Transform), "localPosition.x", AnimationCurve.Linear(0f, 0f, seconds, 1f));
            return clip;
        }

        private static AnimationCueClip CreateCue(DefinitionId cueId, AnimationClip clip, bool loop, float crossfadeDuration = 0.05f)
        {
            object boxed = new AnimationCueClip();
            SetField(boxed, "cueId", cueId);
            SetField(boxed, "clip", clip);
            SetField(boxed, "crossfadeDuration", crossfadeDuration);
            SetField(boxed, "loop", loop);
            return (AnimationCueClip)boxed;
        }

        private CharacterAnimationProfile CreateProfile()
        {
            idleClip = new AnimationClip();
            attackClip = CreateClipWithLength(0.05f);
            deathClip = new AnimationClip();

            var profile = ScriptableObject.CreateInstance<CharacterAnimationProfile>();
            var cues = new List<AnimationCueClip>
            {
                CreateCue(IdleCue, idleClip, loop: true),
                CreateCue(MovingCue, idleClip, loop: true), // same clip is fine -- these tests never move
                CreateCue(AttackCue, attackClip, loop: false),
                CreateCue(DeathCue, deathClip, loop: true),
            };
            SetField(profile, "cues", cues);
            return profile;
        }

        private GameObject CreateModel(string name)
        {
            var model = new GameObject(name);
            model.AddComponent<Animator>();
            return model;
        }

        private CharacterPlayableAnimator CreateBoundAnimator()
        {
            rootGo = new GameObject("CharacterRoot");
            var animator = rootGo.AddComponent<CharacterPlayableAnimator>();
            SetField(animator, "profile", CreateProfile());

            modelGoA = CreateModel("ModelA");
            animator.BindPresentation(modelGoA.GetComponent<Animator>(), null);
            return animator;
        }

        [UnityTest]
        public IEnumerator BaselineResumesIdleAfterAnActionCompletesNaturally()
        {
            var animator = CreateBoundAnimator();
            yield return null; // let the initial baseline settle

            var completed = false;
            animator.RequestAction(AttackCue, outcome => completed = outcome == AnimationOutcome.Completed);
            Assert.AreSame(attackClip, animator.CurrentClip, "The attack clip must actually be playing.");

            var elapsed = 0f;
            while (!completed && elapsed < 2f) { yield return null; elapsed += Time.deltaTime; }
            Assert.IsTrue(completed, "The short attack clip should have completed within 2 real seconds.");
            Assert.IsFalse(animator.PresentationState.IsActionActive);

            yield return null; // one more Update tick for the now-unmasked baseline to actually replay

            Assert.AreSame(idleClip, animator.CurrentClip, "Baseline must have resumed idle -- the old bug left the graph stuck on the attack clip's last frame.");
        }

        [UnityTest]
        public IEnumerator BaselineResumesIdleAfterAnActionIsExplicitlyCancelled()
        {
            var animator = CreateBoundAnimator();
            yield return null;

            var outcome = (AnimationOutcome?)null;
            animator.RequestAction(AttackCue, o => outcome = o);
            yield return null;
            Assert.AreSame(attackClip, animator.CurrentClip);

            animator.CancelCurrentAction();
            Assert.AreEqual(AnimationOutcome.Cancelled, outcome);
            Assert.IsFalse(animator.PresentationState.IsActionActive);

            yield return null; // forced baseline replay on the unmask tick

            Assert.AreSame(idleClip, animator.CurrentClip, "Baseline must resume idle after an explicit cancel too, not just natural completion.");
        }

        [UnityTest]
        public IEnumerator SameFrameCancellationReplaysBaselineAndCompletesExactlyOnce()
        {
            var animator = CreateBoundAnimator();
            yield return null;
            var outcomes = new List<AnimationOutcome>();
            animator.RequestAction(AttackCue, outcomes.Add);
            Assert.AreSame(attackClip, animator.CurrentClip);

            // No Update observes the action mask between request and cancellation.
            animator.CancelCurrentAction();
            animator.CancelCurrentAction();
            CollectionAssert.AreEqual(new[] { AnimationOutcome.Cancelled }, outcomes);
            Assert.IsFalse(animator.PresentationState.IsActionActive);
            Assert.AreSame(idleClip, animator.CurrentClip, "Cancellation must release the actual graph, not only the request state.");
            yield return null;
            yield return null;
            Assert.AreSame(idleClip, animator.CurrentClip);
            Assert.AreEqual(1, outcomes.Count);
        }

        [UnityTest]
        public IEnumerator SameFrameCancellationPreservesAnActionStartedByItsCallback()
        {
            var animator = CreateBoundAnimator();
            yield return null;
            var cancelled = 0;
            animator.RequestAction(AttackCue, outcome =>
            {
                Assert.AreEqual(AnimationOutcome.Cancelled, outcome);
                cancelled++;
                Assert.IsTrue(animator.RequestAction(AttackCue).IsValid);
            });
            animator.CancelCurrentAction();
            Assert.AreEqual(1, cancelled);
            Assert.IsTrue(animator.PresentationState.IsActionActive);
            Assert.AreSame(attackClip, animator.CurrentClip, "The replacement action owns the driver, not the old baseline.");
            yield return null;
        }

        [UnityTest]
        public IEnumerator SameFrameCancellationRestoresBaselineEvenWhenItsCallbackThrows()
        {
            var animator = CreateBoundAnimator();
            yield return null;
            var calls = 0;
            var failure = new System.InvalidOperationException("Cancellation callback failed.");
            animator.RequestAction(AttackCue, _ => { calls++; throw failure; });
            Assert.AreSame(failure, Assert.Throws<System.InvalidOperationException>(() => animator.CancelCurrentAction()));
            Assert.AreEqual(1, calls);
            Assert.IsFalse(animator.PresentationState.IsActionActive);
            Assert.AreSame(idleClip, animator.CurrentClip, "Caller failure must not strand the cancelled clip.");
            animator.CancelCurrentAction();
            yield return null;
            Assert.AreEqual(1, calls);
            Assert.AreSame(idleClip, animator.CurrentClip);
        }

        [UnityTest]
        public IEnumerator SameFrameCancellationPreservesATerminalPoseStartedByItsCallback()
        {
            var animator = CreateBoundAnimator();
            yield return null;
            animator.RequestAction(AttackCue, _ => animator.RequestTerminal(DeathCue));
            animator.CancelCurrentAction();
            animator.CancelCurrentAction();
            Assert.IsTrue(animator.PresentationState.IsTerminal);
            Assert.AreSame(deathClip, animator.CurrentClip);
            yield return null;
            Assert.AreSame(deathClip, animator.CurrentClip);
        }

        [UnityTest]
        public IEnumerator BaselineResumesAfterRecoveringFromTerminal()
        {
            var animator = CreateBoundAnimator();
            yield return null;

            animator.RequestTerminal(DeathCue);
            yield return null;
            Assert.AreSame(deathClip, animator.CurrentClip);

            animator.Recover();
            Assert.IsFalse(animator.PresentationState.IsTerminal);

            yield return null; // forced baseline replay on the unmask tick

            Assert.AreSame(idleClip, animator.CurrentClip, "Baseline must resume idle after Recover(), not stay on the death pose.");
        }

        [UnityTest]
        public IEnumerator ModelRebindDuringAnInFlightActionCancelsItExactlyOnceAndResumesBaseline()
        {
            var animator = CreateBoundAnimator();
            yield return null;

            var outcome = (AnimationOutcome?)null;
            var token = animator.RequestAction(AttackCue, o => outcome = o);
            Assert.IsTrue(token.IsValid);
            yield return null;

            // Model swap while the action is still genuinely in flight (the short attack clip hasn't had time
            // to complete within one frame).
            modelGoB = CreateModel("ModelB");
            animator.BindPresentation(modelGoB.GetComponent<Animator>(), null);

            Assert.AreEqual(AnimationOutcome.Cancelled, outcome, "The stranded action must be cancelled exactly once by the rebind, not left with no completion route at all.");
            Assert.IsFalse(animator.PresentationState.IsActionActive);

            yield return null;

            Assert.AreSame(idleClip, animator.CurrentClip, "Baseline must resume on the NEW graph after the rebind cancels the stranded action.");
        }

        [UnityTest]
        public IEnumerator ModelRebindWhileTerminalRestoresTheTerminalPoseOnTheNewGraph()
        {
            var animator = CreateBoundAnimator();
            yield return null;

            animator.RequestTerminal(DeathCue);
            yield return null;
            Assert.AreSame(deathClip, animator.CurrentClip);

            modelGoB = CreateModel("ModelB");
            animator.BindPresentation(modelGoB.GetComponent<Animator>(), null);

            Assert.IsTrue(animator.PresentationState.IsTerminal, "Death must survive a Model swap -- a re-skinned character should still look dead.");
            Assert.AreSame(deathClip, animator.CurrentClip, "The terminal pose must be re-established on the NEW graph, not lost.");
        }

        [UnityTest]
        public IEnumerator RequestingAnActionBeforeAnyPresentationIsBoundIsCancelledOnceBoundRatherThanStrandedForever()
        {
            rootGo = new GameObject("CharacterRoot");
            var animator = rootGo.AddComponent<CharacterPlayableAnimator>();
            SetField(animator, "profile", CreateProfile());

            var outcome = (AnimationOutcome?)null;
            animator.RequestAction(AttackCue, o => outcome = o); // no driver bound yet -- state accepts it, driver can't play it
            Assert.IsTrue(animator.PresentationState.IsActionActive);
            Assert.IsNull(outcome, "No outcome yet -- nothing has resolved it.");

            modelGoA = CreateModel("ModelA");
            animator.BindPresentation(modelGoA.GetComponent<Animator>(), null);

            Assert.AreEqual(AnimationOutcome.Cancelled, outcome, "Binding must resolve a pre-bind request too, not leave it stranded forever.");
            Assert.IsFalse(animator.PresentationState.IsActionActive);

            yield return null;
            Assert.AreSame(idleClip, animator.CurrentClip);
        }
    }
}
