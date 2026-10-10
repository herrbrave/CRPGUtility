using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace VikingRaid.EditorTools.Spikes
{
    /// <summary>Task 1 recon: prints hand/socket-like transforms of the character rigs and the local bounds of candidate weapon
    /// prefabs so weapon grips can be authored from evidence (output: VikingRaid_Logs/spikes/rig_dump.txt).</summary>
    public static class RigDump
    {
        private static readonly string[] Characters =
        {
            "Assets/Synty/PolygonVikingRealm/Prefabs/Characters/SM_Chr_Warrior_Male_01.prefab",
            "Assets/Synty/SidekickCharacters/Characters/GoblinFighters/GoblinFighter_01/GoblinFighter_01.prefab",
            "Assets/Synty/PolygonGoblinWarCamp/Prefabs/Characters/SM_Chr_Troll_01.prefab",
        };

        [MenuItem("Viking Raid/Spikes/Dump Rigs And Weapons")]
        public static void Run()
        {
            var sb = new StringBuilder();
            foreach (var path in Characters)
            {
                var go = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(path));
                sb.AppendLine($"== {path}");
                var animator = go.GetComponentInChildren<Animator>();
                foreach (var bone in new[] { HumanBodyBones.RightHand, HumanBodyBones.LeftHand, HumanBodyBones.RightLowerArm, HumanBodyBones.Spine, HumanBodyBones.UpperChest })
                {
                    var t = animator != null ? animator.GetBoneTransform(bone) : null;
                    sb.AppendLine($"  {bone}: {(t == null ? "NONE" : PathOf(t, go.transform))} lossyScale={(t == null ? Vector3.zero : t.lossyScale)}");
                }

                foreach (var t in go.GetComponentsInChildren<Transform>(true))
                {
                    var n = t.name.ToLowerInvariant();
                    if (n.Contains("socket") || n.Contains("attach") && !n.StartsWith("sm_") || n.Contains("weapon") || n.Contains("prop") || n.Contains("holster") || n.Contains("grip"))
                        sb.AppendLine($"  node: {PathOf(t, go.transform)}");
                }

                var smrs = go.GetComponentsInChildren<SkinnedMeshRenderer>(true);
                sb.AppendLine($"  SMRs: {smrs.Length} (active {smrs.Count(s => s.gameObject.activeSelf)})");
                Object.DestroyImmediate(go);
            }

            sb.AppendLine("== weapons (local mesh bounds of the prefab root, no scale)");
            foreach (var folder in new[] { "Assets/Synty/PolygonVikingRealm/Prefabs/Weapons", "Assets/Synty/PolygonGoblinWarCamp/Prefabs/Weapons" })
            {
                foreach (var guid in AssetDatabase.FindAssets("t:Prefab", new[] { folder }))
                {
                    var path = AssetDatabase.GUIDToAssetPath(guid);
                    if (path.Contains("Optimised") || path.Contains("Shield_Set")) continue;
                    var go = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(path));
                    var rs = go.GetComponentsInChildren<Renderer>(true);
                    if (rs.Length == 0) { Object.DestroyImmediate(go); continue; }
                    var b = rs[0].bounds;
                    foreach (var r in rs) b.Encapsulate(r.bounds);
                    var extras = string.Join("+", go.GetComponentsInChildren<Component>(true).Select(c => c == null ? "null" : c.GetType().Name).Where(n => n is "Animator" or "BoxCollider" or "MeshCollider" or "SphereCollider" or "CapsuleCollider").Distinct());
                    sb.AppendLine($"  {Path.GetFileNameWithoutExtension(path)} center={b.center:F2} size={b.size:F2} min={b.min:F2} max={b.max:F2} {extras}");
                    Object.DestroyImmediate(go);
                }
            }

            var outDir = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "..", "VikingRaid_Logs", "spikes"));
            Directory.CreateDirectory(outDir);
            File.WriteAllText(Path.Combine(outDir, "rig_dump.txt"), sb.ToString());
            Debug.Log("[RigDump] written");
        }

        private static string PathOf(Transform t, Transform root)
        {
            var parts = new System.Collections.Generic.List<string>();
            for (var c = t; c != null && c != root; c = c.parent) parts.Add(c.name);
            parts.Reverse();
            return string.Join("/", parts);
        }
    }
}
