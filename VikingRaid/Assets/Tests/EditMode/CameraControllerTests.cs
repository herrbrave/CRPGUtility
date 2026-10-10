using NUnit.Framework;
using UnityEngine;
using VikingRaid.Game;

namespace VikingRaid.Tests.EditMode
{
    /// <summary>The camera's control rules (W/A/S/D pan, Q/E rotate, wheel zoom, Space recenter): pan is relative to the view, panning detaches the camera until Recenter, a fight clears it.</summary>
    public class CameraControllerTests
    {
        private static readonly Vector3 Start = new(0f, 0f, 0f);
        private const float AlongX = 90f;   // yaw 90 degrees looks along +X (the road's first leg)

        private static CameraInput Pan(float x, float y) => new() { Pan = new Vector2(x, y) };

        [Test]
        public void PanningForwardMovesAlongTheViewAndPanningRightMovesAcrossIt()
        {
            var c = new CameraController();
            c.Step(1f, Pan(0, 1), Start, AlongX);
            var forward = c.DesiredFocus(Vector3.one * 999f);
            Assert.AreEqual(CameraController.PanSpeed, forward.x, 0.01f, "W moves along +X when looking along +X");
            Assert.AreEqual(0f, forward.z, 0.01f);

            var d = new CameraController();
            d.Step(1f, Pan(1, 0), Start, AlongX);
            var right = d.DesiredFocus(Vector3.one * 999f);
            Assert.AreEqual(-CameraController.PanSpeed, right.z, 0.01f, "D moves to the camera's right (-Z when looking along +X)");
            Assert.AreEqual(0f, right.x, 0.01f);
        }

        [Test]
        public void PanningDetachesTheCameraFromWhatItFollowsAndStartsFromWhereItWas()
        {
            var c = new CameraController();
            var following = new Vector3(50f, 0f, 0f);
            Assert.AreEqual(following, c.DesiredFocus(following), "not panned: it follows");
            c.Step(0.1f, Pan(0, 1), new Vector3(10f, 0f, 3f), AlongX);
            Assert.IsTrue(c.ManualOverride);
            var focus = c.DesiredFocus(new Vector3(200f, 0f, 0f));
            Assert.AreEqual(10f + CameraController.PanSpeed * 0.1f, focus.x, 0.01f, "the pan starts from the camera's current focus: no jump");
            Assert.AreEqual(3f, focus.z, 0.01f);

            c.Step(0.1f, default, focus, AlongX);
            Assert.AreEqual(focus, c.DesiredFocus(new Vector3(500f, 0f, 0f)), "with no input it stays where the player left it, however far the party walks");
        }

        [Test]
        public void RecenterResumesFollowingAndLooksAlongTheRoadAgain()
        {
            var c = new CameraController();
            c.Step(0.5f, new CameraInput { Pan = Vector2.up, Rotate = 1f }, Start, AlongX);
            Assert.IsTrue(c.ManualOverride); Assert.AreNotEqual(0f, c.YawOffset);

            c.Step(0.01f, new CameraInput { Recenter = true }, Start, AlongX);
            Assert.IsFalse(c.ManualOverride); Assert.AreEqual(0f, c.YawOffset);
            var following = new Vector3(7f, 0f, 1f);
            Assert.AreEqual(following, c.DesiredFocus(following));
        }

        [Test]
        public void RotatingAddsAYawOffsetThatWrapsAndZoomIsClamped()
        {
            var c = new CameraController();
            c.Step(1f, new CameraInput { Rotate = 1f }, Start, AlongX);
            Assert.AreEqual(CameraController.RotateDegreesPerSecond, c.YawOffset, 0.01f);
            for (var i = 0; i < 10; i++) c.Step(1f, new CameraInput { Rotate = 1f }, Start, AlongX);
            Assert.That(c.YawOffset, Is.InRange(-180f, 180f));

            for (var i = 0; i < 100; i++) c.Step(0.01f, new CameraInput { Zoom = 1f }, Start, AlongX);
            Assert.AreEqual(CameraController.MinZoom, c.Zoom, 1e-4f, "scrolling in stops at the closest");
            for (var i = 0; i < 100; i++) c.Step(0.01f, new CameraInput { Zoom = -1f }, Start, AlongX);
            Assert.AreEqual(CameraController.MaxZoom, c.Zoom, 1e-4f, "scrolling out stops at the farthest");
        }

        [Test]
        public void TheCameraCannotBePannedOffIntoTheEndlessForest()
        {
            var c = new CameraController();
            // look along +X from the middle of the first leg and pan hard to the side for a long time
            var focus = WorldLayout.PointAt(30f);
            for (var i = 0; i < 400; i++) { c.Step(0.1f, Pan(1, 0), focus, AlongX); focus = c.DesiredFocus(focus); }
            Assert.LessOrEqual(WorldLayout.EdgeDistance(focus), CameraController.MaxDistanceFromWorld + 1f, "panning stops at the limit");
            Assert.Greater(WorldLayout.EdgeDistance(focus), 20f, "but it did get well away from the road");
        }

        [Test]
        public void AFightStartingOrEndingClearsAnyPanSoTheArenaIsAlwaysFramed()
        {
            var c = new CameraController();
            c.Step(0.5f, new CameraInput { Pan = Vector2.up, Rotate = 1f }, Start, AlongX);
            c.ClearForSituationChange();
            Assert.IsFalse(c.ManualOverride); Assert.AreEqual(0f, c.YawOffset);
        }

        [Test]
        public void FartherZoomPansFasterSoTheSpeedFeelsConstantOnScreen()
        {
            var near = new CameraController(); var far = new CameraController();
            for (var i = 0; i < 20; i++) near.Step(0.01f, new CameraInput { Zoom = 1f }, Start, AlongX);
            for (var i = 0; i < 20; i++) far.Step(0.01f, new CameraInput { Zoom = -1f }, Start, AlongX);
            near.Step(1f, Pan(0, 1), Start, AlongX); far.Step(1f, Pan(0, 1), Start, AlongX);
            Assert.Greater(far.DesiredFocus(Vector3.zero).x, near.DesiredFocus(Vector3.zero).x);
        }
    }
}
