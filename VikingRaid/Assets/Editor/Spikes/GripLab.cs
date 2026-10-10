using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace VikingRaid.EditorTools.Spikes
{
    /// <summary>Task 1 tool: finds weapon grips from evidence. Poses a character in a sampled UAL idle frame, attaches a clone of a weapon to a
    /// hand in each of the 24 axis-aligned orientations and renders one contact sheet per lab (6 x 4, row-major from top-left, index =
    /// row * 6 + col) plus a report listing each candidate's Euler angles and where the weapon tip points in character space
    /// (x = character's right, y = up, z = forward). Output: VikingRaid_Logs/spikes/grip_*.png + grip_report.txt. Needs graphics.</summary>
    public static class GripLab
    {
        private const int Cell = 256, Cols = 6;

        private sealed class Lab
        {
            public string Name, Character, Weapon, SocketName;
            public bool Left;
            public float Scale = 1f;
        }

        private const string V = "Assets/Synty/PolygonVikingRealm/Prefabs";
        private const string W = "Assets/Synty/PolygonGoblinWarCamp/Prefabs";
        private const string S = "Assets/Synty/SidekickCharacters/Characters/GoblinFighters/GoblinFighter_01/GoblinFighter_01.prefab";

        private static readonly Lab[] Labs =
        {
            new() { Name = "viking_r_sword", Character = $"{V}/Characters/SM_Chr_Warrior_Male_02.prefab", Weapon = $"{V}/Weapons/SM_Wep_Sword_02.prefab" },
            new() { Name = "viking_r_axe", Character = $"{V}/Characters/SM_Chr_Warrior_Male_01.prefab", Weapon = $"{V}/Weapons/SM_Wep_Axe_02.prefab" },
            new() { Name = "viking_l_shield", Character = $"{V}/Characters/SM_Chr_Warrior_Male_02.prefab", Weapon = $"{V}/Weapons/SM_Wep_Shield_01.prefab", Left = true },
            new() { Name = "viking_l_bow", Character = $"{V}/Characters/SM_Chr_Warrior_Female_01.prefab", Weapon = $"{V}/Weapons/SM_Wep_Bow_Rigged_01.prefab", Left = true },
            new() { Name = "sidekick_r_sword", Character = S, Weapon = $"{W}/Weapons/SM_Wep_Sword_01.prefab", SocketName = "prop_r" },
            new() { Name = "sidekick_l_shield", Character = S, Weapon = $"{W}/Weapons/SM_Wep_Shield_01.prefab", SocketName = "prop_l", Left = true },
            new() { Name = "warcamp_r_club", Character = $"{W}/Characters/SM_Chr_Warrior_Male_01.prefab", Weapon = $"{W}/Weapons/SM_Wep_Club_01.prefab" },
            new() { Name = "warcamp_l_bow", Character = $"{W}/Characters/SM_Chr_Archer_Male_01.prefab", Weapon = $"{W}/Weapons/SM_Wep_Bow_Rigged_01.prefab", Left = true },
            new() { Name = "warcamp_r_staff", Character = $"{W}/Characters/SM_Chr_Shaman_01.prefab", Weapon = $"{W}/Weapons/SM_Wep_Staff_01.prefab" },
        };

        [MenuItem("Viking Raid/Spikes/Grip Lab")]
        public static void Run()
        {
            var outDir = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "..", "VikingRaid_Logs", "spikes"));
            Directory.CreateDirectory(outDir);
            var report = new StringBuilder();
            var idle = AssetDatabase.LoadAllAssetsAtPath("Assets/ThirdParty/UAL/UAL1_Standard.fbx").OfType<AnimationClip>().First(c => c.name == "Armature|Idle_Loop");
            var rotations = Candidates();

            foreach (var lab in Labs) RenderLab(lab, idle, rotations, outDir, report);

            File.WriteAllText(Path.Combine(outDir, "grip_report.txt"), report.ToString());
            Debug.Log("[GripLab] done");
        }

        private static List<Quaternion> Candidates()
        {
            var result = new List<Quaternion>();
            var seen = new HashSet<string>();
            foreach (var ax in new[] { 0, 90, 180, 270 })
            foreach (var ay in new[] { 0, 90, 180, 270 })
            foreach (var az in new[] { 0, 90, 180, 270 })
            {
                var q = Quaternion.Euler(ax, ay, az);
                var key = $"{R(q * Vector3.right)}|{R(q * Vector3.up)}|{R(q * Vector3.forward)}";
                if (seen.Add(key)) result.Add(q);
            }

            return result;
        }

        private static string R(Vector3 v) => $"{Mathf.RoundToInt(v.x)},{Mathf.RoundToInt(v.y)},{Mathf.RoundToInt(v.z)}";

        private static void RenderLab(Lab lab, AnimationClip idle, List<Quaternion> rotations, string outDir, StringBuilder report)
        {
            var characterPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(lab.Character);
            var weaponPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(lab.Weapon);
            if (characterPrefab == null || weaponPrefab == null) { report.AppendLine($"== {lab.Name}: MISSING prefab"); return; }

            var scene = UnityEditor.SceneManagement.EditorSceneManager.NewScene(UnityEditor.SceneManagement.NewSceneSetup.EmptyScene, UnityEditor.SceneManagement.NewSceneMode.Single);
            RenderSettings.ambientMode = AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(0.55f, 0.55f, 0.6f);
            var light = new GameObject("Light").AddComponent<Light>();
            light.type = LightType.Directional;
            light.transform.rotation = Quaternion.Euler(35f, 150f, 0f);

            var character = (GameObject)PrefabUtility.InstantiatePrefab(characterPrefab);
            var animator = character.GetComponentInChildren<Animator>();
            animator.applyRootMotion = false;

            AnimationMode.StartAnimationMode();
            try
            {
                AnimationMode.SampleAnimationClip(animator.gameObject, idle, 0.5f);

                var hand = animator.GetBoneTransform(lab.Left ? HumanBodyBones.LeftHand : HumanBodyBones.RightHand);
                if (!string.IsNullOrEmpty(lab.SocketName))
                    hand = hand.GetComponentsInChildren<Transform>(true).FirstOrDefault(t => t.name == lab.SocketName) ?? hand;

                var weapon = Object.Instantiate(weaponPrefab);
                foreach (var c in weapon.GetComponentsInChildren<Collider>(true)) Object.DestroyImmediate(c);
                foreach (var a in weapon.GetComponentsInChildren<Animator>(true)) Object.DestroyImmediate(a);
                weapon.transform.SetParent(hand, false);
                weapon.transform.localScale = Vector3.one * lab.Scale;

                var tipLocal = TipAxis(weapon);

                // AnimationMode sampling applies the clip's ORIGINAL root orientation (the runtime driver does not), so the model can face any
                // way here. Derive the true facing from the shoulder line and judge everything in that basis.
                var rightArm = animator.GetBoneTransform(HumanBodyBones.RightUpperArm).position;
                var leftArm = animator.GetBoneTransform(HumanBodyBones.LeftUpperArm).position;
                var side = rightArm - leftArm; side.y = 0f; side.Normalize();
                var fwd = Vector3.Cross(side, Vector3.up).normalized;
                var camGo = new GameObject("Cam");
                var cam = camGo.AddComponent<Camera>();
                cam.clearFlags = CameraClearFlags.SolidColor;
                cam.backgroundColor = new Color(0.22f, 0.25f, 0.3f);
                cam.fieldOfView = 40f;
                var rt = new RenderTexture(Cell, Cell, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
                cam.targetTexture = rt;

                var rows = Mathf.CeilToInt(rotations.Count / (float)Cols);
                var sheet = new Texture2D(Cols * Cell, rows * Cell, TextureFormat.RGBA32, false);
                var tile = new Texture2D(Cell, Cell, TextureFormat.RGBA32, false);
                report.AppendLine($"== {lab.Name}: {Path.GetFileName(lab.Character)} + {Path.GetFileName(lab.Weapon)} on {(lab.Left ? "LEFT" : "RIGHT")} {hand.name}; weapon local tip axis {tipLocal:F2}; tip direction in the character's own basis from its shoulder line (x its right, y up, z its forward)");

                for (var i = -1; i < rotations.Count; i++) // i = -1: discarded warm-up render (first shader use renders a white placeholder)
                {
                    weapon.transform.localRotation = i < 0 ? Quaternion.identity : rotations[i];
                    weapon.transform.localPosition = Vector3.zero;
                    var focus = hand.position + new Vector3(0f, 0.1f, 0f);
                    camGo.transform.position = focus + fwd * 2.6f + side * (lab.Left ? -0.5f : 0.5f) + Vector3.up * 0.3f;
                    camGo.transform.LookAt(focus);

                    var request = new UniversalRenderPipeline.SingleCameraRequest { destination = rt };
                    if (RenderPipeline.SupportsRenderRequest(cam, request)) RenderPipeline.SubmitRenderRequest(cam, request); else cam.Render();
                    RenderTexture.active = rt;
                    tile.ReadPixels(new Rect(0, 0, Cell, Cell), 0, 0);
                    tile.Apply();
                    RenderTexture.active = null;

                    if (i < 0) continue;
                    var row = rows - 1 - i / Cols;
                    sheet.SetPixels((i % Cols) * Cell, row * Cell, Cell, Cell, tile.GetPixels());

                    var e = rotations[i].eulerAngles;
                    var tipWorld = weapon.transform.TransformDirection(tipLocal).normalized;
                    var tipDir = new Vector3(Vector3.Dot(tipWorld, side), Vector3.Dot(tipWorld, Vector3.up), Vector3.Dot(tipWorld, fwd));
                    report.AppendLine($"  {i,2} r{i / Cols}c{i % Cols} euler=({Mathf.RoundToInt(e.x)},{Mathf.RoundToInt(e.y)},{Mathf.RoundToInt(e.z)}) tip=({tipDir.x:F1},{tipDir.y:F1},{tipDir.z:F1})");
                }

                sheet.Apply();
                File.WriteAllBytes(Path.Combine(outDir, $"grip_{lab.Name}.png"), sheet.EncodeToPNG());
                Object.DestroyImmediate(sheet);
                Object.DestroyImmediate(tile);
                cam.targetTexture = null;
                rt.Release();
                Object.DestroyImmediate(rt);
            }
            finally
            {
                AnimationMode.StopAnimationMode();
            }
        }

        // The weapon's local long axis, signed toward the end farther from the grip origin (a sword's tip, an axe head, a staff's top).
        private static Vector3 TipAxis(GameObject weapon)
        {
            var renderers = weapon.GetComponentsInChildren<Renderer>(true);
            var b = renderers[0].bounds;
            foreach (var r in renderers) b.Encapsulate(r.bounds);
            var min = weapon.transform.InverseTransformPoint(b.min);
            var max = weapon.transform.InverseTransformPoint(b.max);
            var size = b.size;
            var axis = size.x >= size.y && size.x >= size.z ? 0 : size.y >= size.z ? 1 : 2;
            var lo = axis == 0 ? Mathf.Min(min.x, max.x) : axis == 1 ? Mathf.Min(min.y, max.y) : Mathf.Min(min.z, max.z);
            var hi = axis == 0 ? Mathf.Max(min.x, max.x) : axis == 1 ? Mathf.Max(min.y, max.y) : Mathf.Max(min.z, max.z);
            var sign = Mathf.Abs(hi) >= Mathf.Abs(lo) ? 1f : -1f;
            return axis == 0 ? new Vector3(sign, 0, 0) : axis == 1 ? new Vector3(0, sign, 0) : new Vector3(0, 0, sign);
        }
    }
}
