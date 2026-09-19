using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using CRPG.Camera;
using CRPG.Presentation.Sprites;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace CRPG.Tests.PlayMode
{
    using Camera = UnityEngine.Camera; // CRPG.Camera is a namespace and would otherwise win the lookup

    /// <summary>2D campaign Task 2.1: CameraFacingBillboard keeps a sprite card parallel to the camera plane.</summary>
    public class CameraFacingBillboardTests
    {
        private readonly List<GameObject> spawned = new();

        [TearDown]
        public void TearDown()
        {
            foreach (var go in spawned) if (go != null) Object.DestroyImmediate(go);
            spawned.Clear();
        }

        private GameObject Make(string name, Transform parent = null)
        {
            var go = new GameObject(name);
            if (parent != null) go.transform.SetParent(parent, false);
            spawned.Add(go);
            return go;
        }

        private Camera MakeMainCamera(string name = "Main Camera", float pitch = 90f, float yaw = 0f)
        {
            var go = Make(name);
            go.tag = "MainCamera";
            var camera = go.AddComponent<Camera>();
            go.transform.rotation = Quaternion.Euler(pitch, yaw, 0f);
            return camera;
        }

        private static void SetField(object target, string name, object value) =>
            target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).SetValue(target, value);

        private static void AssertSameRotation(Quaternion expected, Quaternion actual, string message = null) =>
            Assert.Less(Quaternion.Angle(expected, actual), 0.01f, message);

        [UnityTest]
        public IEnumerator TheCardMatchesTheCameraRotationAtPitchNinety() { yield return CardMatchesCameraAt(90f); }

        [UnityTest]
        public IEnumerator TheCardMatchesTheCameraRotationAtPitchEightyFive() { yield return CardMatchesCameraAt(85f); }

        private IEnumerator CardMatchesCameraAt(float pitch)
        {
            var camera = MakeMainCamera(pitch: pitch);
            var card = Make("Card");
            card.AddComponent<CameraFacingBillboard>();

            yield return null;

            AssertSameRotation(camera.transform.rotation, card.transform.rotation);
        }

        [UnityTest]
        public IEnumerator TheCardFollowsACameraYawChange()
        {
            var camera = MakeMainCamera(pitch: 90f);
            var card = Make("Card");
            card.AddComponent<CameraFacingBillboard>();
            yield return null;

            camera.transform.rotation = Quaternion.Euler(90f, 45f, 0f);
            yield return null;

            AssertSameRotation(camera.transform.rotation, card.transform.rotation);
        }

        [UnityTest]
        public IEnumerator ARealIsometricCameraRigAtPitchNinetyIsMatchedInTheFrameTheRigWritesIt()
        {
            var settings = ScriptableObject.CreateInstance<CRPGCameraSettings>();
            SetField(settings, "minimumPitch", 90f);
            SetField(settings, "maximumPitch", 90f);
            SetField(settings, "defaultPitch", 90f); // CameraRigState.Pitch = Clamp(defaultPitch, min, max), set once

            var rigGo = Make("CameraRoot");
            rigGo.SetActive(false);
            var yaw = Make("YawPivot", rigGo.transform).transform;
            var pitch = Make("PitchPivot", yaw).transform;
            var camGo = Make("Main Camera", pitch);
            camGo.tag = "MainCamera";
            camGo.AddComponent<Camera>();
            var rig = rigGo.AddComponent<IsometricCameraRig>();
            SetField(rig, "yawPivot", yaw);
            SetField(rig, "pitchPivot", pitch);
            SetField(rig, "cameraTransform", camGo.transform);
            SetField(rig, "settings", settings);
            rigGo.SetActive(true);

            var card = Make("Card");
            card.AddComponent<CameraFacingBillboard>();
            yield return null;
            yield return null;

            Assert.AreEqual(90f, rig.Rig.Pitch, "the test really runs at pitch 90 (not the asset default of 55)");
            AssertSameRotation(camGo.transform.rotation, card.transform.rotation, "matched after the rig wrote it, not one frame behind");

            rig.Rig.SnapTo(new Vector3(5f, 0f, 5f));
            yield return null;
            AssertSameRotation(camGo.transform.rotation, card.transform.rotation);
            Object.DestroyImmediate(settings);
        }

        [UnityTest]
        public IEnumerator RotatingTheParentDoesNotChangeTheCardsWorldRotation()
        {
            var camera = MakeMainCamera(pitch: 90f);
            var parent = Make("CharacterRoot");
            var card = Make("Card", parent.transform);
            card.AddComponent<CameraFacingBillboard>();
            yield return null;
            var before = card.transform.rotation;

            parent.transform.rotation = Quaternion.Euler(0f, 137f, 0f); // what FacingController does to CharacterRoot
            yield return null;

            AssertSameRotation(before, card.transform.rotation);
            AssertSameRotation(camera.transform.rotation, card.transform.rotation);
        }

        [UnityTest]
        public IEnumerator PositionAndScaleAreNeverTouched()
        {
            MakeMainCamera(pitch: 90f);
            var card = Make("Card");
            card.transform.position = new Vector3(3f, 0.02f, 4f);
            card.transform.localScale = new Vector3(2f, 3f, 1f);
            card.AddComponent<CameraFacingBillboard>();

            yield return Frames(5);

            Assert.AreEqual(new Vector3(3f, 0.02f, 4f), card.transform.position);
            Assert.AreEqual(new Vector3(2f, 3f, 1f), card.transform.localScale);
        }

        [UnityTest]
        public IEnumerator TheOverrideCameraBeatsCameraMain()
        {
            MakeMainCamera(pitch: 90f);
            var other = Make("Other Camera").AddComponent<Camera>();
            other.transform.rotation = Quaternion.Euler(60f, 30f, 0f);
            var card = Make("Card");
            var billboard = card.AddComponent<CameraFacingBillboard>();
            billboard.CameraOverride = other;

            yield return null;

            AssertSameRotation(other.transform.rotation, card.transform.rotation);
        }

        [UnityTest]
        public IEnumerator DestroyingTheResolvedCameraThrowsNothingAndItReResolvesToTheNewMainCamera()
        {
            var first = MakeMainCamera("First", pitch: 90f);
            var card = Make("Card");
            card.AddComponent<CameraFacingBillboard>();
            yield return null;

            LogAssert.Expect(LogType.Warning, new System.Text.RegularExpressions.Regex("No camera to face"));
            Object.DestroyImmediate(first.gameObject);
            yield return Frames(3); // no camera at all: must not throw

            var second = MakeMainCamera("Second", pitch: 60f, yaw: 20f);
            yield return Frames(2);

            AssertSameRotation(second.transform.rotation, card.transform.rotation);
        }

        [UnityTest]
        public IEnumerator NoCameraAtAllWarnsExactlyOnceAndDoesNotThrow()
        {
            var card = Make("Card");
            var before = Quaternion.Euler(10f, 20f, 30f);
            card.transform.rotation = before;
            LogAssert.Expect(LogType.Warning, new System.Text.RegularExpressions.Regex("No camera to face"));
            card.AddComponent<CameraFacingBillboard>();

            yield return Frames(10);

            LogAssert.NoUnexpectedReceived();
            AssertSameRotation(before, card.transform.rotation);
        }

        [Test]
        public void TheBillboardRunsAfterTheCameraRig()
        {
            var attribute = (DefaultExecutionOrder)System.Attribute.GetCustomAttribute(typeof(CameraFacingBillboard), typeof(DefaultExecutionOrder));
            Assert.IsNotNull(attribute);
            Assert.AreEqual(100, attribute.order);
            Assert.Greater(attribute.order, 0, "IsometricCameraRig runs at the default order 0 and writes the camera in LateUpdate");
        }

        private static IEnumerator Frames(int count)
        {
            for (var i = 0; i < count; i++) yield return null;
        }
    }
}
