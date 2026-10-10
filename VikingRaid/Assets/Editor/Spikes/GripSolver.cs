using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace VikingRaid.EditorTools.Spikes
{
    /// <summary>One solved grip: where a weapon sits in its hand (or socket) so it behaves like a held weapon through the idle AND the attack.</summary>
    [System.Serializable]
    public sealed class SolvedGrip
    {
        public string key;          // "<character id>|<weapon prefab name>"
        public Vector3 euler;       // local rotation under the hand bone / socket
        public Vector3 offset;      // local position (shields are centred on the forearm; blades sit at the grip origin)
        public float cost;
        public string notes;
    }

    [System.Serializable]
    public sealed class SolvedGrips { public List<SolvedGrip> grips = new(); }

    /// <summary>
    /// Derives every melee weapon and shield grip in the roster from the ANIMATION, against each character's own rig. (Task 1's GripLab judged 24 orientations on a single idle frame
    /// and chose "blade up beside the body": that needs the wrist bent 90 degrees and swings backward through the whole attack. The user spotted it in the world renders.)
    /// For each weapon it poses the real character through the UAL idle and every frame of Sword_Attack, tries ~1,700 hand-local orientations (30 degree Euler grid) and scores them on
    /// what a hand can do:
    ///   blades and shafts: the tip leads away from the elbow in the attack (mean angle to the forearm near 35-50 degrees), the flat faces sideways so the edge lines up with the
    ///   knuckles, and at rest the tip points forward (not backward, not up into the face);
    ///   shields: strapped on, face outward along the back of the hand, long axis along the forearm, centred on the forearm.
    /// Bows are excluded (calibrated separately by BowAnimationTests). Output: Assets/Editor/Data/WeaponGrips.json (read by CharacterRosterAuthor) + VikingRaid_Logs/spikes/grip_solution.txt.
    /// </summary>
    public static class GripSolver
    {
        public const string OutputPath = "Assets/Editor/Data/WeaponGrips.json";
        private const string Ual = "Assets/ThirdParty/UAL/UAL1_Standard.fbx";

        private enum Kind { Blade, Shaft, Shield }

        private struct Frame { public Matrix4x4 Hand; public Vector3 Elbow, HandPos; }
        private struct ModelShape { public Vector3 Long, Thin, Centre; }

        [MenuItem("Viking Raid/Task 3/Solve Weapon Grips")]
        public static void Run()
        {
            var outDir = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "..", "VikingRaid_Logs", "spikes"));
            Directory.CreateDirectory(outDir);
            var report = new StringBuilder();
            var clips = AssetDatabase.LoadAllAssetsAtPath(Ual).OfType<AnimationClip>().ToList();
            var idle = clips.First(c => c.name == "Armature|Idle_Loop");
            var attack = clips.First(c => c.name == "Armature|Sword_Attack");
            var candidates = Candidates();
            var result = new SolvedGrips();

            foreach (var entry in CharacterRosterAuthor.Roster)
                foreach (var weapon in entry.Weapons)
                {
                    var name = Path.GetFileNameWithoutExtension(weapon.Prefab);
                    if (name.Contains("Bow")) continue;
                    try { result.grips.Add(Solve(entry, weapon, idle, attack, candidates, report)); }
                    catch (System.Exception e) { report.AppendLine($"== {entry.Id}|{name}: FAILED {e.GetType().Name}: {e.Message}"); Debug.LogError(e); }
                }

            Directory.CreateDirectory(Path.Combine(Directory.GetCurrentDirectory(), "Assets/Editor/Data"));
            File.WriteAllText(OutputPath, JsonUtility.ToJson(result, true));
            File.WriteAllText(Path.Combine(outDir, "grip_solution.txt"), report.ToString());
            AssetDatabase.Refresh();
            Debug.Log($"[GripSolver] solved {result.grips.Count} grips -> {OutputPath}");
        }

        public static SolvedGrips Load()
        {
            var full = Path.Combine(Directory.GetCurrentDirectory(), OutputPath);
            return File.Exists(full) ? JsonUtility.FromJson<SolvedGrips>(File.ReadAllText(full)) : new SolvedGrips();
        }

        public static string Key(string characterId, string weaponPrefabPath) => $"{characterId}|{Path.GetFileNameWithoutExtension(weaponPrefabPath)}";

        private static List<Quaternion> Candidates()
        {
            var result = new List<Quaternion>(); var seen = new HashSet<string>();
            string K(Vector3 v) => $"{Mathf.RoundToInt(v.x * 20)},{Mathf.RoundToInt(v.y * 20)},{Mathf.RoundToInt(v.z * 20)}";
            for (var x = 0; x < 360; x += 30) for (var y = 0; y < 360; y += 30) for (var z = 0; z < 360; z += 30)
            {
                var q = Quaternion.Euler(x, y, z);
                if (seen.Add(K(q * Vector3.right) + "|" + K(q * Vector3.up))) result.Add(q);
            }

            return result;
        }

        private static SolvedGrip Solve(CharacterRosterAuthor.Entry entry, CharacterRosterAuthor.Weapon weapon, AnimationClip idle, AnimationClip attack, List<Quaternion> candidates, StringBuilder report)
        {
            var name = Path.GetFileNameWithoutExtension(weapon.Prefab);
            var kind = name.Contains("Shield") ? Kind.Shield : name.Contains("Spear") || name.Contains("Staff") ? Kind.Shaft : Kind.Blade;
            var left = weapon.Hand == CharacterRosterAuthor.Hand.Left;

            UnityEditor.SceneManagement.EditorSceneManager.NewScene(UnityEditor.SceneManagement.NewSceneSetup.EmptyScene, UnityEditor.SceneManagement.NewSceneMode.Single);
            var character = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(entry.Model));
            var animator = character.GetComponentInChildren<Animator>(); animator.applyRootMotion = false;

            AnimationMode.StartAnimationMode();
            try
            {
                var hand = animator.GetBoneTransform(left ? HumanBodyBones.LeftHand : HumanBodyBones.RightHand);
                var forearm = animator.GetBoneTransform(left ? HumanBodyBones.LeftLowerArm : HumanBodyBones.RightLowerArm);
                if (!string.IsNullOrEmpty(weapon.Socket)) hand = hand.GetComponentsInChildren<Transform>(true).FirstOrDefault(t => t.name == weapon.Socket) ?? hand;

                var model = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(weapon.Prefab));
                foreach (var c in model.GetComponentsInChildren<Collider>(true)) Object.DestroyImmediate(c);
                foreach (var a in model.GetComponentsInChildren<Animator>(true)) Object.DestroyImmediate(a);
                model.transform.SetParent(hand, false); model.transform.localScale = Vector3.one * weapon.Scale;
                model.transform.localPosition = Vector3.zero; model.transform.localRotation = Quaternion.identity;
                var shape = Shape(model);

                // the character's own basis from the shoulder line (sampling applies the clip's original root orientation, so the model can face any way)
                AnimationMode.SampleAnimationClip(animator.gameObject, idle, 0.5f);
                var side = animator.GetBoneTransform(HumanBodyBones.RightUpperArm).position - animator.GetBoneTransform(HumanBodyBones.LeftUpperArm).position; side.y = 0f; side.Normalize();
                var fwd = Vector3.Cross(side, Vector3.up).normalized;
                var inward = left ? side : -side;                           // the palm of a hanging arm faces the thigh
                var idleFrame = Capture(hand, forearm);
                var frames = new List<Frame>();
                for (var i = 0; i <= 24; i++) { AnimationMode.SampleAnimationClip(animator.gameObject, attack, attack.length * i / 24f); frames.Add(Capture(hand, forearm)); }

                var best = (cost: float.MaxValue, q: Quaternion.identity, why: "");
                foreach (var q in candidates)
                {
                    var (cost, why) = kind == Kind.Shield ? ScoreShield(q, shape, idleFrame, inward) : ScoreWeapon(q, shape, kind, idleFrame, frames, fwd, inward);
                    if (cost < best.cost) best = (cost, q, why);
                }

                var offset = Vector3.zero;
                if (kind == Kind.Shield)
                {
                    // centred on the forearm: a hand-length toward the elbow, a little out of the back of the hand
                    var a = (idleFrame.HandPos - idleFrame.Elbow).normalized;
                    var target = idleFrame.HandPos - a * 0.13f - inward * 0.06f;
                    offset = idleFrame.Hand.inverse.MultiplyPoint3x4(target) - best.q * (shape.Centre * weapon.Scale);
                }

                var e = best.q.eulerAngles;
                var euler = new Vector3(Mathf.Round(e.x), Mathf.Round(e.y), Mathf.Round(e.z));
                report.AppendLine($"== {entry.Id}|{name} ({kind}, {(left ? "LEFT" : "RIGHT")} {hand.name}): euler=({euler.x},{euler.y},{euler.z}) offset=({offset.x:F3},{offset.y:F3},{offset.z:F3}) cost {best.cost:F1}  {best.why}");
                return new SolvedGrip { key = Key(entry.Id, weapon.Prefab), euler = euler, offset = offset, cost = best.cost, notes = best.why };
            }
            finally { AnimationMode.StopAnimationMode(); Object.DestroyImmediate(character); }
        }

        private static Frame Capture(Transform hand, Transform forearm) => new() { Hand = hand.localToWorldMatrix, Elbow = forearm.position, HandPos = hand.position };

        private static ModelShape Shape(GameObject model)
        {
            var renderers = model.GetComponentsInChildren<Renderer>(true);
            var worldBounds = renderers[0].bounds;
            foreach (var r in renderers) worldBounds.Encapsulate(r.bounds);
            var corners = new List<Vector3>();
            for (var i = 0; i < 8; i++) corners.Add(model.transform.InverseTransformPoint(worldBounds.center + Vector3.Scale(worldBounds.extents, new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1))));
            var lo = new Vector3(corners.Min(c => c.x), corners.Min(c => c.y), corners.Min(c => c.z));
            var hi = new Vector3(corners.Max(c => c.x), corners.Max(c => c.y), corners.Max(c => c.z));
            var size = hi - lo;
            int Longest(Vector3 s) => s.x >= s.y && s.x >= s.z ? 0 : s.y >= s.z ? 1 : 2;
            int Shortest(Vector3 s) => s.x <= s.y && s.x <= s.z ? 0 : s.y <= s.z ? 1 : 2;
            Vector3 Axis(int i, float sign) => i == 0 ? new Vector3(sign, 0, 0) : i == 1 ? new Vector3(0, sign, 0) : new Vector3(0, 0, sign);
            var l = Longest(size); var t = Shortest(size);
            var longSign = Mathf.Abs(hi[l]) >= Mathf.Abs(lo[l]) ? 1f : -1f;      // toward the end farther from the grip origin: a sword's tip, an axe head, a spear point
            return new ModelShape { Long = Axis(l, longSign), Thin = Axis(t, 1f), Centre = model.transform.InverseTransformPoint(worldBounds.center) };
        }

        private static (float, string) ScoreWeapon(Quaternion q, ModelShape shape, Kind kind, Frame idleFrame, List<Frame> frames, Vector3 fwd, Vector3 inward)
        {
            float sum = 0f, max = 0f;
            foreach (var f in frames)
            {
                var tip = f.Hand.MultiplyVector(q * shape.Long).normalized;
                var angle = Vector3.Angle(tip, (f.HandPos - f.Elbow).normalized);
                sum += angle; if (angle > max) max = angle;
            }

            var mean = sum / frames.Count;
            var target = kind == Kind.Shaft ? 50f : 35f;
            var idleTip = idleFrame.Hand.MultiplyVector(q * shape.Long).normalized;
            var tipForward = Vector3.Dot(idleTip, fwd); var tipUp = idleTip.y;
            var thin = idleFrame.Hand.MultiplyVector(q * shape.Thin).normalized;
            var flatAcrossPalm = Mathf.Abs(Vector3.Dot(thin, inward));        // 0 = the flat faces fore/aft of the swing, so the edge lines up with the knuckles

            var cost = Mathf.Abs(mean - target) + Mathf.Max(0f, max - 110f) * 0.6f + flatAcrossPalm * 45f + Mathf.Max(0f, 0.2f - tipForward) * 120f + Mathf.Max(0f, tipUp - 0.35f) * 90f;
            return (cost, $"attack tip-vs-forearm mean {mean:F0} max {max:F0}; idle tip (fwd {tipForward:F2}, up {tipUp:F2}); flat-vs-palm {flatAcrossPalm:F2}");
        }

        private static (float, string) ScoreShield(Quaternion q, ModelShape shape, Frame idleFrame, Vector3 inward)
        {
            var face = idleFrame.Hand.MultiplyVector(q * shape.Thin).normalized;
            var longAxis = idleFrame.Hand.MultiplyVector(q * shape.Long).normalized;
            var forearm = (idleFrame.HandPos - idleFrame.Elbow).normalized;
            var faceOut = Vector3.Dot(face, -inward);                         // strapped on: the face looks away from the body
            var along = Mathf.Abs(Vector3.Dot(longAxis, forearm));            // ...its long axis along the forearm
            return ((1f - faceOut) * 90f + (1f - along) * 60f, $"face-out {faceOut:F2}, long-along-forearm {along:F2}");
        }
    }
}
