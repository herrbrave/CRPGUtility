using NUnit.Framework;
using UnityEngine;
using VikingRaid.Game;

namespace VikingRaid.Tests.EditMode
{
    /// <summary>The pure rules behind the yaw correction and the animation phase spread.</summary>
    public class PresentationFixesTests
    {
        [Test]
        public void TheModelIsTurnedAgainstTheMeasuredOffsetOnlyWhileIdleOrMoving()
        {
            Assert.AreEqual(-30f, ModelYawCorrection.TargetYaw(false, false, true, 0f, 30f), "jogging: cancel the jog's +30");
            Assert.AreEqual(-19f, ModelYawCorrection.TargetYaw(false, false, false, 19f, 31f), "idle: cancel the goblin idle's +19");
            Assert.AreEqual(0f, ModelYawCorrection.TargetYaw(true, false, true, 19f, 31f), "an attack, hit or shot uses the combat clips: no correction");
            Assert.AreEqual(0f, ModelYawCorrection.TargetYaw(false, true, false, 19f, 31f), "a body on the ground: no correction");
        }

        [Test]
        public void PhasesAreSpreadByTheGoldenRatioSoAnyGroupIsWellSpread()
        {
            float Gap(float a, float b) { var d = Mathf.Abs(a - b); return Mathf.Min(d, 1f - d); }
            // characters spawn in order, so the ones on screen together are a consecutive run of indices: any run of up to 8 (4 heroes plus an encounter) must be well spread
            for (var start = 0; start < 20; start++)
                for (var i = start; i < start + 8; i++)
                    for (var j = i + 1; j < start + 8; j++)
                        Assert.Greater(Gap(AnimationPhaseOffset.PhaseForIndex(i), AnimationPhaseOffset.PhaseForIndex(j)), 0.08f, $"{i} and {j} in a run starting at {start}");
            for (var i = 0; i < 14; i++) Assert.That(AnimationPhaseOffset.PhaseForIndex(i), Is.InRange(0f, 1f));
        }

        [Test]
        public void TheCameraCanNowZoomInCloserThanBefore()
        {
            Assert.LessOrEqual(CameraController.MinZoom, 0.3f, "the user asked for a little more zoom");
            Assert.Greater(CameraController.MinZoom, 0.15f, "but not so close the party leaves the frame");
        }
    }
}
