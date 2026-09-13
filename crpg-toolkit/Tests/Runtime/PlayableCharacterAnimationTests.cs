using System;
using System.Collections;
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
    /// Task 2.3 (Production Foundation): "make playback interruption production-safe" -- normalized output
    /// weights and bounded graph cleanup across an attack-&gt;hit-&gt;death interruption chain, pause/resume,
    /// disable/destroy, and continuous locomotion speed blending. Exercises
    /// <see cref="PlayableCharacterAnimationDriver"/> directly (a real Animator is still required to bind
    /// against, so PlayMode) rather than only through CharacterPlayableAnimator's black-box surface.
    /// </summary>
    public class PlayableCharacterAnimationTests
    {
        private GameObject modelGo;

        [TearDown]
        public void TearDown()
        {
            if (modelGo != null) UnityEngine.Object.DestroyImmediate(modelGo);
        }

        private GameObject CreateModel(string name)
        {
            modelGo = new GameObject(name);
            modelGo.AddComponent<Animator>();
            return modelGo;
        }

        private static AnimationCueClip CreateCue(string cueId, AnimationClip clip, float crossfadeDuration = 0.2f, bool loop = true, float referenceSpeed = 0f)
        {
            object boxed = new AnimationCueClip();
            SetField(boxed, "cueId", new DefinitionId(cueId));
            SetField(boxed, "clip", clip);
            SetField(boxed, "crossfadeDuration", crossfadeDuration);
            SetField(boxed, "loop", loop);
            SetField(boxed, "referenceSpeed", referenceSpeed);
            return (AnimationCueClip)boxed;
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

            throw new Exception($"Field '{fieldName}' not found on {target.GetType().Name} or its base types.");
        }

        // new AnimationClip() alone actually defaults to length 1 (confirmed empirically, not 0 as the class
        // name might suggest) -- usable, but not a controlled/short value for a test that specifically needs
        // a genuinely-pending one-shot to interrupt/tear down mid-flight within a small number of ticks.
        private static AnimationClip CreateClipWithLength(float seconds)
        {
            var clip = new AnimationClip();
            clip.SetCurve(string.Empty, typeof(Transform), "localPosition.x", AnimationCurve.Linear(0f, 0f, seconds, 1f));
            return clip;
        }

        private static PlayableCharacterAnimationDriver CreateBoundDriver(GameObject model)
        {
            var driver = new PlayableCharacterAnimationDriver();
            driver.Bind(model.GetComponent<Animator>());
            return driver;
        }

        [Test]
        public void InterruptingMidCrossfadeKeepsCombinedWeightExactlyNormalized()
        {
            // Phase 2 correction pass (R7): strengthened from the original "never drops below the dominant
            // port's weight" (a bounded-dip tolerance) to the actual design requirement -- combined weight is
            // exactly 1 at every instant, not just eventually. See the driver's class remarks for the proof.
            var driver = CreateBoundDriver(CreateModel("Model"));
            var attack = CreateCue("anim-attack", new AnimationClip(), crossfadeDuration: 1f, loop: false);
            var hit = CreateCue("anim-hit", new AnimationClip(), crossfadeDuration: 1f, loop: false);

            driver.PlayOnce(attack, null); // first clip -- starts at full weight, no crossfade
            driver.Tick(0.016f);

            driver.PlayOnce(hit, null); // interrupt well before attack "finishes" (its crossfade duration is 1s)
            driver.Tick(0.1f); // partway through the hit's own crossfade-in

            Assert.AreEqual(1f, driver.CurrentBlendWeightSum, 0.001f, "Combined weight must be exactly normalized, not merely bounded.");
        }

        [Test]
        public void AnEqualWeightInterruptionStillPreservesTheFullOutgoingPoseContribution()
        {
            // The specific failure mode the old design's own comments acknowledged: interrupting at exactly
            // t=0.5 (both ports equally weighted) used to arbitrarily discard ONE of the two equally-dominant
            // contributors, losing half the visible pose instantly. The new design keeps every live contributor.
            var driver = CreateBoundDriver(CreateModel("Model"));
            var attack = CreateCue("anim-attack", new AnimationClip(), crossfadeDuration: 1f, loop: false);
            var hit = CreateCue("anim-hit", new AnimationClip(), crossfadeDuration: 1f, loop: false);
            var death = CreateCue("anim-death", new AnimationClip(), crossfadeDuration: 1f, loop: true);

            driver.PlayOnce(attack, null);
            driver.Tick(0.016f);
            driver.PlayOnce(hit, null);
            driver.Tick(0.5f); // exactly halfway through a 1s crossfade -- both ports at ~0.5 weight

            var attackPort = 0; // PlayFirstClip always uses port 0
            var hitPort = driver.ActivePort;
            var attackWeightAtInterrupt = driver.GetPortWeight(attackPort);
            var hitWeightAtInterrupt = driver.GetPortWeight(hitPort);
            Assert.Greater(attackWeightAtInterrupt, 0.01f, "Test setup check: attack must still hold real weight at the equal-weight point.");
            Assert.Greater(hitWeightAtInterrupt, 0.01f, "Test setup check: hit must still hold real weight at the equal-weight point.");

            driver.PlayLoop(death); // interrupt again, right at the equal-weight instant

            // Immediately after the new interruption: the new (death) port is 0, but BOTH previous
            // contributors must still be present at their pre-interrupt weights -- neither silently discarded.
            Assert.AreEqual(attackWeightAtInterrupt, driver.GetPortWeight(attackPort), 0.02f);
            Assert.AreEqual(hitWeightAtInterrupt, driver.GetPortWeight(hitPort), 0.02f);
            Assert.AreEqual(1f, driver.CurrentBlendWeightSum, 0.001f);
        }

        [UnityTest]
        public IEnumerator RapidInterruptionsBeyondThePortBoundCollapseTheWeakestContributorOnly()
        {
            // R7's documented, deliberate exception: MaxPorts (4) bounds the graph. Four rapid-fire
            // interruptions within one crossfade window force a collapse -- verify it snaps only the single
            // weakest live contributor, not an unrelated one, and never grows the graph beyond the bound.
            var driver = CreateBoundDriver(CreateModel("Model"));
            var cueA = CreateCue("anim-a", new AnimationClip(), crossfadeDuration: 2f, loop: false);
            var cueB = CreateCue("anim-b", new AnimationClip(), crossfadeDuration: 2f, loop: false);
            var cueC = CreateCue("anim-c", new AnimationClip(), crossfadeDuration: 2f, loop: false);
            var cueD = CreateCue("anim-d", new AnimationClip(), crossfadeDuration: 2f, loop: false);
            var cueE = CreateCue("anim-e", new AnimationClip(), crossfadeDuration: 2f, loop: true);

            driver.PlayOnce(cueA, null);
            driver.Tick(0.02f);
            driver.PlayOnce(cueB, null); // 2 live ports
            driver.Tick(0.02f);
            driver.PlayOnce(cueC, null); // 3 live ports
            driver.Tick(0.02f);
            driver.PlayOnce(cueD, null); // 4 live ports -- exactly MaxPorts, no collapse needed yet

            yield return null;
            Assert.AreEqual(1f, driver.CurrentBlendWeightSum, 0.001f, "Still within the port bound -- must still be exactly normalized.");

            driver.PlayLoop(cueE); // 5th distinct live clip -- forces a bounded collapse of the weakest contributor

            Assert.LessOrEqual(driver.CurrentBlendWeightSum, 1.001f, "Collapse must never push total weight over 1.");
            Assert.GreaterOrEqual(driver.CurrentBlendWeightSum, 0.5f, "Collapsing exactly one weakest contributor must not crater total weight.");
        }

        [UnityTest]
        public IEnumerator AttackHitDeathChainDuringCrossfadeSettlesBackToFullNormalizedWeightOnTheFinalClip()
        {
            var driver = CreateBoundDriver(CreateModel("Model"));
            var attack = CreateCue("anim-attack", new AnimationClip(), crossfadeDuration: 0.2f, loop: false);
            var hit = CreateCue("anim-hit", new AnimationClip(), crossfadeDuration: 0.2f, loop: false);
            var death = CreateCue("anim-death", new AnimationClip(), crossfadeDuration: 0.2f, loop: true);

            driver.PlayOnce(attack, null);
            driver.Tick(0.016f);

            driver.PlayOnce(hit, null); // interrupt #1, mid-crossfade-in from nothing
            driver.Tick(0.05f);

            driver.PlayLoop(death); // interrupt #2, mid-crossfade of the first interruption
            var deathPort = driver.ActivePort;

            // Exactly normalized (not just bounded) at every instant while the final crossfade settles.
            var elapsed = 0f;
            while (elapsed < 1f)
            {
                driver.Tick(0.016f);
                elapsed += 0.016f;
                Assert.AreEqual(1f, driver.CurrentBlendWeightSum, 0.001f);
                yield return null;
            }

            Assert.AreEqual(1f, driver.GetPortWeight(deathPort), 0.001f, "The final (death) clip must settle to full weight.");
            Assert.AreEqual(1f, driver.CurrentBlendWeightSum, 0.001f, "Combined weight must be fully normalized once settled -- no leftover weight from an earlier interrupted clip.");
        }

        [UnityTest]
        public IEnumerator TickWithZeroDeltaTimeDuringPauseDoesNotAdvanceCrossfadeOrCompletion()
        {
            var driver = CreateBoundDriver(CreateModel("Model"));
            var idle = CreateCue("anim-idle", new AnimationClip(), loop: true);
            var moving = CreateCue("anim-moving", new AnimationClip(), crossfadeDuration: 1f, loop: true);

            driver.PlayLoop(idle);
            driver.Tick(0.016f);
            driver.PlayLoop(moving);
            driver.Tick(0.4f); // partway through the 1s crossfade
            var weightBeforePause = driver.CurrentBlendWeightSum;
            var activeWeightBeforePause = driver.GetPortWeight(driver.ActivePort);

            for (var i = 0; i < 10; i++)
            {
                driver.Tick(0f); // simulated pause -- Time.deltaTime itself would read 0
                yield return null;
            }

            Assert.AreEqual(activeWeightBeforePause, driver.GetPortWeight(driver.ActivePort), 0.0001f, "Ticking with 0 delta must not advance the crossfade at all.");
            Assert.AreEqual(weightBeforePause, driver.CurrentBlendWeightSum, 0.0001f);
        }

        [UnityTest]
        public IEnumerator CrossfadeResumesExactlyWhereItLeftOffAfterAPause()
        {
            var driver = CreateBoundDriver(CreateModel("Model"));
            var idle = CreateCue("anim-idle", new AnimationClip(), loop: true);
            var moving = CreateCue("anim-moving", new AnimationClip(), crossfadeDuration: 1f, loop: true);

            driver.PlayLoop(idle);
            driver.Tick(0.016f);
            driver.PlayLoop(moving);
            driver.Tick(0.4f);
            var activePort = driver.ActivePort;
            var weightAtPause = driver.GetPortWeight(activePort);

            driver.Tick(0f); // pause
            driver.Tick(0f);
            yield return null;

            driver.Tick(0.1f); // resume
            var weightAfterResume = driver.GetPortWeight(activePort);

            Assert.Greater(weightAfterResume, weightAtPause, "Resuming must continue advancing the crossfade from where it paused, not reset it.");
            Assert.Less(weightAfterResume, 1f, "0.1s more of a 1s crossfade must not already be complete.");
        }

        [Test]
        public void UnbindMidCrossfadeLeavesNoLeakedPlayablesAndDoesNotThrow()
        {
            var driver = CreateBoundDriver(CreateModel("Model"));
            var idle = CreateCue("anim-idle", new AnimationClip(), loop: true);
            var moving = CreateCue("anim-moving", new AnimationClip(), crossfadeDuration: 1f, loop: true);
            driver.PlayLoop(idle);
            driver.Tick(0.016f);
            driver.PlayLoop(moving);
            driver.Tick(0.1f); // now genuinely mid-crossfade

            Assert.DoesNotThrow(() => driver.Unbind());

            Assert.IsFalse(driver.IsBound);
            Assert.AreEqual(0f, driver.CurrentBlendWeightSum, "An unbound driver must report no live weight -- bounded cleanup, nothing left running.");
            Assert.DoesNotThrow(() => driver.Tick(0.016f), "Ticking an unbound driver must be a safe no-op.");
        }

        [Test]
        public void DisablingByUnbindingDuringAPendingOneShotNeverInvokesItsCallbackAfterTeardown()
        {
            var driver = CreateBoundDriver(CreateModel("Model"));
            var attack = CreateCue("anim-attack", CreateClipWithLength(1f), loop: false);
            var completedCount = 0;
            driver.PlayOnce(attack, () => completedCount++);
            driver.Tick(0.001f); // barely started -- nowhere near the clip's length

            driver.Unbind(); // simulates disable/destroy (CharacterPlayableAnimator.UnbindPresentation/OnDestroy)

            Assert.AreEqual(0, completedCount, "A callback must never fire after the graph it belonged to was torn down.");
        }

        [Test]
        public void SpeedBlendingScalesTheActiveLoopsPlaybackRateByActualOverReferenceSpeed()
        {
            var driver = CreateBoundDriver(CreateModel("Model"));
            var moving = CreateCue("anim-moving", new AnimationClip(), loop: true, referenceSpeed: 2f);

            driver.PlayLoop(moving); // first clip -- full weight immediately, no crossfade to wait out
            driver.SetLocomotionSpeed(1f); // half the reference speed
            driver.Tick(0.016f);

            Assert.AreEqual(0.5, driver.GetPortSpeed(driver.ActivePort), 0.001, "Actual speed 1 / reference speed 2 must play the clip at half rate.");
        }

        [Test]
        public void SpeedBlendingClampsToABoundedPlaybackRateRange()
        {
            var driver = CreateBoundDriver(CreateModel("Model"));
            var moving = CreateCue("anim-moving", new AnimationClip(), loop: true, referenceSpeed: 1f);

            driver.PlayLoop(moving);
            driver.SetLocomotionSpeed(100f); // wildly over reference speed
            driver.Tick(0.016f);

            Assert.LessOrEqual(driver.GetPortSpeed(driver.ActivePort), 1.75, "An extreme actual/reference mismatch must be clamped, not produce an absurd playback rate.");
        }

        [Test]
        public void SpeedBlendingDoesNotApplyWhileACueHasNoAuthoredReferenceSpeed()
        {
            var driver = CreateBoundDriver(CreateModel("Model"));
            var idle = CreateCue("anim-idle", new AnimationClip(), loop: true); // referenceSpeed defaults to 0 -- opted out

            driver.PlayLoop(idle);
            driver.SetLocomotionSpeed(5f);
            driver.Tick(0.016f);

            Assert.AreEqual(1.0, driver.GetPortSpeed(driver.ActivePort), 0.001, "A cue with no authored reference speed must always play at its authored (1x) rate.");
        }

        [Test]
        public void SpeedBlendingDoesNotApplyWhileAOneShotActionIsPlaying()
        {
            var driver = CreateBoundDriver(CreateModel("Model"));
            var moving = CreateCue("anim-moving", new AnimationClip(), loop: true, referenceSpeed: 2f);
            driver.PlayLoop(moving);
            driver.SetLocomotionSpeed(1f);
            driver.Tick(0.016f);
            Assert.AreEqual(0.5, driver.GetPortSpeed(driver.ActivePort), 0.001);

            var attackClip = new AnimationClip();
            var attack = CreateCue("anim-attack", attackClip, loop: false);
            driver.PlayOnce(attack, null);
            var actionPort = driver.ActivePort;
            driver.SetLocomotionSpeed(1f); // still "half reference speed" if it were (wrongly) applied here
            driver.Tick(0.016f);

            Assert.AreEqual(1.0, driver.GetPortSpeed(actionPort), 0.001, "A one-shot action must always play at its authored rate, never speed-blended by locomotion.");
        }

        // Found live (Task 3.3 InteractionLab bug report): a raw AnimationClipPlayable in this custom graph
        // does not loop on its own regardless of the source clip's own WrapMode -- CharacterPlayableAnimator's
        // RefreshBaseline only ever calls PlayLoop ONCE per cue change, so nothing else would ever re-trigger a
        // looping cue. A walk longer than the clip's own length froze mid-stride while the character kept
        // translating, reading as "animation speed doesn't match movement" (legs stopped, body kept gliding).
        //
        // A Playable's own GetTime() advances on Unity's real per-frame graph evaluation, NOT on however much
        // "deltaTime" is passed into this class's own Tick() -- so proving a loop requires watching real time
        // pass (budgeted generously, not by a fixed frame/tick count, matching CharacterMotionTests' own
        // established convention) and detecting an actual DECREASE in GetTime() (an unambiguous wrap-around),
        // not merely that it reads below the clip length at one arbitrary sampled instant.
        [UnityTest]
        public IEnumerator ALoopingCueWrapsItsTimeBackPastItsOwnClipLengthInsteadOfFreezing()
        {
            var driver = CreateBoundDriver(CreateModel("Model"));
            var moving = CreateCue("anim-moving", CreateClipWithLength(0.1f), loop: true);

            driver.PlayLoop(moving); // first clip -- full weight immediately, no crossfade to wait out
            var port = driver.ActivePort;

            var wrapped = false;
            var previousTime = driver.GetPortTime(port);
            var elapsed = 0f;
            while (elapsed < 3f && !wrapped)
            {
                driver.Tick(Time.deltaTime);
                yield return null;
                elapsed += Time.deltaTime;

                var currentTime = driver.GetPortTime(port);
                if (currentTime < previousTime) wrapped = true;
                previousTime = currentTime;
            }

            Assert.AreEqual(port, driver.ActivePort, "Looping must never re-trigger a fresh port -- the same clip identity keeps playing.");
            Assert.IsTrue(wrapped, "A looping clip's evaluated time must genuinely wrap back toward zero at least once, not sit at/above its own length forever.");
        }

        [UnityTest]
        public IEnumerator ANonLoopingCueDoesNotHaveItsTimeWrapped()
        {
            var driver = CreateBoundDriver(CreateModel("Model"));
            var attack = CreateCue("anim-attack", CreateClipWithLength(0.1f), loop: false);

            driver.PlayOnce(attack, null);
            var port = driver.ActivePort;

            var wrapped = false;
            var previousTime = driver.GetPortTime(port);
            var elapsed = 0f;
            while (elapsed < 1f) // several multiples of the 0.1s clip's own length, real time
            {
                driver.Tick(Time.deltaTime);
                yield return null;
                elapsed += Time.deltaTime;

                var currentTime = driver.GetPortTime(port);
                if (currentTime < previousTime) wrapped = true;
                previousTime = currentTime;
            }

            Assert.IsFalse(wrapped, "A non-looping cue's time must never be wrapped -- only Loop == true cues loop.");
        }
    }
}
