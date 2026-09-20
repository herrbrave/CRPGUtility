using System.Linq;
using BarrowsEnd.Editor;
using CRPG.Camera;
using CRPG.Editor.Validation;
using CRPG.Exploration;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace BarrowsEnd.Tests
{
    /// <summary>Task 3.1: the authored campaign camera -- settings asset and rig prefab.</summary>
    public class CameraContentTests
    {
        [OneTimeSetUp]
        public void Author() => BarrowsEndCameraAuthor.Author();

        private static CRPGCameraSettings Settings() => AssetDatabase.LoadAssetAtPath<CRPGCameraSettings>(BarrowsEndCameraAuthor.SettingsPath);
        private static GameObject Prefab() => AssetDatabase.LoadAssetAtPath<GameObject>(BarrowsEndCameraAuthor.RigPrefabPath);

        [Test]
        public void TheSettingsAreAnOrthographicPixelPerfectTopDownWithLockedYawAndFixedDistance()
        {
            var s = Settings();
            Assert.IsNotNull(s);
            Assert.IsTrue(s.Orthographic);
            Assert.AreEqual(16, s.PixelsPerUnit);
            CollectionAssert.AreEqual(new[] { 2, 3, 4, 5, 6 }, s.ZoomScales.ToArray());
            Assert.AreEqual(3, s.DefaultScale);
            Assert.IsTrue(s.PixelSnapFocus);
            Assert.AreEqual(0f, s.RotationSpeed);
            Assert.AreEqual(0f, s.SnapRotationDegrees);
            Assert.AreEqual(90f, s.MinimumPitch);
            Assert.AreEqual(90f, s.MaximumPitch);
            Assert.AreEqual(90f, s.DefaultPitch);
            Assert.AreEqual(s.MinimumZoom, s.MaximumZoom, "one fixed camera distance");
            Assert.IsTrue(s.FollowSelection);
        }

        [Test]
        public void TheValidatorIsCleanOnTheAuthoredSettings()
        {
            Assert.IsEmpty(CameraSettingsValidator.Validate(Settings(), BarrowsEndCameraAuthor.SettingsPath).Select(r => r.Message).ToList());
        }

        [Test]
        public void TheRigPrefabHasTheFullHierarchyAndEveryReferenceWired()
        {
            var root = Prefab();
            Assert.IsNotNull(root);
            var rig = root.GetComponent<IsometricCameraRig>();
            var follow = root.GetComponent<PartyCameraFollowBehaviour>();
            Assert.IsNotNull(rig);
            Assert.IsNotNull(follow);

            var rigSo = new SerializedObject(rig);
            var yaw = (Transform)rigSo.FindProperty("yawPivot").objectReferenceValue;
            var pitch = (Transform)rigSo.FindProperty("pitchPivot").objectReferenceValue;
            var cameraTransform = (Transform)rigSo.FindProperty("cameraTransform").objectReferenceValue;
            Assert.IsNotNull(yaw);
            Assert.IsNotNull(pitch);
            Assert.IsNotNull(cameraTransform);
            Assert.AreSame(root.transform, yaw.parent, "CameraRoot > YawPivot");
            Assert.AreSame(yaw, pitch.parent, "> PitchPivot");
            Assert.AreSame(pitch, cameraTransform.parent, "> Camera");
            Assert.AreSame(Settings(), rigSo.FindProperty("settings").objectReferenceValue);

            var followSo = new SerializedObject(follow);
            Assert.AreSame(rig, followSo.FindProperty("cameraRig").objectReferenceValue);
            Assert.AreSame(Settings(), followSo.FindProperty("settings").objectReferenceValue);
        }

        [Test]
        public void TheCameraIsTheOrthographicMainCameraWithUrpDataAndOneAudioListener()
        {
            var camera = Prefab().GetComponentInChildren<Camera>(true);
            Assert.IsNotNull(camera);
            Assert.AreEqual("MainCamera", camera.gameObject.tag);
            Assert.IsTrue(camera.orthographic);
            Assert.IsNotNull(camera.GetComponent<UnityEngine.Rendering.Universal.UniversalAdditionalCameraData>());
            Assert.AreEqual(1, Prefab().GetComponentsInChildren<AudioListener>(true).Length);
            Assert.Greater(camera.farClipPlane, Settings().DefaultZoom, "the camera distance is inside the far plane");
            Assert.Less(camera.nearClipPlane, Settings().DefaultZoom);
        }

        [Test]
        public void AuthoringTwiceKeepsTheGuidsAndAddsNothing()
        {
            var settingsGuid = AssetDatabase.AssetPathToGUID(BarrowsEndCameraAuthor.SettingsPath);
            var prefabGuid = AssetDatabase.AssetPathToGUID(BarrowsEndCameraAuthor.RigPrefabPath);

            BarrowsEndCameraAuthor.Author();

            Assert.AreEqual(settingsGuid, AssetDatabase.AssetPathToGUID(BarrowsEndCameraAuthor.SettingsPath));
            Assert.AreEqual(prefabGuid, AssetDatabase.AssetPathToGUID(BarrowsEndCameraAuthor.RigPrefabPath));
            Assert.AreEqual(1, Prefab().GetComponents<IsometricCameraRig>().Length);
        }
    }
}
