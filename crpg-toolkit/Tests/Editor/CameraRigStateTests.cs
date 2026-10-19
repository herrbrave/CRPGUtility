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
        public void SetZoomIsAbsoluteAndClampedAndLeavesTheFollowAlone()
        {
            var settings = CreateSettings();
            var rig = new CameraRigState(settings);
            rig.Pan(new Vector2(1f, 0f), 0.1f);   // a manual pan is in effect

            rig.SetZoom((settings.MinimumZoom + settings.MaximumZoom) * 0.5f);
            Assert.AreEqual((settings.MinimumZoom + settings.MaximumZoom) * 0.5f, rig.Zoom, 1e-4f, "an absolute value, not a delta");
            rig.SetZoom(settings.MaximumZoom * 10f); Assert.AreEqual(settings.MaximumZoom, rig.Zoom, 1e-4f);
            rig.SetZoom(-5f); Assert.AreEqual(settings.MinimumZoom, rig.Zoom, 1e-4f);
            Assert.IsTrue(rig.IsManualOverrideActive, "a zoom is not a re-centre");
        }

        [Test]
        public void SetYawIsAbsoluteAndWrapsIntoOneTurn()
        {
            var rig = new CameraRigState(CreateSettings());
            rig.SetYaw(90f); Assert.AreEqual(90f, rig.Yaw, 1e-4f);
            rig.SetYaw(450f); Assert.AreEqual(90f, rig.Yaw, 1e-3f);
            rig.SetYaw(-30f); Assert.AreEqual(330f, rig.Yaw, 1e-3f);
            rig.Pan(new Vector2(0f, 1f), 1f);   // the pan follows the new yaw: the yaw really took effect
            Assert.Less(Mathf.Abs(rig.FocusPoint.x - -Mathf.Sin(30f * Mathf.Deg2Rad) * CreateSettings().PanSpeed), 0.05f);
        }

        [Test]
        public void SetZoomIsIgnoredInOrthographicMode()
        {
            var settings = CreateSettings();
            typeof(CRPGCameraSettings).GetField("orthographic", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)?.SetValue(settings, true);
            var rig = new CameraRigState(settings);
            var before = rig.Zoom;
            rig.SetZoom(before + 3f);
            if (rig.Orthographic) Assert.AreEqual(before, rig.Zoom, 1e-4f, "orthographic zoom is the integer scale list, not a distance");
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

        private sealed class FakeGroundHeightProvider : IGroundHeightProvider
        {
            public bool Succeeds = true;
            public float Height;
            public Vector3? LastQueriedXZ;

            public bool TryGetGroundHeight(Vector3 xzPosition, out float height)
            {
                LastQueriedXZ = xzPosition;
                height = Height;
                return Succeeds;
            }
        }

        [Test]
        public void FollowTargetApproachesGraduallyAndNeverSnaps()
        {
            var rig = new CameraRigState(CreateSettings());
            rig.SnapTo(Vector3.zero);
            var target = new Vector3(10f, 0f, 0f);

            rig.FollowTarget(target, deltaTime: 0.1f);

            Assert.Greater(rig.FocusPoint.x, 0f);
            Assert.Less(rig.FocusPoint.x, target.x); // approached, not snapped
        }

        [Test]
        public void FollowTargetConvergesOverManyCalls()
        {
            var rig = new CameraRigState(CreateSettings());
            rig.SnapTo(Vector3.zero);
            var target = new Vector3(10f, 0f, 0f);

            for (var i = 0; i < 200; i++) rig.FollowTarget(target, deltaTime: 0.1f);

            Assert.Less(Vector3.Distance(rig.FocusPoint, target), 0.01f);
        }

        [Test]
        public void SnapToLandsExactlyInOneCallAndClearsManualOverride()
        {
            var rig = new CameraRigState(CreateSettings());
            rig.Pan(Vector2.up, deltaTime: 1f); // sets manual override
            Assert.IsTrue(rig.IsManualOverrideActive);

            var target = new Vector3(7f, 0f, -3f);
            rig.SnapTo(target);

            Assert.AreEqual(target, rig.FocusPoint);
            Assert.IsFalse(rig.IsManualOverrideActive);
        }

        [Test]
        public void PanSuspendsFollowUntilResumeFollowIsCalled()
        {
            var rig = new CameraRigState(CreateSettings());
            rig.SnapTo(Vector3.zero);
            rig.Pan(Vector2.up, deltaTime: 1f);
            var afterPan = rig.FocusPoint;

            rig.FollowTarget(new Vector3(50f, 0f, 50f), deltaTime: 1f);
            Assert.AreEqual(afterPan, rig.FocusPoint); // FollowTarget was a no-op while overridden

            rig.ResumeFollow();
            rig.FollowTarget(new Vector3(50f, 0f, 50f), deltaTime: 1f);
            Assert.AreNotEqual(afterPan, rig.FocusPoint); // now resumes
        }

        [Test]
        public void BoundsClampFocusPointForEveryMutator()
        {
            var rig = new CameraRigState(CreateSettings());
            rig.SetBounds(new Rect(-5f, -5f, 10f, 10f)); // x/z in [-5, 5]

            rig.SnapTo(new Vector3(100f, 0f, 100f));
            Assert.AreEqual(5f, rig.FocusPoint.x, 0.001f);
            Assert.AreEqual(5f, rig.FocusPoint.z, 0.001f);

            rig.SnapTo(Vector3.zero);
            rig.FollowTarget(new Vector3(-100f, 0f, -100f), deltaTime: 1000f); // huge deltaTime -> converges fully
            Assert.AreEqual(-5f, rig.FocusPoint.x, 0.001f);
            Assert.AreEqual(-5f, rig.FocusPoint.z, 0.001f);

            rig.SnapTo(Vector3.zero);
            rig.Pan(new Vector2(100f, 100f), deltaTime: 1f);
            Assert.LessOrEqual(rig.FocusPoint.x, 5f);
            Assert.LessOrEqual(rig.FocusPoint.z, 5f);

            rig.FocusOn(new Vector3(100f, 0f, 100f));
            Assert.AreEqual(5f, rig.FocusPoint.x, 0.001f);
        }

        [Test]
        public void GroundHeightProviderIsQueriedWithTheAlreadyBoundsClampedXZ()
        {
            var rig = new CameraRigState(CreateSettings());
            rig.SetBounds(new Rect(-5f, -5f, 10f, 10f));
            var provider = new FakeGroundHeightProvider { Succeeds = true, Height = 3f };
            rig.GroundHeightProvider = provider;

            rig.SnapTo(new Vector3(100f, 0f, 0f)); // clamps to x=5 before the provider ever sees it

            Assert.AreEqual(5f, provider.LastQueriedXZ.Value.x, 0.001f);
            Assert.AreEqual(3f, rig.FocusPoint.y, 0.001f);
        }

        [Test]
        public void AGroundHeightProviderReturningFalseLeavesYUnchanged()
        {
            var rig = new CameraRigState(CreateSettings());
            rig.GroundHeightProvider = new FakeGroundHeightProvider { Succeeds = false };

            rig.SnapTo(new Vector3(1f, 9f, 1f));

            Assert.AreEqual(9f, rig.FocusPoint.y, 0.001f);
        }
    }
}
