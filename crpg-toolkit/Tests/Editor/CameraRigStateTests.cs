using CRPG.Camera;
using NUnit.Framework;
using UnityEngine;

namespace CRPG.Tests.EditMode
{
    public class CameraRigStateTests
    {
        private static CRPGCameraSettings CreateSettings() => ScriptableObject.CreateInstance<CRPGCameraSettings>();

        [Test]
        public void FocusOnSetsFocusPointDirectly()
        {
            var rig = new CameraRigState(CreateSettings());
            var point = new Vector3(5f, 0f, 3f);

            rig.FocusOn(point);

            Assert.AreEqual(point, rig.FocusPoint);
        }

        [Test]
        public void PanMovesFocusAlongYawRelativeAxes()
        {
            var settings = CreateSettings();
            var rig = new CameraRigState(settings);

            rig.Pan(new Vector2(0f, 1f), deltaTime: 1f); // Yaw starts at 0 -> forward is +Z

            Assert.Less(Vector3.Distance(new Vector3(0f, 0f, settings.PanSpeed), rig.FocusPoint), 0.01f);
        }

        [Test]
        public void ApplyZoomClampsToMaximum()
        {
            var settings = CreateSettings();
            var rig = new CameraRigState(settings);

            rig.ApplyZoom(1000f);

            Assert.AreEqual(settings.MaximumZoom, rig.Zoom, 0.001f);
        }

        [Test]
        public void ApplyZoomClampsToMinimum()
        {
            var settings = CreateSettings();
            var rig = new CameraRigState(settings);

            rig.ApplyZoom(-1000f);

            Assert.AreEqual(settings.MinimumZoom, rig.Zoom, 0.001f);
        }

        [Test]
        public void RotateWrapsAtFullCircle()
        {
            var settings = CreateSettings();
            var rig = new CameraRigState(settings);

            rig.Rotate(1f, deltaTime: 4f); // RotationSpeed(90) * 4 = 360 -> wraps to 0

            Assert.AreEqual(0f, rig.Yaw, 0.001f);
        }

        [Test]
        public void SnapRotateJumpsByConfiguredDegrees()
        {
            var settings = CreateSettings();
            var rig = new CameraRigState(settings);

            rig.SnapRotate(1f);

            Assert.AreEqual(settings.SnapRotationDegrees, rig.Yaw, 0.001f);
        }
    }
}
