using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace VikingRaid.Appearance.Editor
{
    /// <summary>Diagnostic: how does an imported part FBX compare with Synty's own for the same region (mesh bounds, bind poses, renderer and root transforms, bones)? Writes <c>VikingRaid_Logs/spikes/part_import_audit.txt</c>.</summary>
    public static class PartImportAudit
    {
        public static void Run()
        {
            var report = new StringBuilder();
            foreach (var path in new[]
            {
                "Assets/Synty/SidekickCharacters/Resources/Meshes/Species/Humans/SK_HUMN_BASE_01_10TORS_HU01.fbx",
                "Assets/Appearance/SidekickHumans/Outfits/VikingTunic/SK_VKNG_TUNC_01_10TORS_HU01.fbx",
                "Assets/Synty/SidekickCharacters/Resources/Meshes/Species/Humans/SK_HUMN_BASE_01_18LEGL_HU01.fbx",
                "Assets/Appearance/SidekickHumans/Outfits/VikingTunic/SK_VKNG_TUNC_01_18LEGL_HU01.fbx",
            })
            {
                var go = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                report.AppendLine($"== {Path.GetFileName(path)}: root '{go.name}' pos {go.transform.localPosition} rot {go.transform.localEulerAngles} scale {go.transform.localScale}");
                var importer = (ModelImporter)AssetImporter.GetAtPath(path);
                report.AppendLine($"  importer: globalScale {importer.globalScale}, useFileScale {importer.useFileScale}, fileScale {importer.fileScale}, animationType {importer.animationType}");
                foreach (var smr in go.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                {
                    var m = smr.sharedMesh;
                    report.AppendLine($"  SMR '{smr.name}' local pos {smr.transform.localPosition} rot {smr.transform.localEulerAngles} scale {smr.transform.localScale}; parent '{(smr.transform.parent != null ? smr.transform.parent.name : "-")}'");
                    report.AppendLine($"    mesh bounds centre {m.bounds.center} size {m.bounds.size}, submeshes {m.subMeshCount}, verts {m.vertexCount}, bones {smr.bones.Length}, blendshapes {m.blendShapeCount}, rootBone {(smr.rootBone != null ? smr.rootBone.name : "-")}");
                    var weighted = m.GetAllBoneWeights().Where(w => w.weight > 0.001f).Select(w => w.boneIndex).Distinct().Take(3).ToList();
                    foreach (var i in weighted)
                    {
                        var bone = smr.bones[i];
                        report.AppendLine($"    bone[{i}] '{bone.name}' world pos {bone.position} lossyScale {bone.lossyScale}; bindpose pos {(Vector3)m.bindposes[i].inverse.GetColumn(3)} (inverse), bindpose scale {m.bindposes[i].lossyScale}");
                    }
                }

                var bones = go.GetComponentsInChildren<Transform>(true);
                var head = bones.FirstOrDefault(t => t.name == "head");
                report.AppendLine($"  transforms {bones.Length}, head bone world pos {(head != null ? head.position.ToString() : "-")}, root world scale {go.transform.lossyScale}");
            }

            var outDir = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "..", "VikingRaid_Logs", "spikes"));
            File.WriteAllText(Path.Combine(outDir, "part_import_audit.txt"), report.ToString());
            Debug.Log("[PartImportAudit] done");
        }
    }
}
