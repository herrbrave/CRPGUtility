using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace VikingRaid.EditorTools.Spikes
{
    /// <summary>Task 1 measurement: the ground speed (m/s) a locomotion clip implies, so each cue's <c>referenceSpeed</c> -- and the
    /// NavMeshAgent speed that goes with it -- is measured, not guessed. Method: sample the clip on a real model with AnimationMode, find
    /// the PLANTED foot (lowest foot, within 3 cm of the clip's minimum height) and take how fast it slides backward relative to the body;
    /// a planted foot moves at exactly the ground speed. Validated against two clips whose true speed is known from the UAL root-motion
    /// twin's <c>averageSpeed</c> (Walk_Loop 0.95 m/s, Jog_Fwd_Loop 4.83 m/s). The Synty goblin root-motion twins report averageSpeed 0
    /// (no usable root curves), which is why this method exists. Output: VikingRaid_Logs/spikes/locomotion_speeds.txt.</summary>
    public static class LocomotionSpeeds
    {
        private const string Ual = "Assets/ThirdParty/UAL/UAL1_Standard.fbx";
        private const string Poly = "Assets/Synty/AnimationGoblinLocomotion/Animations/Polygon/Neutral/Locomotion";
        private const string Viking = "Assets/Synty/PolygonVikingRealm/Prefabs/Characters/SM_Chr_Warrior_Male_01.prefab";
        private const string Goblin = "Assets/Synty/PolygonGoblinWarCamp/Prefabs/Characters/SM_Chr_Warrior_Male_01.prefab";
        private const string Sidekick = "Assets/Synty/SidekickCharacters/Characters/GoblinFighters/GoblinFighter_01/GoblinFighter_01.prefab";
        private const int Samples = 120;

        [MenuItem("Viking Raid/Spikes/Measure Locomotion Speeds")]
        public static void Run()
        {
            var sb = new StringBuilder();
            sb.AppendLine("clip on model: planted-foot ground speed (m/s). VALIDATION rows must match the root-motion averageSpeed in brackets.");
            Measure(sb, Viking, UalClip("Walk_Loop"), "UAL Walk_Loop on Viking [VALIDATION true 0.95]");
            Measure(sb, Viking, UalClip("Jog_Fwd_Loop"), "UAL Jog_Fwd_Loop on Viking [VALIDATION true 4.83]");
            Measure(sb, Viking, UalClip("Sprint_Loop"), "UAL Sprint_Loop on Viking [true 7.73]");
            Measure(sb, Goblin, PolyClip("Walk/A_POLY_GBL_Walk_F_Neut"), "Goblin Walk_F on War Camp goblin");
            Measure(sb, Goblin, PolyClip("Run/A_POLY_GBL_Run_F_Neut"), "Goblin Run_F on War Camp goblin");
            Measure(sb, Sidekick, PolyClip("Walk/A_POLY_GBL_Walk_F_Neut"), "Goblin Walk_F on Sidekick goblin");
            Measure(sb, Sidekick, PolyClip("Run/A_POLY_GBL_Run_F_Neut"), "Goblin Run_F on Sidekick goblin");

            var outDir = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "..", "VikingRaid_Logs", "spikes"));
            Directory.CreateDirectory(outDir);
            File.WriteAllText(Path.Combine(outDir, "locomotion_speeds.txt"), sb.ToString());
            Debug.Log("[LocomotionSpeeds]\n" + sb);
        }

        private static AnimationClip UalClip(string name) =>
            AssetDatabase.LoadAllAssetsAtPath(Ual).OfType<AnimationClip>().First(c => c.name == "Armature|" + name);

        private static AnimationClip PolyClip(string relative) =>
            AssetDatabase.LoadAllAssetsAtPath($"{Poly}/{relative}.fbx").OfType<AnimationClip>().First(c => !c.name.StartsWith("__preview__"));

        private static void Measure(StringBuilder sb, string modelPath, AnimationClip clip, string label)
        {
            var character = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(modelPath));
            var animator = character.GetComponentInChildren<Animator>();
            animator.applyRootMotion = false;
            var left = animator.GetBoneTransform(HumanBodyBones.LeftFoot);
            var right = animator.GetBoneTransform(HumanBodyBones.RightFoot);
            var leftArm = animator.GetBoneTransform(HumanBodyBones.LeftUpperArm);
            var rightArm = animator.GetBoneTransform(HumanBodyBones.RightUpperArm);

            var feet = new List<(float t, Vector2[] p)>(); // per foot: (forward, height) in the body's frame
            AnimationMode.StartAnimationMode();
            try
            {
                for (var i = 0; i <= Samples; i++)
                {
                    var t = clip.length * i / Samples;
                    AnimationMode.SampleAnimationClip(animator.gameObject, clip, t);
                    var side = rightArm.position - leftArm.position; side.y = 0f; side.Normalize();
                    var fwd = Vector3.Cross(side, Vector3.up).normalized;
                    // The body's own position: the midpoint of the shoulders, so a body-relative foot velocity is independent of any root drift.
                    var body = (rightArm.position + leftArm.position) * 0.5f;
                    feet.Add((t, new[]
                    {
                        new Vector2(Vector3.Dot(left.position - body, fwd), left.position.y),
                        new Vector2(Vector3.Dot(right.position - body, fwd), right.position.y),
                    }));
                }
            }
            finally
            {
                AnimationMode.StopAnimationMode();
                Object.DestroyImmediate(character);
            }

            var speeds = new List<float>();
            for (var foot = 0; foot < 2; foot++)
            {
                var minY = feet.Min(f => f.p[foot].y);
                for (var i = 1; i < feet.Count; i++)
                {
                    var a = feet[i - 1]; var b = feet[i];
                    if (a.p[foot].y > minY + 0.03f || b.p[foot].y > minY + 0.03f) continue; // not planted
                    speeds.Add(-(b.p[foot].x - a.p[foot].x) / (b.t - a.t));
                }
            }

            speeds.Sort();
            var median = speeds.Count > 0 ? speeds[speeds.Count / 2] : float.NaN;
            sb.AppendLine($"  {label}: {median:F2} m/s (clip {clip.length:F2}s, {speeds.Count} planted samples)");
        }
    }
}
