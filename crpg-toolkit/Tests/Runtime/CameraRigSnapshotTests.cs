using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using CRPG.Camera;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace CRPG.Tests.PlayMode
{
    public class CameraRigSnapshotTests
    {
        private readonly List<RigFixture> fixtures = new List<RigFixture>();

        private sealed class RigFixture
        {
            public readonly CRPGCameraSettings Settings;
            public readonly GameObject Root;
            public readonly Transform Yaw;
            public readonly Transform Pitch;
            public readonly UnityEngine.Camera Camera;
            public readonly IsometricCameraRig Driver;
            public Transform[] Transforms => new[] { Root.transform, Yaw, Pitch, Camera.transform };

            public RigFixture()
            {
                Settings = ScriptableObject.CreateInstance<CRPGCameraSettings>();
                JsonUtility.FromJsonOverwrite("{\"zoomSmoothingSpeed\":0.5,\"closeUpPitchOffset\":-15}", Settings);
                Root = new GameObject("SnapshotCameraRoot");
                Root.SetActive(false);
                Yaw = new GameObject("YawPivot").transform;
                Pitch = new GameObject("PitchPivot").transform;
                Camera = new GameObject("Camera").AddComponent<UnityEngine.Camera>();
                Yaw.SetParent(Root.transform, false);
                Pitch.SetParent(Yaw, false);
                Camera.transform.SetParent(Pitch, false);
                Driver = Root.AddComponent<IsometricCameraRig>();
                SetField(Driver, "yawPivot", Yaw);
                SetField(Driver, "pitchPivot", Pitch);
                SetField(Driver, "cameraTransform", Camera.transform);
                SetField(Driver, "settings", Settings);
                Root.SetActive(true);
            }

            // Serialized dependencies only, before native Awake/activation. Frames drive all presentation.
            private static void SetField(object target, string name, object value)
            {
                target.GetType().GetField(name, BindingFlags.NonPublic | BindingFlags.Instance).SetValue(target, value);
            }
        }

        private RigFixture Build()
        {
            RigFixture fixture = new RigFixture();
            this.fixtures.Add(fixture);
            return fixture;
        }

        [TearDown]
        public void TearDown()
        {
            foreach (RigFixture fixture in this.fixtures)
            {
                Object.DestroyImmediate(fixture.Root);
                Object.DestroyImmediate(fixture.Settings);
            }
            this.fixtures.Clear();
        }

        private static Pose[] CapturePoses(Transform[] transforms, bool local)
        {
            Pose[] poses = new Pose[transforms.Length];
            for (int i = 0; i < transforms.Length; i++)
            {
                poses[i] = local
                    ? new Pose(transforms[i].localPosition, transforms[i].localRotation)
                    : new Pose(transforms[i].position, transforms[i].rotation);
            }
            return poses;
        }

        private static void AssertPoses(Transform[] transforms, Pose[] poses, bool local)
        {
            for (int i = 0; i < transforms.Length; i++)
            {
                Vector3 position = local ? transforms[i].localPosition : transforms[i].position;
                Quaternion rotation = local ? transforms[i].localRotation : transforms[i].rotation;
                Assert.Less(Vector3.Distance(poses[i].position, position), 1e-5f, transforms[i].name + " position");
                Assert.Less(Quaternion.Angle(poses[i].rotation, rotation), 0.05f, transforms[i].name + " rotation");
            }
        }

        [UnityTest]
        public IEnumerator RestoreWhileDisabledImmediatelyRestoresActualPosesNotRecomputedTargets()
        {
            RigFixture fixture = Build();
            yield return null;
            fixture.Driver.Rig.SnapTo(new Vector3(3f, 2f, -4f));
            fixture.Driver.Rig.SetYaw(37f);
            fixture.Driver.Rig.SetZoom(fixture.Settings.MinimumZoom);
            fixture.Driver.Rig.Pan(new Vector2(1f, -0.5f), 0.1f);

            // Presentation deliberately differs from the logical targets on every transform.
            Transform[] transforms = fixture.Transforms;
            for (int i = 0; i < transforms.Length; i++)
            {
                transforms[i].localPosition = new Vector3(i + 1f, i - 2f, -i - 3f);
                transforms[i].localRotation = Quaternion.Euler(11f + i * 7f, 23f + i * 13f, 5f + i);
            }
            Vector3 focus = fixture.Driver.Rig.FocusPoint;
            float yaw = fixture.Driver.Rig.Yaw;
            float zoom = fixture.Driver.Rig.Zoom;
            float displayedZoom = fixture.Driver.DisplayedZoom;
            Pose[] localPoses = CapturePoses(transforms, true);
            Pose[] worldPoses = CapturePoses(transforms, false);
            string settingsBefore = JsonUtility.ToJson(fixture.Settings);
            IsometricCameraRig.Snapshot snapshot = fixture.Driver.CaptureState();

            fixture.Driver.Rig.SnapTo(new Vector3(-20f, 7f, 30f));
            fixture.Driver.Rig.SetYaw(210f);
            fixture.Driver.Rig.SetZoom(fixture.Settings.MaximumZoom);
            fixture.Driver.enabled = true;
            yield return null;
            yield return null;
            Assert.AreNotEqual(localPoses[0].position, fixture.Root.transform.localPosition, "a real LateUpdate must present the temporary shot");
            fixture.Driver.enabled = false;
            fixture.Camera.enabled = false;
            fixture.Camera.fieldOfView = 42f;
            foreach (Transform target in transforms)
            {
                target.localPosition = Vector3.one * 50f;
                target.localRotation = Quaternion.Euler(80f, 90f, 100f);
            }

            fixture.Driver.RestoreState(snapshot);

            Assert.AreEqual(focus, fixture.Driver.Rig.FocusPoint);
            Assert.AreEqual(yaw, fixture.Driver.Rig.Yaw);
            Assert.AreEqual(zoom, fixture.Driver.Rig.Zoom);
            Assert.IsTrue(fixture.Driver.Rig.IsManualOverrideActive);
            Assert.AreEqual(displayedZoom, fixture.Driver.DisplayedZoom, "restore must not advance the accumulator");
            AssertPoses(transforms, localPoses, true);
            AssertPoses(transforms, worldPoses, false);
            Assert.IsFalse(fixture.Driver.enabled, "the caller owns enable flags");
            Assert.IsFalse(fixture.Camera.enabled);
            Assert.AreEqual(42f, fixture.Camera.fieldOfView, "the caller owns the lens");
            Assert.AreSame(fixture.Settings, fixture.Driver.Settings);
            Assert.AreEqual(settingsBefore, JsonUtility.ToJson(fixture.Settings));
            yield return null;
            AssertPoses(transforms, localPoses, true);
            AssertPoses(transforms, worldPoses, false);
        }

        [UnityTest]
        public IEnumerator RestoredSmoothingContinuesLikeAnUntouchedRigOnRealFrames()
        {
            RigFixture fixture = Build();
            RigFixture control = Build();
            fixture.Driver.Rig.SetZoom(fixture.Settings.MinimumZoom);
            control.Driver.Rig.SetZoom(control.Settings.MinimumZoom);
            yield return null;
            yield return null;
            fixture.Driver.enabled = false;
            control.Driver.enabled = false;
            float displayedZoom = fixture.Driver.DisplayedZoom;
            Assert.Greater(displayedZoom, fixture.Driver.Rig.Zoom, "capture a real in-flight zoom, not a settled target");
            Assert.Less(displayedZoom, fixture.Settings.DefaultZoom);
            Assert.AreEqual(displayedZoom, control.Driver.DisplayedZoom, 1e-5f);
            IsometricCameraRig.Snapshot snapshot = fixture.Driver.CaptureState();
            Pose[] baselinePoses = CapturePoses(fixture.Transforms, true);

            fixture.Driver.Rig.SetZoom(fixture.Settings.MaximumZoom);
            fixture.Driver.enabled = true;
            yield return null;
            yield return null;
            fixture.Driver.enabled = false;
            Assert.Greater(fixture.Driver.DisplayedZoom, displayedZoom, "temporary shot must actually advance smoothing");

            fixture.Driver.RestoreState(snapshot);

            Assert.AreEqual(displayedZoom, fixture.Driver.DisplayedZoom);
            Assert.AreEqual(control.Driver.Rig.Zoom, fixture.Driver.Rig.Zoom);
            Assert.IsFalse(fixture.Driver.Rig.IsManualOverrideActive);
            AssertPoses(fixture.Transforms, baselinePoses, true);
            fixture.Driver.enabled = true;
            control.Driver.enabled = true;
            yield return null;
            yield return null;
            Assert.Less(fixture.Driver.DisplayedZoom, displayedZoom, "native LateUpdate resumes the original zoom trajectory");
            Assert.Greater(fixture.Driver.DisplayedZoom, fixture.Driver.Rig.Zoom);
            Assert.AreEqual(control.Driver.DisplayedZoom, fixture.Driver.DisplayedZoom, 1e-5f, "the restored accumulator must match the untouched control");
            Assert.AreEqual(-fixture.Driver.DisplayedZoom, fixture.Camera.transform.localPosition.z, 1e-5f);
            Assert.Less(Quaternion.Angle(Quaternion.Euler(fixture.Driver.Rig.PitchFor(fixture.Driver.DisplayedZoom), 0f, 0f), fixture.Pitch.localRotation), 0.05f);
            AssertPoses(fixture.Transforms, CapturePoses(control.Transforms, true), true);
        }
    }
}
