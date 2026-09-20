using System.Collections;
using System.Reflection;
using CRPG.Camera;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace CRPG.Tests.PlayMode
{
    /// <summary>2D campaign Task 3.1: the REAL IsometricCameraRig with a real Camera in orthographic pixel-art mode -- orthographicSize follows the
    /// integer scale and the rendered pixel height, the camera looks straight down, follow positions are pixel-snapped, the visible rectangle stays
    /// inside the map, and a default (perspective) rig is untouched.</summary>
    public class OrthographicCameraRigTests
    {
        private GameObject rigGo;
        private IsometricCameraRig rig;
        private UnityEngine.Camera cam;

        [TearDown]
        public void TearDown()
        {
            if (rigGo != null) Object.DestroyImmediate(rigGo);
        }

        private static void SetField(object target, string name, object value) =>
            target.GetType().GetField(name, BindingFlags.NonPublic | BindingFlags.Instance).SetValue(target, value);

        private void Build(bool orthographic, int defaultScale = 3)
        {
            var settings = ScriptableObject.CreateInstance<CRPGCameraSettings>();
            SetField(settings, "orthographic", orthographic);
            SetField(settings, "defaultScale", defaultScale);
            SetField(settings, "rotationSpeed", 0f);
            SetField(settings, "snapRotationDegrees", 0f);
            SetField(settings, "minimumPitch", 90f);
            SetField(settings, "maximumPitch", 90f);
            SetField(settings, "defaultPitch", 90f);

            rigGo = new GameObject("CameraRoot");
            rigGo.SetActive(false); // fields must be in place before Awake runs Apply()
            var yaw = new GameObject("YawPivot").transform;
            var pitch = new GameObject("PitchPivot").transform;
            var cameraGo = new GameObject("Camera");
            yaw.SetParent(rigGo.transform);
            pitch.SetParent(yaw);
            cameraGo.transform.SetParent(pitch);
            cam = cameraGo.AddComponent<UnityEngine.Camera>();

            rig = rigGo.AddComponent<IsometricCameraRig>();
            SetField(rig, "yawPivot", yaw);
            SetField(rig, "pitchPivot", pitch);
            SetField(rig, "cameraTransform", cameraGo.transform);
            SetField(rig, "settings", settings);
            rigGo.SetActive(true);
        }

        [UnityTest]
        public IEnumerator OrthographicSizeFollowsTheIntegerScaleAndTheRenderedPixelHeight()
        {
            Build(orthographic: true, defaultScale: 3);
            yield return null;

            Assert.IsTrue(cam.orthographic);
            Assert.AreEqual(cam.pixelHeight / (2f * 16f * 3f), cam.orthographicSize, 1e-4f);

            rig.Rig.ApplyZoom(1f);
            yield return null;
            Assert.AreEqual(4, rig.Rig.Scale);
            Assert.AreEqual(cam.pixelHeight / (2f * 16f * 4f), cam.orthographicSize, 1e-4f, "one notch is one integer scale");
        }

        [UnityTest]
        public IEnumerator TheCameraLooksStraightDownWithYawZeroAtAFixedDistanceWhateverTheScale()
        {
            Build(orthographic: true);
            yield return null;
            var distance = cam.transform.position.y - rigGo.transform.position.y;

            for (var i = 0; i < 4; i++) { rig.Rig.ApplyZoom(-1f); yield return null; }
            rig.Rig.Rotate(1f, 10f);
            rig.Rig.SnapRotate(1f);
            yield return null;

            Assert.AreEqual(90f, cam.transform.eulerAngles.x, 0.01f);
            Assert.AreEqual(0f, Mathf.DeltaAngle(0f, cam.transform.eulerAngles.y), 0.01f, "yaw stays 0");
            Assert.AreEqual(distance, cam.transform.position.y - rigGo.transform.position.y, 1e-4f, "zoom is scale, not distance");
        }

        [UnityTest]
        public IEnumerator TheCameraPositionIsSnappedToWholeScreenPixelsWhileTheSmoothedFollowMoves()
        {
            Build(orthographic: true, defaultScale: 3);
            rig.Rig.SnapTo(Vector3.zero);
            yield return null;
            var step = 1f / (16f * 3f);
            var target = new Vector3(7.31f, 0f, 3.77f);
            var distinctX = new System.Collections.Generic.HashSet<float>();

            for (var i = 0; i < 90; i++)
            {
                rig.Rig.FollowTarget(target, 0.016f);
                yield return null;
                var p = rigGo.transform.position;
                distinctX.Add(p.x);
                Assert.AreEqual(0f, Mathf.Repeat(p.x / step + 0.5f, 1f) - 0.5f, 2e-3f, $"frame {i}: x is on the pixel grid");
                Assert.AreEqual(0f, Mathf.Repeat(p.z / step + 0.5f, 1f) - 0.5f, 2e-3f, $"frame {i}: z is on the pixel grid");
            }

            Assert.Greater(distinctX.Count, 5, "and the camera really did move (the check is not vacuous)");
        }

        [UnityTest]
        public IEnumerator TheVisibleRectangleStaysInsideTheMapBounds()
        {
            Build(orthographic: true, defaultScale: 3);
            yield return null;
            rig.Rig.SetBounds(new Rect(0f, 0f, 200f, 200f));

            foreach (var target in new[] { new Vector3(-50f, 0f, -50f), new Vector3(500f, 0f, 500f), new Vector3(100f, 0f, 100f) })
            {
                rig.Rig.SnapTo(target);
                yield return null;
                var half = new Vector2(cam.orthographicSize * cam.aspect, cam.orthographicSize);
                var position = cam.transform.position;
                Assert.GreaterOrEqual(position.x - half.x, -0.1f, $"left edge inside for {target}");
                Assert.LessOrEqual(position.x + half.x, 200.1f, $"right edge inside for {target}");
                Assert.GreaterOrEqual(position.z - half.y, -0.1f, $"bottom edge inside for {target}");
                Assert.LessOrEqual(position.z + half.y, 200.1f, $"top edge inside for {target}");
            }
        }

        [UnityTest]
        public IEnumerator ADefaultRigStaysPerspectiveAndUntouched()
        {
            Build(orthographic: false);
            var sizeBefore = cam.orthographicSize;
            cam.orthographic = false;
            yield return null;
            yield return null;

            Assert.IsFalse(cam.orthographic, "the rig does not turn a perspective camera orthographic");
            Assert.AreEqual(sizeBefore, cam.orthographicSize);
            rig.Rig.FocusOn(new Vector3(1.2345f, 0f, 6.789f));
            yield return null;
            Assert.AreEqual(rig.Rig.FocusPoint, rigGo.transform.position, "no snapping in perspective mode");
        }
    }
}
