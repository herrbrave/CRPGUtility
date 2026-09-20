using System.IO;
using System.Reflection;
using CRPG.Camera;
using CRPG.Exploration;
using UnityEditor;
using UnityEngine;

namespace BarrowsEnd.Editor
{
    /// <summary>
    /// Task 3.1: authors the campaign's top-down camera -- the <c>BarrowsEnd_Camera</c> settings asset (orthographic pixel-art mode, integer scales 2-6
    /// with 3 as the default at 1080p, yaw locked at 0, pitch 90, pixel-snapped follow) and the <c>BarrowsEnd_CameraRig</c> prefab
    /// (CameraRoot -> YawPivot -> PitchPivot -> Camera, with <see cref="IsometricCameraRig"/> and <see cref="PartyCameraFollowBehaviour"/>).
    /// Idempotent: existing assets are updated in place (GUIDs stay). The input source that feeds zoom to the rig lives in the scene (the location
    /// shell wires it), not in this prefab.
    /// </summary>
    public static class BarrowsEndCameraAuthor
    {
        public const string OutputDir = "Assets/Content/Camera";
        public const string SettingsPath = OutputDir + "/BarrowsEnd_Camera.asset";
        public const string RigPrefabPath = OutputDir + "/BarrowsEnd_CameraRig.prefab";

        [MenuItem("Barrows End/Content/Author Camera")]
        public static void Author()
        {
            Directory.CreateDirectory(OutputDir);
            AssetDatabase.Refresh();

            var settings = AssetDatabase.LoadAssetAtPath<CRPGCameraSettings>(SettingsPath);
            var isNew = settings == null;
            if (isNew) settings = ScriptableObject.CreateInstance<CRPGCameraSettings>();

            Set(settings, "orthographic", true);
            Set(settings, "pixelsPerUnit", 16);
            Set(settings, "zoomScales", new[] { 2, 3, 4, 5, 6 });
            Set(settings, "defaultScale", 3);
            Set(settings, "pixelSnapFocus", true);
            // Yaw locked (the sprite sorting contract needs yaw 0) and looking straight down.
            Set(settings, "rotationSpeed", 0f);
            Set(settings, "snapRotationDegrees", 0f);
            Set(settings, "minimumPitch", 90f);
            Set(settings, "maximumPitch", 90f);
            Set(settings, "defaultPitch", 90f);
            // Fixed camera distance; must clear the camera's near/far planes and the tallest sorted content.
            Set(settings, "minimumZoom", 20f);
            Set(settings, "maximumZoom", 20f);
            Set(settings, "defaultZoom", 20f);
            Set(settings, "followSelection", true);
            Set(settings, "followSmoothingSpeed", 8f);
            Set(settings, "enableEdgeScrolling", false);

            if (isNew) AssetDatabase.CreateAsset(settings, SettingsPath);
            else EditorUtility.SetDirty(settings);
            AssetDatabase.SaveAssets();
            settings = AssetDatabase.LoadAssetAtPath<CRPGCameraSettings>(SettingsPath);

            BuildRigPrefab(settings);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log($"[BarrowsEnd] Authored {SettingsPath} and {RigPrefabPath}");
        }

        private static void BuildRigPrefab(CRPGCameraSettings settings)
        {
            var root = new GameObject("CameraRoot");
            root.SetActive(false); // fields must be in place before Awake runs Apply()
            var yaw = new GameObject("YawPivot").transform;
            var pitch = new GameObject("PitchPivot").transform;
            var cameraGo = new GameObject("Main Camera") { tag = "MainCamera" };
            yaw.SetParent(root.transform, false);
            pitch.SetParent(yaw, false);
            cameraGo.transform.SetParent(pitch, false);

            var camera = cameraGo.AddComponent<Camera>();
            camera.orthographic = true;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(0.08f, 0.07f, 0.1f);
            camera.nearClipPlane = 0.1f;
            camera.farClipPlane = 100f;
            cameraGo.AddComponent<UnityEngine.Rendering.Universal.UniversalAdditionalCameraData>();
            cameraGo.AddComponent<AudioListener>();

            var rig = root.AddComponent<IsometricCameraRig>();
            SetObject(rig, "yawPivot", yaw);
            SetObject(rig, "pitchPivot", pitch);
            SetObject(rig, "cameraTransform", cameraGo.transform);
            SetObject(rig, "settings", settings);

            var follow = root.AddComponent<PartyCameraFollowBehaviour>();
            SetObject(follow, "cameraRig", rig);
            SetObject(follow, "settings", settings);

            root.SetActive(true);
            PrefabUtility.SaveAsPrefabAsset(root, RigPrefabPath);
            Object.DestroyImmediate(root);
        }

        private static void Set(object target, string name, object value) => Field(target, name).SetValue(target, value);

        private static void SetObject(Object target, string name, Object value)
        {
            var so = new SerializedObject(target);
            so.FindProperty(name).objectReferenceValue = value;
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        private static FieldInfo Field(object target, string name)
        {
            var type = target.GetType();
            while (type != null)
            {
                var field = type.GetField(name, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
                if (field != null) return field;
                type = type.BaseType;
            }

            throw new System.Exception($"Field '{name}' not found on {target.GetType().Name}.");
        }
    }
}
