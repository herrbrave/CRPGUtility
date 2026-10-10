using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace VikingRaid.EditorTools.Appearance
{
    /// <summary>
    /// Gate spike for a Sidekick creator: can the raw per-region part meshes be assembled onto the shared skeleton exactly like the Viking master (renderers bound by BONE NAME), what do the blend shapes
    /// offer, and what do the colour maps do? Writes <c>appearance_sidekick_*.png</c> and <c>sidekick_assemble_report.txt</c> to <c>VikingRaid_Logs/spikes</c>. Needs graphics.
    /// </summary>
    public static class SidekickAssembleSpike
    {
        private const string Root = "Assets/Synty/SidekickCharacters";
        private const string Humans = Root + "/Resources/Meshes/Species/Humans";
        private const string Outfits = Root + "/Resources/Meshes/Outfits/Starter";
        private const string SkeletonPrefab = Root + "/Characters/HumanSpecies/HumanSpecies_01/HumanSpecies_01.prefab";
        private const string BaseMaterial = Root + "/Characters/HumanSpecies/HumanSpecies_01/Materials/HumanSpecies_01.mat";
        private static readonly string[] BodyRegions = { "10TORS", "11AUPL", "12AUPR", "13ALWL", "14ALWR", "15HNDL", "16HNDR", "17HIPS", "18LEGL", "19LEGR", "20FOTL", "21FOTR" };

        public static void Run()
        {
            var outDir = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "..", "VikingRaid_Logs", "spikes"));
            Directory.CreateDirectory(outDir);
            var report = new StringBuilder();
            var previousAsync = EditorSettings.asyncShaderCompilation;
            EditorSettings.asyncShaderCompilation = false;
            var utility = new PreviewRenderUtility();
            try
            {
                utility.camera.fieldOfView = 28f; utility.camera.nearClipPlane = 0.03f; utility.camera.farClipPlane = 60f;
                utility.camera.clearFlags = CameraClearFlags.SolidColor; utility.camera.backgroundColor = new Color(0.2f, 0.22f, 0.26f);
                utility.ambientColor = new Color(0.55f, 0.55f, 0.6f);
                utility.lights[0].intensity = 1.15f; utility.lights[0].transform.rotation = Quaternion.Euler(35f, 150f, 0f);
                utility.lights[1].intensity = 0.8f; utility.lights[1].transform.rotation = Quaternion.Euler(335f, 330f, 0f);

                BlendShapeReport(report);
                var colourMaps = AssetDatabase.FindAssets("t:Material", new[] { Root + "/Characters" }).Select(AssetDatabase.GUIDToAssetPath).Where(p => p.Contains("Species_0") && p.Contains("Human") || p.Contains("Starter_0")).OrderBy(p => p).ToList();
                report.AppendLine("colour-map materials: " + string.Join(", ", colourMaps.Select(Path.GetFileNameWithoutExtension)));

                // warm-up
                var warm = Assemble(1, null, "10", false); Shoot(utility, warm, 220, 220, false); Shoot(utility, warm, 220, 220, false); Object.DestroyImmediate(warm);

                const int tile = 220;
                // sheet 1: the 10 face variants (head 01, every other face part from species N), full figure and head
                var sheet1 = new Texture2D(10 * tile, 2 * tile, TextureFormat.RGBA32, false);
                for (var n = 1; n <= 10; n++)
                {
                    var human = Assemble(n, null, "10", false);
                    sheet1.SetPixels32((n - 1) * tile, tile, tile, tile, Shoot(utility, human, tile, tile, false));
                    sheet1.SetPixels32((n - 1) * tile, 0, tile, tile, Shoot(utility, human, tile, tile, true));
                    Object.DestroyImmediate(human);
                }

                Save(sheet1, Path.Combine(outDir, "appearance_sidekick_faces.png"));

                // sheet 2: outfits (none, knight, civilian 09, civilian 10, villain head) on face 3
                var outfits = new (string Name, string Set)[] { ("bare", null), ("knight", "SK_FANT_KNGT_17"), ("civilian09", "SK_SCFI_CIVL_09"), ("civilian10", "SK_SCFI_CIVL_10") };
                var sheet2 = new Texture2D(outfits.Length * tile, 2 * tile, TextureFormat.RGBA32, false);
                for (var i = 0; i < outfits.Length; i++)
                {
                    var human = Assemble(3, outfits[i].Set, "10", false);
                    sheet2.SetPixels32(i * tile, tile, tile, tile, Shoot(utility, human, tile, tile, false));
                    sheet2.SetPixels32(i * tile, 0, tile, tile, Shoot(utility, human, tile, tile, true));
                    Object.DestroyImmediate(human);
                }

                Save(sheet2, Path.Combine(outDir, "appearance_sidekick_outfits.png"));

                // sheet 3: every colour map on face 2 with the knight outfit
                var sheet3 = new Texture2D(Mathf.Max(colourMaps.Count, 1) * tile, 2 * tile, TextureFormat.RGBA32, false);
                for (var i = 0; i < colourMaps.Count; i++)
                {
                    var human = Assemble(2, "SK_FANT_KNGT_17", "10", false);
                    var material = AssetDatabase.LoadAssetAtPath<Material>(colourMaps[i]);
                    foreach (var r in human.GetComponentsInChildren<Renderer>()) r.sharedMaterial = material;
                    sheet3.SetPixels32(i * tile, tile, tile, tile, Shoot(utility, human, tile, tile, false));
                    sheet3.SetPixels32(i * tile, 0, tile, tile, Shoot(utility, human, tile, tile, true));
                    Object.DestroyImmediate(human);
                }

                Save(sheet3, Path.Combine(outDir, "appearance_sidekick_colours.png"));

                // sheet 4: blend shape extremes
                var shapes = BlendShapeNames();
                var picks = shapes.Where(s => s.ToLowerInvariant().Contains("masc") || s.ToLowerInvariant().Contains("fem") || s.ToLowerInvariant().Contains("heav") || s.ToLowerInvariant().Contains("buff") || s.ToLowerInvariant().Contains("skin")).Take(8).ToList();
                var sheet4 = new Texture2D(Mathf.Max(picks.Count + 1, 2) * tile, tile, TextureFormat.RGBA32, false);
                for (var i = -1; i < picks.Count; i++)
                {
                    var human = Assemble(2, "SK_SCFI_CIVL_09", "10", false);
                    if (i >= 0) foreach (var smr in human.GetComponentsInChildren<SkinnedMeshRenderer>()) { var idx = smr.sharedMesh.GetBlendShapeIndex(picks[i]); if (idx >= 0) smr.SetBlendShapeWeight(idx, 100f); }
                    sheet4.SetPixels32((i + 1) * tile, 0, tile, tile, Shoot(utility, human, tile, tile, false));
                    Object.DestroyImmediate(human);
                }

                report.AppendLine("blend shapes rendered at 100: " + string.Join(", ", picks));
                Save(sheet4, Path.Combine(outDir, "appearance_sidekick_shapes.png"));
            }
            finally { utility.Cleanup(); EditorSettings.asyncShaderCompilation = previousAsync; }

            File.WriteAllText(Path.Combine(outDir, "sidekick_assemble_report.txt"), report.ToString());
            Debug.Log("[SidekickAssembleSpike] done");
        }

        private static List<string> BlendShapeNames()
        {
            var part = AssetDatabase.LoadAssetAtPath<GameObject>(Humans + "/SK_HUMN_BASE_01_10TORS_HU01.fbx");
            var mesh = part.GetComponentInChildren<SkinnedMeshRenderer>().sharedMesh;
            return Enumerable.Range(0, mesh.blendShapeCount).Select(mesh.GetBlendShapeName).ToList();
        }

        private static void BlendShapeReport(StringBuilder report)
        {
            foreach (var file in new[] { "SK_HUMN_BASE_01_10TORS_HU01", "SK_HUMN_BASE_01_01HEAD_HU01", "SK_HUMN_BASE_01_02HAIR_HU01" })
            {
                var part = AssetDatabase.LoadAssetAtPath<GameObject>($"{Humans}/{file}.fbx");
                var mesh = part.GetComponentInChildren<SkinnedMeshRenderer>().sharedMesh;
                report.AppendLine($"{file}: v{mesh.vertexCount} blend shapes ({mesh.blendShapeCount}): " + string.Join(", ", Enumerable.Range(0, mesh.blendShapeCount).Select(mesh.GetBlendShapeName)));
            }
        }

        /// <summary>The skeleton (HumanSpecies_01 with its combined mesh removed) dressed with raw parts bound by bone name.</summary>
        internal static GameObject Assemble(int species, string outfitSet, string torsoCode, bool withFacialHair, bool withHair = true)
        {
            var skeleton = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(SkeletonPrefab));
            PrefabUtility.UnpackPrefabInstance(skeleton, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
            skeleton.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            foreach (var combined in skeleton.GetComponentsInChildren<SkinnedMeshRenderer>(true)) Object.DestroyImmediate(combined.gameObject);
            var animator = skeleton.GetComponentInChildren<Animator>(true);
            var bones = skeleton.GetComponentsInChildren<Transform>(true).GroupBy(t => t.name).ToDictionary(g => g.Key, g => g.First());
            var meshParent = skeleton.transform;
            var material = AssetDatabase.LoadAssetAtPath<Material>(BaseMaterial);

            void Add(string path)
            {
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                if (prefab == null) { Debug.LogWarning("[SidekickAssembleSpike] missing " + path); return; }
                var clone = Object.Instantiate(prefab);
                foreach (var smr in clone.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                {
                    var mapped = smr.bones.Select(b => b != null && bones.TryGetValue(b.name, out var m) ? m : null).ToArray();
                    if (mapped.Any(m => m == null)) Debug.LogWarning($"[SidekickAssembleSpike] {path}: {mapped.Count(m => m == null)} bones unmapped");
                    smr.bones = mapped;
                    smr.rootBone = smr.rootBone != null && bones.TryGetValue(smr.rootBone.name, out var r) ? r : bones["root"];
                    smr.sharedMaterials = Enumerable.Repeat(material, smr.sharedMaterials.Length).ToArray();
                    smr.transform.SetParent(meshParent, false);
                    smr.transform.localPosition = Vector3.zero; smr.transform.localRotation = Quaternion.identity; smr.transform.localScale = Vector3.one;
                }

                Object.DestroyImmediate(clone);
            }

            var n = species.ToString("00");
            Add($"{Humans}/SK_HUMN_BASE_01_01HEAD_HU01.fbx");
            foreach (var region in new[] { withHair ? "02HAIR" : null, "03EBRL", "04EBRR", "07EARL", "08EARR", "35NOSE", "36TETH" }) if (region != null) Add($"{Humans}/SK_HUMN_BASE_{n}_{region}_HU01.fbx");
            if (withFacialHair) Add($"{Humans}/SK_HUMN_BASE_{n}_09FCHR_HU01.fbx");
            Add($"{Humans}/SK_HUMN_BASE_01_05EYEL_HU01.fbx"); Add($"{Humans}/SK_HUMN_BASE_01_06EYER_HU01.fbx");
            Add($"{Humans}/SK_HUMN_BASE_01_37TONG_HU01.fbx");
            foreach (var region in BodyRegions)
            {
                var outfit = outfitSet != null ? $"{Outfits}/{outfitSet}_{region}_HU01.fbx" : null;
                if (outfit != null && File.Exists(outfit)) Add(outfit); else Add($"{Humans}/SK_HUMN_BASE_01_{region}_HU01.fbx");
            }

            if (animator != null) animator.enabled = false;
            return skeleton;
        }

        private static Color32[] Shoot(PreviewRenderUtility utility, GameObject model, int width, int height, bool head)
        {
            utility.AddSingleGO(model);
            var rect = new Rect(0, 0, width, height);
            utility.BeginPreview(rect, GUIStyle.none);
            var renderers = model.GetComponentsInChildren<Renderer>().Where(r => r.enabled).ToArray();
            var b = renderers[0].bounds; foreach (var r in renderers) b.Encapsulate(r.bounds);
            var halfFov = utility.camera.fieldOfView * 0.5f * Mathf.Deg2Rad;
            var headBone = model.GetComponentsInChildren<Transform>(true).FirstOrDefault(t => t.name == "head");
            var centre = head && headBone != null ? headBone.position + Vector3.up * 0.04f : b.center;
            var distance = head ? 0.5f / Mathf.Tan(halfFov) : b.size.y * 0.58f / Mathf.Tan(halfFov);
            utility.camera.transform.position = centre + new Vector3(0f, 0.05f, distance);
            utility.camera.transform.rotation = Quaternion.LookRotation(centre - utility.camera.transform.position, Vector3.up);
            utility.Render(true);
            var texture = utility.EndPreview();
            var rt = RenderTexture.GetTemporary(width, height, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
            Graphics.Blit(texture, rt);
            var previous = RenderTexture.active; RenderTexture.active = rt;
            var cell = new Texture2D(width, height, TextureFormat.RGBA32, false);
            cell.ReadPixels(new Rect(0, 0, width, height), 0, 0);
            var pixels = cell.GetPixels32();
            RenderTexture.active = previous; RenderTexture.ReleaseTemporary(rt); Object.DestroyImmediate(cell);
            return pixels;
        }

        private static void Save(Texture2D sheet, string path)
        {
            sheet.Apply();
            File.WriteAllBytes(path, sheet.EncodeToPNG());
            Object.DestroyImmediate(sheet);
        }
    }
}
