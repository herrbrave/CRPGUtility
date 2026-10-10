using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace VikingRaid.Appearance.Editor
{
    /// <summary>Diagnostic: the skinned attachments baked into the Viking master (hairs, pelts, furs): vertex counts, material, which bones carry their weights, and the bounds relative to the Head bone. Writes <c>VikingRaid_Logs/spikes/skinned_pieces.txt</c>.</summary>
    public static class SkinnedPieceProbe
    {
        public static void Run()
        {
            var master = AssetDatabase.LoadAssetAtPath<GameObject>(ModelAppearanceCatalogAuthor.MasterPath);
            var report = new StringBuilder();
            var head = master.GetComponentsInChildren<Transform>(true).First(t => t.name == "Head");
            foreach (var smr in master.GetComponentsInChildren<SkinnedMeshRenderer>(true).Where(s => s.name.StartsWith("SM_Chr_Attach_")).OrderBy(s => s.name))
            {
                var mesh = smr.sharedMesh;
                var bones = smr.bones;
                var weights = mesh.GetAllBoneWeights();
                var perBone = weights.GroupBy(w => w.boneIndex).Select(g => (name: bones[g.Key] != null ? bones[g.Key].name : "?", sum: g.Sum(w => w.weight))).OrderByDescending(b => b.sum).ToList();
                var total = perBone.Sum(b => b.sum);
                var top = string.Join(", ", perBone.Take(5).Select(b => $"{b.name} {100f * b.sum / total:F0}%"));
                var b2 = smr.bounds;
                report.AppendLine($"{smr.name,-34} verts {mesh.vertexCount,5} tris {mesh.triangles.Length / 3,5} bones {bones.Length,3} mats [{string.Join(",", smr.sharedMaterials.Select(m => m != null ? m.name : "null"))}] active {smr.gameObject.activeSelf} blend {mesh.blendShapeCount}");
                report.AppendLine($"    weights: {top}");
                report.AppendLine($"    world bounds centre {b2.center - head.position:F3} size {b2.size:F3} (relative to Head bone)");
            }

            File.WriteAllText(Path.GetFullPath(Path.Combine(Application.dataPath, "..", "..", "VikingRaid_Logs", "spikes", "skinned_pieces.txt")), report.ToString());
            Debug.Log("[SkinnedPieceProbe] done");
        }
    }
}
