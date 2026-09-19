using CRPG.Presentation.Sprites;
using NUnit.Framework;
using UnityEngine;

namespace CRPG.Tests.EditMode
{
    /// <summary>
    /// 2D campaign Task 2.1: RockingMotionState -- the Gate-A-approved rock as pure logic. Includes the Opus review's
    /// cases: leg gaps bridged by the coast, a real stop never pinned mid-lean, and a tick-for-tick oracle of the
    /// approved spike (BarrowsEnd/Assets/Spike/SpikeCharacter.cs) so the feel cannot drift silently.
    /// </summary>
    public class RockingMotionStateTests
    {
        private const float Frame = 1f / 60f;
        private const float Eps = 1e-4f;

        private static void Run(RockingMotionState state, float speed, float seconds, float dt = Frame)
        {
            var frames = Mathf.RoundToInt(seconds / dt);
            for (var i = 0; i < frames; i++) state.Tick(speed, dt);
        }

        private static RockingMotionState Cruising(float speed = 2f)
        {
            var state = new RockingMotionState();
            Run(state, speed, 1f);
            Assert.AreEqual(1f, state.Amplitude, Eps, "test setup: fully rocking");
            return state;
        }

        [Test]
        public void IdleStaysExactlyZero()
        {
            var state = new RockingMotionState();
            Run(state, 0f, 2f);
            Assert.AreEqual(0f, state.TiltDegrees);
            Assert.AreEqual(0f, state.Amplitude);
            Assert.AreEqual(0f, state.Phase);
            Assert.IsFalse(state.IsMoving);
        }

        [Test]
        public void MovingRampsAmplitudeAtTheRiseRate()
        {
            var state = new RockingMotionState();
            for (var i = 0; i < 3; i++) state.Tick(2f, Frame);
            Assert.AreEqual(3 * 8f * Frame, state.Amplitude, Eps);
        }

        [Test]
        public void TheStartEdgeResetsThenAdvancesInTheSameTick()
        {
            var state = new RockingMotionState();
            state.Tick(2f, Frame);
            Assert.AreEqual(2f * Frame / 1f, state.Phase, Eps, "phase = speed*dt/stride, not discarded by the reset");
            Assert.AreEqual(8f * Frame, state.Amplitude, Eps);
        }

        [Test]
        public void PhaseAdvancesWithDistanceNotTime()
        {
            var slow = new RockingMotionState();
            Run(slow, 1f, 0.6f, 0.01f);
            var fast = new RockingMotionState();
            Run(fast, 2f, 0.3f, 0.01f);
            Assert.AreEqual(slow.Phase, fast.Phase, 1e-3f);

            var longStride = new RockingMotionState(new RockingMotionSettings { strideLength = 2f });
            Run(longStride, 1f, 0.6f, 0.01f);
            Assert.AreEqual(slow.Phase / 2f, longStride.Phase, 1e-3f);
        }

        [Test]
        public void ARealStopRocksOnAtMostTheCoastThenEasesToLevelWithNoPinnedLean()
        {
            var settings = new RockingMotionSettings();
            var state = Cruising();

            var lastPhase = state.Phase;
            var stationaryTicks = 0;
            var previousAmplitude = state.Amplitude;
            var maxSeconds = settings.coastSeconds + 1f / settings.amplitudeFallRate + 0.1f;
            var t = 0f;
            var amplitudeStartedFalling = false;
            while (t < maxSeconds && (state.Amplitude > 0f || state.IsMoving))
            {
                state.Tick(0f, Frame);
                t += Frame;
                if (Mathf.Approximately(state.Phase, lastPhase) && state.IsMoving) stationaryTicks++;
                else stationaryTicks = 0;
                Assert.LessOrEqual(stationaryTicks, 1, "the phase must not sit still while the state still holds full amplitude (a pinned lean)");
                if (state.Amplitude < previousAmplitude) amplitudeStartedFalling = true;
                if (amplitudeStartedFalling) Assert.LessOrEqual(state.Amplitude, previousAmplitude + Eps, "amplitude never rises again after a real stop");
                lastPhase = state.Phase;
                previousAmplitude = state.Amplitude;
            }

            Assert.IsFalse(state.IsMoving);
            Assert.AreEqual(0f, state.Amplitude, "settles to exactly level");
            Assert.AreEqual(0f, state.TiltDegrees);
            Assert.Less(t, maxSeconds, "settled within coast + fall time");
        }

        [Test]
        public void SpeedInsideTheHysteresisBandNeitherStartsNorStops()
        {
            var inBand = 0.07f; // between stopSpeed 0.05 and startSpeed 0.10

            var stopped = new RockingMotionState();
            Run(stopped, inBand, 1f);
            Assert.IsFalse(stopped.IsMoving);
            Assert.AreEqual(0f, stopped.Amplitude);

            var moving = Cruising();
            Run(moving, inBand, 2f); // far longer than any coast
            Assert.IsTrue(moving.IsMoving, "in-band speed keeps a moving character moving");
            Assert.AreEqual(1f, moving.Amplitude, Eps);
        }

        [TestCase(1)]
        [TestCase(3)]
        public void ALegGapOfAFewFramesIsBridged(int gapFrames)
        {
            var withGap = Cruising();
            var noGap = Cruising();

            for (var i = 0; i < gapFrames; i++)
            {
                withGap.Tick(0f, Frame);
                noGap.Tick(2f, Frame);
                Assert.IsTrue(withGap.IsMoving, $"still moving {i + 1} frames into the gap");
                Assert.IsTrue(withGap.IsCoasting);
                Assert.AreEqual(1f, withGap.Amplitude, Eps, "no amplitude dip across the gap");
                Assert.AreEqual(noGap.Phase, withGap.Phase, Eps, "phase carries at the last real speed");
                Assert.AreEqual(noGap.TiltDegrees, withGap.TiltDegrees, Eps, "the rock is continuous across the gap");
            }

            withGap.Tick(2f, Frame);
            Assert.IsFalse(withGap.IsCoasting);
        }

        [Test]
        public void AHalfSecondZeroSpeedGapStopsTheRock()
        {
            var state = Cruising();
            Run(state, 0f, 0.5f);
            Assert.IsFalse(state.IsMoving);
            Assert.Less(state.Amplitude, 1f);
        }

        [Test]
        public void TheCoastHasAFrameFloorForSlowFrameRates()
        {
            var state = Cruising();
            state.Tick(0f, 0.1f);
            state.Tick(0f, 0.1f); // 0.2 s of gap: long enough in time, but only 2 frames
            Assert.IsTrue(state.IsMoving, "2 frames is still a leg gap however long each frame was");
            state.Tick(0f, 0.1f);
            state.Tick(0f, 0.1f);
            Assert.IsFalse(state.IsMoving, "4 frames and 0.4 s: a real stop");
        }

        [Test]
        public void ANonZeroBlipInsideAGapResetsTheCoastTimer()
        {
            var state = Cruising();
            Run(state, 0f, 0.15f);
            Assert.IsTrue(state.IsMoving);
            state.Tick(2f, Frame);
            Run(state, 0f, 0.15f);
            Assert.IsTrue(state.IsMoving, "two 0.15 s gaps separated by a blip do not add up to a stop");
        }

        [Test]
        public void CoastStateIsClearedByAStopSoTheNextMoveIsNotCutShort()
        {
            var state = Cruising();
            Run(state, 0f, 1f);
            Assert.IsFalse(state.IsMoving, "test setup: the coast expired and it stopped");

            state.Tick(2f, Frame); // the restart tick itself: no "moving" tick has cleared anything yet
            Assert.IsTrue(state.IsMoving);
            state.Tick(0f, Frame);
            Assert.IsTrue(state.IsMoving, "a single zero-speed frame right after a restart must not stop it (stale coast state)");
        }

        [Test]
        public void ResetMidCoastClearsEverything()
        {
            var state = Cruising();
            state.Tick(0f, Frame);
            Assert.IsTrue(state.IsCoasting);

            state.Reset();

            Assert.IsFalse(state.IsMoving);
            Assert.IsFalse(state.IsCoasting);
            Assert.AreEqual(0f, state.Amplitude);
            Assert.AreEqual(0f, state.Phase);
            Assert.AreEqual(0f, state.TiltDegrees);
        }

        [Test]
        public void RestartingBeforeTheAmplitudeHasEasedOutContinuesFromTheCurrentPhaseWithoutAPop()
        {
            var state = Cruising();
            Run(state, 0f, 0.3f); // stopped (coast expired), amplitude partly eased out
            var amplitudeBefore = state.Amplitude;
            Assert.Greater(amplitudeBefore, 0f);
            Assert.IsFalse(state.IsMoving);
            var tiltBefore = state.TiltDegrees;
            var phaseBefore = state.Phase;

            state.Tick(2f, Frame);

            Assert.AreEqual(phaseBefore + 2f * Frame, state.Phase, 1e-3f, "phase is not reset while the sprite is still leaning");
            Assert.Less(Mathf.Abs(state.TiltDegrees - tiltBefore), 1f, "no visible pop at the restart");
        }

        [Test]
        public void ZeroNegativeAndNaNDeltaTimeAreNoOps()
        {
            var state = Cruising();
            var phase = state.Phase;
            var amplitude = state.Amplitude;

            state.Tick(2f, 0f);
            state.Tick(2f, -1f);
            state.Tick(2f, float.NaN);

            Assert.AreEqual(phase, state.Phase);
            Assert.AreEqual(amplitude, state.Amplitude);
            Assert.IsFalse(float.IsNaN(state.TiltDegrees));
        }

        [Test]
        public void NonFiniteAndNegativeSpeedAreTreatedAsZero()
        {
            foreach (var bad in new[] { float.NaN, float.PositiveInfinity, float.NegativeInfinity, -3f })
            {
                var state = new RockingMotionState();
                Run(state, bad, 1f);
                Assert.IsFalse(state.IsMoving, $"speed {bad} never starts a rock");
                Assert.AreEqual(0f, state.TiltDegrees);

                var cruising = Cruising();
                cruising.Tick(bad, Frame);
                Assert.IsFalse(float.IsNaN(cruising.TiltDegrees), $"speed {bad} never produces a NaN tilt");
                Assert.IsFalse(float.IsNaN(cruising.Phase));
            }
        }

        [Test]
        public void AHitchFrameIsClamped()
        {
            var state = new RockingMotionState();
            state.Tick(2f, 5f);
            Assert.AreEqual(2f * 0.1f, state.Phase, Eps, "a 5 s hitch advances at most 0.1 s worth");
            Assert.AreEqual(0.8f, state.Amplitude, Eps, "and eases at most 0.1 s worth (8/s * 0.1 s)");
        }

        [Test]
        public void GarbageSettingsStillBehave()
        {
            var settings = new RockingMotionSettings
            {
                peakDegrees = -5f, strideLength = 0f, startSpeed = 0f, stopSpeed = 9f, coastSeconds = -1f,
                coastMinFrames = -3, amplitudeRiseRate = 0f, amplitudeFallRate = float.NaN,
            };

            var idle = new RockingMotionState(settings);
            Run(idle, 0f, 2f);
            Assert.IsFalse(idle.IsMoving, "startSpeed 0 must not make an idle character permanently moving");
            Assert.AreEqual(0f, idle.TiltDegrees);

            var walking = new RockingMotionState(settings);
            for (var i = 0; i < 1000; i++)
            {
                walking.Tick(i % 100 < 50 ? 2f : 0f, Frame);
                Assert.IsTrue(float.IsFinite(walking.TiltDegrees) && float.IsFinite(walking.Phase) && float.IsFinite(walking.Amplitude));
            }
        }

        [Test]
        public void AllZeroSettingsBehaveLikeDefaults()
        {
            var zero = new RockingMotionSettings
            {
                peakDegrees = 0f, strideLength = 0f, startSpeed = 0f, stopSpeed = 0f, coastSeconds = 0f,
                coastMinFrames = 0, amplitudeRiseRate = 0f, amplitudeFallRate = 0f,
            };
            var state = new RockingMotionState(zero);
            Run(state, 0f, 1f);
            Assert.IsFalse(state.IsMoving);

            Run(state, 2f, 1f);
            Assert.IsTrue(state.IsMoving);
            Assert.AreEqual(1f, state.Amplitude, Eps);
        }

        [Test]
        public void MatchesTheApprovedSpikeTickForTick()
        {
            // The five lines of SpikeCharacter.Update, ported as an oracle (peak 6, stride 1, rise 8, fall 6, moving = speed > 0.1).
            float amplitude = 0f, phase = 0f;
            var state = new RockingMotionState();
            var speeds = new[] { 2f, 2f, 2f, 1.3f, 1.3f, 3f, 3f, 3f };

            for (var i = 0; i < 240; i++)
            {
                var speed = speeds[(i / 30) % speeds.Length];
                const float dt = Frame;
                var moving = speed > 0.1f;
                amplitude = Mathf.MoveTowards(amplitude, moving ? 1f : 0f, dt * (moving ? 8f : 6f));
                if (moving) phase += speed * dt / 1f;
                var oracleTilt = Mathf.Sin(2f * Mathf.PI * phase) * 6f * amplitude;

                state.Tick(speed, dt);

                Assert.AreEqual(oracleTilt, state.TiltDegrees, 1e-3f, $"tick {i}");
            }
        }

        [Test]
        public void TiltNeverExceedsThePeakAndReachesItAtFullAmplitude()
        {
            var settings = new RockingMotionSettings();
            var state = new RockingMotionState(settings);
            var max = 0f;
            for (var i = 0; i < 3 * 240; i++)
            {
                state.Tick(2f, 1f / 240f);
                Assert.LessOrEqual(Mathf.Abs(state.TiltDegrees), settings.peakDegrees + Eps);
                max = Mathf.Max(max, Mathf.Abs(state.TiltDegrees));
            }

            Assert.AreEqual(settings.peakDegrees, max, settings.peakDegrees * 0.01f, "within 1% of the peak over three strides");
        }
    }
}
