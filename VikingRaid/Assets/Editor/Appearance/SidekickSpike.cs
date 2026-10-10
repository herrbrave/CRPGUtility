using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace VikingRaid.EditorTools.Appearance
{
    /// <summary>Diagnostic for the Sidekick families (humans from the Starter pack, goblins): how the assembled prefabs, the per-part meshes, the skeleton and the material are organised. Writes <c>VikingRaid_Logs/spikes/sidekick_report.txt</c>.</summary>
    public static class SidekickSpike
    {
        private const string Root = "Assets/Synty/SidekickCharacters";

        public static void Inspect()
        {
            var report = new StringBuilder();

            foreach (var path in new[] { Root + "/Characters/HumanSpecies/HumanSpecies_01/HumanSpecies_01.prefab", Root + "/Characters/Starter/Starter_01/Starter_01.prefab", Root + "/Characters/GoblinSpecies/GoblinSpecies_01/GoblinSpecies_01.prefab" })
            {
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                report.AppendLine($"== {path}: {(prefab == null ? "MISSING" : "")}");
                if (prefab == null) continue;
                var smrs = prefab.GetComponentsInChildren<SkinnedMeshRenderer>(true);
                var transforms = prefab.GetComponentsInChildren<Transform>(true);
                report.AppendLine($"  transforms {transforms.Length}, skinned renderers {smrs.Length}, animator {(prefab.GetComponentInChildren<Animator>(true) != null)}");
                foreach (var s in smrs)
                    report.AppendLine($"  SMR '{s.name}' mesh '{(s.sharedMesh != null ? s.sharedMesh.name + " v" + s.sharedMesh.vertexCount + " sub" + s.sharedMesh.subMeshCount : "none")}' bones {s.bones.Length} root '{(s.rootBone != null ? s.rootBone.name : "-")}' mats {string.Join(",", s.sharedMaterials.Select(m => m != null ? m.name + "/" + m.shader.name : "null"))}");
                var skeleton = string.Join(" ", transforms.Select(t => t.name).Take(70));
                report.AppendLine("  transforms: " + skeleton);
            }

            foreach (var matPath in new[] { Root + "/Characters/HumanSpecies/HumanSpecies_01/Materials/HumanSpecies_01.mat", Root + "/Characters/GoblinSpecies/GoblinSpecies_01/Materials/GoblinSpecies_01.mat" })
            {
                var m = AssetDatabase.LoadAssetAtPath<Material>(matPath);
                report.AppendLine($"== material {matPath}: {(m == null ? "MISSING" : m.shader.name)}");
                if (m == null) continue;
                var shader = m.shader;
                for (var i = 0; i < shader.GetPropertyCount(); i++)
                {
                    var n = shader.GetPropertyName(i); var type = shader.GetPropertyType(i);
                    var value = type == UnityEngine.Rendering.ShaderPropertyType.Texture ? (m.GetTexture(n) != null ? m.GetTexture(n).name : "null")
                        : type == UnityEngine.Rendering.ShaderPropertyType.Color ? m.GetColor(n).ToString()
                        : type == UnityEngine.Rendering.ShaderPropertyType.Vector ? m.GetVector(n).ToString() : m.GetFloat(n).ToString("F2");
                    report.AppendLine($"  {n} ({type}) = {value}");
                }
            }

            foreach (var folder in new[] { Root + "/Resources/Meshes/Species/Humans", Root + "/Resources/Meshes/Species/Goblins", Root + "/Resources/Meshes/Outfits/Starter", Root + "/Resources/Meshes/Outfits/GoblinFighters" })
            {
                var files = Directory.Exists(folder) ? Directory.GetFiles(folder, "*.fbx").Select(Path.GetFileNameWithoutExtension).OrderBy(x => x).ToList() : new System.Collections.Generic.List<string>();
                report.AppendLine($"== {folder}: {files.Count} files");
                // group by region code (the 4 letters after the two-digit index) so the part structure is visible
                var groups = files.GroupBy(f => { var p = f.Split('_'); return p.Length > 4 ? p[4].Substring(2) : f; }).OrderBy(g => g.Key);
                foreach (var g in groups) report.AppendLine($"  {g.Key}: {g.Count()}  e.g. {string.Join(", ", g.Take(3))}");
            }

            var sample = AssetDatabase.LoadAssetAtPath<GameObject>(Root + "/Resources/Meshes/Species/Humans/SK_HUMN_BASE_01_01HEAD_HU01.fbx");
            if (sample != null)
            {
                report.AppendLine("== sample part SK_HUMN_BASE_01_01HEAD_HU01.fbx");
                foreach (var s in sample.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                    report.AppendLine($"  SMR '{s.name}' v{s.sharedMesh.vertexCount} bones {s.bones.Length} root '{(s.rootBone != null ? s.rootBone.name : "-")}' mats {string.Join(",", s.sharedMaterials.Select(m => m != null ? m.name : "null"))} blendshapes {s.sharedMesh.blendShapeCount}");
                report.AppendLine($"  transforms {sample.GetComponentsInChildren<Transform>(true).Length}, meshfilters {sample.GetComponentsInChildren<MeshFilter>(true).Length}");
                if (sample.GetComponentInChildren<SkinnedMeshRenderer>(true) is { } first)
                    report.AppendLine("  bone order: " + string.Join(" ", first.bones.Take(80).Select(b => b != null ? b.name : "null")));
            }

            var outDir = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "..", "VikingRaid_Logs", "spikes"));
            Directory.CreateDirectory(outDir);
            File.WriteAllText(Path.Combine(outDir, "sidekick_report.txt"), report.ToString());
            Debug.Log("[SidekickSpike] report written");
        }
    }
}
