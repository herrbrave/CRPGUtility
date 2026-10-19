using CRPG.Camera;
using NUnit.Framework;
using UnityEngine;

namespace CRPG.Tests.EditMode
{
    /// <summary>The perspective camera feel: scrolling up zooms IN (the wheel delta is mapped per rig kind), the pitch lowers as the camera comes in from the default zoom to the minimum (a close-up looks more level at the characters), and a mouse drag rotates by exact degrees.</summary>
    public class CameraZoomPitchTests
    {
        private static CRPGCameraSettings Settings(float closeUpPitchOffset)
        {
            var s = ScriptableObject.CreateInstance<CRPGCameraSettings>();
            TestDefinitionFactory.SetPrivateField(s, "closeUpPitchOffset", closeUpPitchOffset);
            TestDefinitionFactory.SetPrivateField(s, "minimumZoom", 8f); TestDefinitionFactory.SetPrivateField(s, "maximumZoom", 38f); TestDefinitionFactory.SetPrivateField(s, "defaultZoom", 17f);
            TestDefinitionFactory.SetPrivateField(s, "defaultPitch", 58f); TestDefinitionFactory.SetPrivateField(s, "minimumPitch", 30f); TestDefinitionFactory.SetPrivateField(s, "maximumPitch", 70f);
            return s;
        }

        [Test]
        public void WithNoOffsetThePitchNeverMovesWithTheZoom()
        {
            var rig = new CameraRigState(Settings(0f));
            foreach (var zoom in new[] { 8f, 12f, 17f, 30f }) { rig.SetZoom(zoom); Assert.AreEqual(58f, rig.Pitch, 1e-4f, "zoom " + zoom); }
        }

        [Test]
        public void ZoomingInLowersThePitchFromTheDefaultZoomToTheFullOffsetAtTheMinimum()
        {
            var rig = new CameraRigState(Settings(-20f));
            rig.SetZoom(17f); Assert.AreEqual(58f, rig.Pitch, 1e-4f, "at the default zoom: the base pitch");
            rig.SetZoom(30f); Assert.AreEqual(58f, rig.Pitch, 1e-4f, "farther out: still the base pitch");
            rig.SetZoom(8f); Assert.AreEqual(38f, rig.Pitch, 1e-4f, "fully zoomed in: the full offset");
            rig.SetZoom(12.5f); Assert.That(rig.Pitch, Is.GreaterThan(38f).And.LessThan(58f), "in between");
            var previous = 100f;
            foreach (var zoom in new[] { 17f, 15f, 13f, 11f, 9f, 8f }) { rig.SetZoom(zoom); Assert.LessOrEqual(rig.Pitch, previous + 1e-4f, "monotonic as it comes in"); previous = rig.Pitch; }
        }

        [Test]
        public void TheClosePitchIsClampedToThePitchLimits()
        {
            var rig = new CameraRigState(Settings(-60f)); rig.SetZoom(8f);
            Assert.AreEqual(30f, rig.Pitch, 1e-4f, "never below the minimum pitch");
            Assert.AreEqual(58f, rig.BasePitch, 1e-4f);
        }

        [Test]
        public void ScrollingUpZoomsInOnAPerspectiveRigAndStepsInOnAnOrthographicOne()
        {
            Assert.AreEqual(-1f, CameraRigState.WheelToZoomDelta(false, 1f)); Assert.AreEqual(1f, CameraRigState.WheelToZoomDelta(false, -1f));
            Assert.AreEqual(1f, CameraRigState.WheelToZoomDelta(true, 1f), "the 2D pixel rig already steps in on a positive delta");
            Assert.AreEqual(-1f, CameraRigState.WheelToZoomDelta(false, 120f), "a Windows notch (120) is ONE notch, not a fling"); Assert.AreEqual(1f, CameraRigState.WheelToZoomDelta(false, -120f)); Assert.AreEqual(-3f, CameraRigState.WheelToZoomDelta(false, 2400f), "a burst is capped at three notches");
            var rig = new CameraRigState(Settings(0f)); var before = rig.Zoom;
            rig.ApplyZoom(CameraRigState.WheelToZoomDelta(false, 1f));
            Assert.Less(rig.Zoom, before, "scrolling up brought the camera closer");
        }

        [Test]
        public void ADragRotatesByExactDegreesAndWraps()
        {
            var rig = new CameraRigState(Settings(0f));
            rig.RotateByDegrees(30f); Assert.AreEqual(30f, rig.Yaw, 1e-4f);
            rig.RotateByDegrees(-40f); Assert.AreEqual(350f, rig.Yaw, 1e-3f);
            rig.RotateByDegrees(725f); Assert.AreEqual(355f, rig.Yaw, 1e-3f);   // 350 + 725 = 1075 = 2 turns + 355
        }
    }
}
