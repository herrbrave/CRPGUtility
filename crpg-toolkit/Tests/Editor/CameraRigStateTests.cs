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

        private static object CaptureState(CameraRigState rig)
        {
            var capture = typeof(CameraRigState).GetMethod("CaptureState",
                System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance,
                null, System.Type.EmptyTypes, null);
            Assert.NotNull(capture, "CameraRigState must expose CaptureState() for exact scripted-camera restoration.");
            var snapshot = capture.Invoke(rig, null);
            Assert.NotNull(snapshot, "CaptureState must return a snapshot, not an absent-state fallback.");
            return snapshot;
        }

        private static void RestoreState(CameraRigState rig, object snapshot)
        {
            var restore = typeof(CameraRigState).GetMethod("RestoreState",
                System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance,
                null, new[] { snapshot.GetType() }, null);
            Assert.NotNull(restore, "CameraRigState must expose RestoreState(snapshot) for the captured snapshot type.");
            restore.Invoke(rig, new[] { snapshot });
        }

        [Test]
        public void RestoreStateRestoresManualPanViewExactlyAndKeepsFollowSuspended()
        {
            var settings = CreateSettings();
            try
            {
                var rig = new CameraRigState(settings);
                rig.SnapTo(new Vector3(2f, 1f, -3f));
                rig.FollowTarget(new Vector3(8f, 1f, 4f), deltaTime: 0.1f);
                rig.SetYaw(37f);
                rig.SetZoom(settings.MinimumZoom + 1f);
                rig.Pan(new Vector2(1f, -0.5f), deltaTime: 0.1f);
                Assert.IsTrue(rig.IsManualOverrideActive);
                var focus = rig.FocusPoint;
                var yaw = rig.Yaw;
                var zoom = rig.Zoom;
                var snapshot = CaptureState(rig);

                rig.SnapTo(new Vector3(-20f, 5f, 30f));
                rig.SetYaw(210f);
                rig.SetZoom(settings.MaximumZoom);
                Assert.IsFalse(rig.IsManualOverrideActive, "the scripted snap clears the original manual override");

                RestoreState(rig, snapshot);

                Assert.AreEqual(focus, rig.FocusPoint);
                Assert.AreEqual(yaw, rig.Yaw);
                Assert.AreEqual(zoom, rig.Zoom);
                Assert.IsTrue(rig.IsManualOverrideActive, "restoration must not act like SnapTo/recenter");
                rig.FollowTarget(new Vector3(50f, 0f, 50f), deltaTime: 1f);
                Assert.AreEqual(focus, rig.FocusPoint, "manual pan must still win after the dialogue ends");
                rig.ResumeFollow();
                rig.FollowTarget(new Vector3(50f, 0f, 50f), deltaTime: 0.1f);
                Assert.AreNotEqual(focus, rig.FocusPoint, "an explicit recenter must still release the restored override");
            }
            finally { Object.DestroyImmediate(settings); }
        }

        [Test]
        public void RestoreStateRestoresNonmanualViewAndResumesSmoothedFollow()
        {
            var settings = CreateSettings();
            try
            {
                var rig = new CameraRigState(settings);
                rig.SnapTo(new Vector3(2f, 1f, -3f));
                rig.SetYaw(123f);
                rig.SetZoom(settings.MinimumZoom + 2f);
                Assert.IsFalse(rig.IsManualOverrideActive);
                var focus = rig.FocusPoint;
                var yaw = rig.Yaw;
                var zoom = rig.Zoom;
                var snapshot = CaptureState(rig);

                rig.SnapTo(new Vector3(-20f, 5f, 30f));
                rig.SetYaw(270f);
                rig.SetZoom(settings.MaximumZoom);
                rig.Pan(Vector2.right, deltaTime: 0.1f);
                Assert.IsTrue(rig.IsManualOverrideActive, "the temporary view must differ in follow state too");

                RestoreState(rig, snapshot);

                Assert.AreEqual(focus, rig.FocusPoint);
                Assert.AreEqual(yaw, rig.Yaw);
                Assert.AreEqual(zoom, rig.Zoom);
                Assert.IsFalse(rig.IsManualOverrideActive);
                var target = focus + Vector3.right * 10f;
                rig.FollowTarget(target, deltaTime: 0.1f);
                Assert.Greater(rig.FocusPoint.x, focus.x, "following resumes without a separate ResumeFollow call");
                Assert.Less(rig.FocusPoint.x, target.x, "restoration must retain smoothed rather than snapping follow");
            }
            finally { Object.DestroyImmediate(settings); }
        }

        [Test]
        public void RestoreStateLeavesCurrentSettingsBoundsAndGroundProviderUntouched()
        {
            var settings = CreateSettings();
            var currentSettings = CreateSettings();
            try
            {
                JsonUtility.FromJsonOverwrite("{\"maximumZoom\":7,\"defaultZoom\":6}", currentSettings);
                var settingsBefore = JsonUtility.ToJson(settings);
                var currentSettingsBefore = JsonUtility.ToJson(currentSettings);
                var rig = new CameraRigState(settings);
                rig.SetBounds(new Rect(-10f, -10f, 20f, 20f));
                var originalProvider = new FakeGroundHeightProvider { Height = 3f };
                rig.GroundHeightProvider = originalProvider;
                rig.SnapTo(new Vector3(4f, 0f, 4f));
                var focus = rig.FocusPoint;
                var snapshot = CaptureState(rig);

                rig.UseSettings(currentSettings);
                rig.SetBounds(new Rect(-2f, -2f, 4f, 4f));
                var currentProvider = new FakeGroundHeightProvider { Height = 9f };
                rig.GroundHeightProvider = currentProvider;
                rig.SnapTo(Vector3.zero);
                currentProvider.LastQueriedXZ = null;

                RestoreState(rig, snapshot);

                Assert.AreEqual(focus, rig.FocusPoint, "restore is exact, not another bounds/ground projection");
                Assert.AreSame(currentProvider, rig.GroundHeightProvider);
                Assert.IsFalse(currentProvider.LastQueriedXZ.HasValue, "restore must not re-sample ground height");
                rig.FollowTarget(new Vector3(100f, 0f, 100f), deltaTime: 1000f);
                Assert.AreEqual(new Vector3(2f, 9f, 2f), rig.FocusPoint, "subsequent movement must use current bounds and provider");
                rig.SetZoom(100f);
                Assert.AreEqual(currentSettings.MaximumZoom, rig.Zoom, "the current settings asset must remain active");
                Assert.AreEqual(settingsBefore, JsonUtility.ToJson(settings));
                Assert.AreEqual(currentSettingsBefore, JsonUtility.ToJson(currentSettings));
            }
            finally
            {
                Object.DestroyImmediate(settings);
                Object.DestroyImmediate(currentSettings);
            }
        }

        [Test]
        public void RestoreStateKeepsCurrentOrthographicScaleWhenSettingsHaveFewerSteps()
        {
            var settings = CreateSettings();
            var currentSettings = CreateSettings();
            try
            {
                JsonUtility.FromJsonOverwrite("{\"orthographic\":true,\"defaultScale\":3}", settings);
                JsonUtility.FromJsonOverwrite("{\"orthographic\":true,\"zoomScales\":[2],\"defaultScale\":2,\"defaultPitch\":40}", currentSettings);
                var settingsBefore = JsonUtility.ToJson(settings);
                var currentSettingsBefore = JsonUtility.ToJson(currentSettings);
                var rig = new CameraRigState(settings);
                Assert.AreEqual(3, rig.Scale, "Capture a selected index above zero in the original default scale list.");
                rig.GroundHeightProvider = new FakeGroundHeightProvider { Height = 3f };
                rig.SetBounds(new Rect(-10f, -10f, 20f, 20f));
                rig.SnapTo(new Vector3(4f, 0f, 4f));
                rig.SetYaw(37f);
                var focus = rig.FocusPoint;
                var zoom = rig.Zoom;
                var snapshot = CaptureState(rig);

                rig.UseSettings(currentSettings);
                Assert.AreEqual(2, rig.Scale);
                rig.SetBounds(new Rect(-2f, -2f, 4f, 4f));
                var currentProvider = new FakeGroundHeightProvider { Height = 9f };
                rig.GroundHeightProvider = currentProvider;
                rig.SnapTo(Vector3.zero);
                rig.SetYaw(210f);
                rig.Pan(Vector2.right, deltaTime: 0.1f);
                currentProvider.LastQueriedXZ = null;

                RestoreState(rig, snapshot);

                Assert.AreEqual(2, rig.Scale, "Restore must leave the current scale selection alone, not reinstate the old out-of-range index.");
                Assert.IsTrue(rig.Orthographic);
                Assert.AreEqual(currentSettings.DefaultPitch, rig.BasePitch);
                Assert.AreEqual(focus, rig.FocusPoint, "Logical focus is restored exactly even outside the current bounds.");
                Assert.AreEqual(37f, rig.Yaw);
                Assert.AreEqual(zoom, rig.Zoom);
                Assert.IsFalse(rig.IsManualOverrideActive);
                Assert.AreSame(currentProvider, rig.GroundHeightProvider);
                Assert.IsFalse(currentProvider.LastQueriedXZ.HasValue, "Restore must not sample the provider.");
                rig.FollowTarget(new Vector3(100f, 0f, 100f), deltaTime: 1000f);
                Assert.AreEqual(new Vector3(2f, 9f, 2f), rig.FocusPoint, "Later movement must still use current bounds and provider.");
                rig.SetViewport(160f, 1f);
                Assert.AreEqual(160f / (2f * currentSettings.PixelsPerUnit * 2f), rig.OrthographicSize,
                    "The real orthographic rig consumer must remain safe to read after restoration.");
                Assert.AreEqual(settingsBefore, JsonUtility.ToJson(settings));
                Assert.AreEqual(currentSettingsBefore, JsonUtility.ToJson(currentSettings));
            }
            finally
            {
                Object.DestroyImmediate(settings);
                Object.DestroyImmediate(currentSettings);
            }
        }

        [Test]
        public void RestoreStateDoesNotRewindOrthographicScaleSelectedSinceCapture()
        {
            var settings = CreateSettings();
            try
            {
                JsonUtility.FromJsonOverwrite("{\"orthographic\":true,\"defaultScale\":3}", settings);
                var rig = new CameraRigState(settings);
                Assert.AreEqual(3, rig.Scale);
                var snapshot = CaptureState(rig);

                rig.StepScale(1);
                Assert.AreEqual(4, rig.Scale, "A real user/configuration scale change happened after capture.");
                RestoreState(rig, snapshot);

                Assert.AreEqual(4, rig.Scale, "The dialogue snapshot owns focus/yaw/distance/manual follow, not orthographic scale selection.");
            }
            finally { Object.DestroyImmediate(settings); }
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
