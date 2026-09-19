using System.IO;
using System.Linq;
using BarrowsEnd.Spike;
using CRPG.Camera;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace BarrowsEnd.Editor
{
    /// <summary>Builds the throwaway Task 2.0b look-and-feel scene through the REAL IsometricCameraRig at pitch 90.</summary>
    public static class LookSpikeBuilder
    {
        private const string ScenePath = "Assets/Scenes/Spike/LookSpike.unity";
        private const string Char = "Assets/Art/Source/roguelikeChar_transparent.png";
        private const string Bodies = "Assets/Art/Source/barrowsend_bodies_transparent.png";
        private const string Dun = "Assets/Art/Source/roguelikeDungeon_transparent.png";

        private static Sprite S(string sheet, int c, int r)
        {
            var p = sheet == "c" ? Char : sheet == "b" ? Bodies : Dun;
            var n = Path.GetFileNameWithoutExtension(p) + $"_{c}_{r}";
            var s = AssetDatabase.LoadAllAssetsAtPath(p).OfType<Sprite>().FirstOrDefault(x => x.name == n);
            if (s == null) Debug.LogWarning("[BarrowsEnd] missing sprite " + n);
            return s;
        }

        private static void EnsureSortingLayers(params string[] names)
        {
            var tm = new SerializedObject(AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/TagManager.asset")[0]);
            var arr = tm.FindProperty("m_SortingLayers");
            var next = 1000;
            foreach (var n in names)
            {
                var have = false;
                for (var i = 0; i < arr.arraySize; i++) if (arr.GetArrayElementAtIndex(i).FindPropertyRelative("name").stringValue == n) have = true;
                if (have) continue;
                arr.InsertArrayElementAtIndex(arr.arraySize);
                var e = arr.GetArrayElementAtIndex(arr.arraySize - 1);
                e.FindPropertyRelative("name").stringValue = n;
                e.FindPropertyRelative("uniqueID").intValue = next++;
                e.FindPropertyRelative("locked").intValue = 0;
            }
            tm.ApplyModifiedPropertiesWithoutUndo();
        }

        private static CRPGCameraSettings Settings()
        {
            const string p = "Assets/Settings/BarrowsEnd_Camera.asset";
            var s = AssetDatabase.LoadAssetAtPath<CRPGCameraSettings>(p);
            if (s != null) return s;
            s = ScriptableObject.CreateInstance<CRPGCameraSettings>();
            AssetDatabase.CreateAsset(s, p);
            var so = new SerializedObject(s);
            so.FindProperty("minimumPitch").floatValue = 90f;
            so.FindProperty("maximumPitch").floatValue = 90f;
            so.FindProperty("defaultPitch").floatValue = 90f;
            so.FindProperty("defaultZoom").floatValue = 20f;
            so.FindProperty("minimumZoom").floatValue = 5f;
            so.FindProperty("maximumZoom").floatValue = 40f;
            so.ApplyModifiedPropertiesWithoutUndo();
            AssetDatabase.SaveAssets();
            return s;
        }

        private static SpriteRenderer Quad(string name, Transform parent, Sprite sprite, Vector3 local, string layer, int order)
        {
            var go = new GameObject(name);
            if (parent != null) go.transform.SetParent(parent, false);
            go.transform.localPosition = local;
            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = sprite; sr.sortingLayerName = layer; sr.sortingOrder = order;
            return sr;
        }

        private static SpikeCharacter MakeCharacter(string name, Vector3 pos, Sprite[] stack, float walk)
        {
            var root = new GameObject(name);
            root.transform.position = pos;
            var card = new GameObject("Card").transform; card.SetParent(root.transform, false);
            card.localPosition = new Vector3(0f, 0.02f, 0f);
            card.rotation = Quaternion.Euler(90f, 0f, 0f);
            var rock = new GameObject("RockPivot").transform; rock.SetParent(card, false);
            rock.localPosition = new Vector3(0f, -0.5f, 0f);
            var layersRoot = new GameObject("Layers").transform; layersRoot.SetParent(rock, false);
            layersRoot.localPosition = new Vector3(0f, 0.5f, 0f);
            var ch = root.AddComponent<SpikeCharacter>();
            ch.card = card; ch.rockPivot = rock; ch.walkRange = walk;
            for (var i = 0; i < stack.Length; i++)
                if (stack[i] != null) ch.layers.Add(Quad("L" + i, layersRoot, stack[i], Vector3.zero, "World", 0));
            ch.ApplySort(); ch.ApplyPose();
            return ch;
        }

        [MenuItem("Barrows End/Spike/Build Look Spike")]
        public static void Build()
        {
            EnsureSortingLayers("Ground", "World");
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            // Real camera rig at pitch 90, orthographic, pixel-perfect size for 1080p at integer scale 4.
            var rig = new GameObject("CameraRoot");
            var yaw = new GameObject("YawPivot"); yaw.transform.SetParent(rig.transform, false);
            var pitch = new GameObject("PitchPivot"); pitch.transform.SetParent(yaw.transform, false);
            var camGo = new GameObject("Main Camera") { tag = "MainCamera" }; camGo.transform.SetParent(pitch.transform, false);
            var cam = camGo.AddComponent<Camera>();
            cam.orthographic = true; cam.orthographicSize = 1080f / (2f * 16f * 4f);
            cam.clearFlags = CameraClearFlags.SolidColor; cam.backgroundColor = new Color(0.08f, 0.07f, 0.1f);
            cam.nearClipPlane = 0.1f; cam.farClipPlane = 100f;
            camGo.AddComponent<UnityEngine.Rendering.Universal.UniversalAdditionalCameraData>();
            var irig = rig.AddComponent<IsometricCameraRig>();
            var so = new SerializedObject(irig);
            so.FindProperty("yawPivot").objectReferenceValue = yaw.transform;
            so.FindProperty("pitchPivot").objectReferenceValue = pitch.transform;
            so.FindProperty("cameraTransform").objectReferenceValue = camGo.transform;
            so.FindProperty("settings").objectReferenceValue = Settings();
            so.ApplyModifiedPropertiesWithoutUndo();
            rig.transform.position = new Vector3(6f, 0f, 4f);

            // Floor: grey stone with a brown path. Ground layer, flat on XZ.
            var floor = new GameObject("Floor").transform;
            for (var x = 0; x < 12; x++)
            for (var z = 0; z < 8; z++)
            {
                var spr = (z == 3 || z == 4) ? S("d", 16 + (x % 4), 12) : S("d", 16 + (x % 4), 10);
                var sr = Quad($"t{x}_{z}", floor, spr, new Vector3(x + 0.5f, 0f, z + 0.5f), "Ground", 0);
                sr.transform.rotation = Quaternion.Euler(90f, 0f, 0f);
            }

            // Props sharing the World y-sort with characters: crates and a two-cell pillar.
            var props = new (Sprite s, Vector3 p)[] {
                (S("d", 10, 5), new Vector3(3.5f, 0.02f, 5.5f)), (S("d", 11, 5), new Vector3(8.5f, 0.02f, 2.5f)),
                (S("d", 3, 5), new Vector3(6.5f, 0.02f, 6.5f)), (S("d", 3, 6), new Vector3(6.5f, 0.02f, 5.5f)) };
            foreach (var (s, p) in props)
            {
                var g = Quad("prop", null, s, p, "World", -Mathf.RoundToInt(p.z * 8f) * 16 - 8);
                g.transform.rotation = Quaternion.Euler(90f, 0f, 0f);
            }

            // Characters: body(c0-1,r0-3) / clothes(6+) / hair(19+) / helmet(28+) / shield(33+) / weapon(42+).
            MakeCharacter("Bare", new Vector3(2.5f, 0f, 3.5f), new[] { S("c", 0, 0) }, 0f);
            MakeCharacter("Outfit", new Vector3(3.5f, 0f, 3.5f), new[] { S("c", 0, 1), S("c", 6, 0), S("c", 19, 0) }, 0f);
            MakeCharacter("Knight", new Vector3(4.5f, 0f, 3.5f), new[] { S("c", 0, 2), S("c", 8, 4), S("c", 28, 0), S("c", 33, 0), S("c", 42, 0) }, 0f);
            MakeCharacter("Walker", new Vector3(3.5f, 0f, 4.5f), new[] { S("c", 0, 0), S("c", 10, 0), S("c", 20, 0), S("c", 33, 1), S("c", 43, 0) }, 4f);
            MakeCharacter("Walker2", new Vector3(3.5f, 0f, 2.5f), new[] { S("c", 0, 1), S("c", 12, 0), S("c", 42, 2) }, 3f).speed = 1.2f;
            MakeCharacter("Monster", new Vector3(9.5f, 0f, 3.5f), new[] { S("c", 0, 3), S("c", 6, 2) }, 0f);

            // Custom skeleton bodies (Aseprite-style custom layer) stacked with the stock garment/helmet/shield/weapon layers.
            MakeCharacter("Skeleton", new Vector3(8.5f, 0f, 4.5f), new[] { S("b", 0, 0) }, 0f);
            MakeCharacter("SkeletonWarrior", new Vector3(9.5f, 0f, 4.5f), new[] { S("b", 1, 0), S("c", 14, 4), S("c", 29, 1), S("c", 36, 2), S("c", 44, 1) }, 0f);
            MakeCharacter("SkeletonMage", new Vector3(10.5f, 0f, 3.5f), new[] { S("b", 0, 0), S("c", 16, 0), S("c", 30, 0), S("c", 50, 2) }, 2f).speed = 1.5f;

            Directory.CreateDirectory("Assets/Scenes/Spike");
            EditorSceneManager.SaveScene(scene, ScenePath);
            EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(ScenePath, true) };
            Debug.Log("[BarrowsEnd] Look spike built: " + ScenePath);
        }

        /// <summary>Renders the scene's camera to a PNG (edit-mode, single frame) so un-pink/pitch/sort can be checked headlessly.</summary>
        public static void Screenshot()
        {
            EditorSceneManager.OpenScene(ScenePath);
            var irig = Object.FindFirstObjectByType<IsometricCameraRig>();
            var st = new CameraRigState(irig.Settings);
            irig.transform.position = new Vector3(6f, 0f, 4f);
            var so = new SerializedObject(irig);
            ((Transform)so.FindProperty("pitchPivot").objectReferenceValue).localRotation = Quaternion.Euler(st.Pitch, 0f, 0f);
            ((Transform)so.FindProperty("cameraTransform").objectReferenceValue).localPosition = Vector3.back * st.Zoom;
            var cam = Camera.main;
            foreach (var c in Object.FindObjectsByType<SpikeCharacter>(FindObjectsSortMode.None)) { c.card.rotation = cam.transform.rotation; c.ApplySort(); }
            if (System.Environment.GetEnvironmentVariable("HIDE_FLOOR") == "1") { GameObject.Find("Floor").SetActive(false); foreach (var pr in GameObject.FindObjectsByType<SpriteRenderer>(FindObjectsSortMode.None)) if (pr.name == "prop") pr.gameObject.SetActive(false); }
            var rt = new RenderTexture(1920, 1080, 24);
            cam.targetTexture = rt; cam.Render();
            RenderTexture.active = rt;
            var tex = new Texture2D(1920, 1080, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, 1920, 1080), 0, 0); tex.Apply();
            var outPath = Path.Combine(System.Environment.GetEnvironmentVariable("TEMP"), "sp", "spike.png");
            File.WriteAllBytes(outPath, tex.EncodeToPNG());
            RenderTexture.active = null; cam.targetTexture = null;
            Debug.Log("[BarrowsEnd] Screenshot: " + outPath + " pitch=" + st.Pitch);
        }
    }
}
