using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace VikingRaid.EditorTools.Appearance
{
    /// <summary>
    /// Gate spike for a Sidekick GOBLIN creator: which raw parts bind to one skeleton (counts the unmapped bones of EVERY part), which colour map suits which parts (a species map vs a fighter map on
    /// species faces, on whole fighter sets and on mixed sets), what the blend shapes do, and how big goblins are. Writes <c>appearance_goblin_*.png</c> and <c>goblin_report.txt</c>. Needs graphics.
    /// </summary>
    public static class SidekickGoblinSpike
    {
        private const string Root = "Assets/Synty/SidekickCharacters";
        private const string Species = Root + "/Resources/Meshes/Species/Goblins";
        private const string Fighters = Root + "/Resources/Meshes/Outfits/GoblinFighters";
        private static readonly string[] BodyRegions = { "10TORS", "11AUPL", "12AUPR", "13ALWL", "14ALWR", "15HNDL", "16HNDR", "17HIPS", "18LEGL", "19LEGR", "20FOTL", "21FOTR" };
        private static readonly string[] Attachments = { "22AHED", "23AFAC", "24ABAC", "25AHPF", "26AHPB", "27AHPL", "28AHPR", "29ASHL", "30ASHR", "31AEBL", "32AEBR", "33AKNL", "34AKNR" };
        private static readonly string[] FaceRegions = { "02HAIR", "03EBRL", "04EBRR", "07EARL", "08EARR", "35NOSE", "36TETH" };

        private static Material Map(string family, int n)
        {
            var single = family == "GoblinSpecies" ? "GoblinSpecies" : "GoblinFighter";
            return AssetDatabase.LoadAssetAtPath<Material>($"{Root}/Characters/{family}/{single}_{n:00}/Materials/{single}_{n:00}.mat");
        }

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

                BoneReport(report);
                BlendShapes(report);
                var speciesMaps = Enumerable.Range(1, 5).Select(n => Map("GoblinSpecies", n)).ToList();
                var fighterMaps = Enumerable.Range(1, 5).Select(n => Map("GoblinFighters", n)).ToList();
                report.AppendLine($"species maps {speciesMaps.Count(m => m != null)}, fighter maps {fighterMaps.Count(m => m != null)}");

                var warm = Build(1, null, null, speciesMaps[0], false); Shoot(utility, warm, 200, false); Shoot(utility, warm, 200, false); Object.DestroyImmediate(warm);
                const int tile = 200;

                // G1: ten species faces, bare, species map 1 (full, head)
                var g1 = new Texture2D(10 * tile, 2 * tile, TextureFormat.RGBA32, false);
                for (var n = 1; n <= 10; n++)
                {
                    var goblin = Build(n, null, null, speciesMaps[0], n % 3 == 0);
                    g1.SetPixels32((n - 1) * tile, tile, tile, tile, Shoot(utility, goblin, tile, false));
                    g1.SetPixels32((n - 1) * tile, 0, tile, tile, Shoot(utility, goblin, tile, true));
                    Object.DestroyImmediate(goblin);
                }

                Save(g1, Path.Combine(outDir, "appearance_goblin_faces.png"));

                // G2: ten whole fighter sets (face parts from the set too); columns = species map 1, fighter map 1, 3, 5
                var maps = new[] { speciesMaps[0], fighterMaps[0], fighterMaps[2], fighterMaps[4] };
                var g2 = new Texture2D(10 * tile, maps.Length * tile, TextureFormat.RGBA32, false);
                for (var k = 1; k <= 10; k++)
                    for (var m = 0; m < maps.Length; m++)
                    {
                        var goblin = Build(1, k, null, maps[m], false, setOwnsFace: true);
                        g2.SetPixels32((k - 1) * tile, (maps.Length - 1 - m) * tile, tile, tile, Shoot(utility, goblin, tile, false));
                        Object.DestroyImmediate(goblin);
                    }

                Save(g2, Path.Combine(outDir, "appearance_goblin_sets.png"));

                // G3: mixed: species face 4 + torso/arms set 2, legs/feet set 7, head attachment set 4 / 9, shoulders set 5; under fighter map 1 and 3
                var g3 = new Texture2D(4 * tile, 2 * tile, TextureFormat.RGBA32, false);
                var mixes = new[] { (2, 7, 4, 5, 0), (3, 3, 9, 9, 2), (6, 1, 2, 8, 0), (8, 10, 10, 1, 2) };
                for (var i = 0; i < mixes.Length; i++)
                {
                    var (upper, lower, head, shoulder, map) = mixes[i];
                    var goblin = Build(4, null, new Dictionary<string, int> { ["upper"] = upper, ["lower"] = lower, ["head"] = head, ["shoulder"] = shoulder }, fighterMaps[map], false);
                    g3.SetPixels32(i * tile, tile, tile, tile, Shoot(utility, goblin, tile, false));
                    g3.SetPixels32(i * tile, 0, tile, tile, Shoot(utility, goblin, tile, true));
                    Object.DestroyImmediate(goblin);
                }

                Save(g3, Path.Combine(outDir, "appearance_goblin_mixed.png"));
            }
            finally { utility.Cleanup(); EditorSettings.asyncShaderCompilation = previousAsync; }

            File.WriteAllText(Path.Combine(outDir, "goblin_report.txt"), report.ToString());
            Debug.Log("[SidekickGoblinSpike] done");
        }

        // ------------------------------------------------------------------ bones
        private static void BoneReport(StringBuilder report)
        {
            foreach (var skeletonPath in new[] { Root + "/Characters/GoblinSpecies/GoblinSpecies_01/GoblinSpecies_01.prefab", Root + "/Characters/GoblinFighters/GoblinFighter_01/GoblinFighter_01.prefab" })
            {
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(skeletonPath);
                var names = new HashSet<string>(prefab.GetComponentsInChildren<Transform>(true).Select(t => t.name));
                report.AppendLine($"== skeleton {Path.GetFileNameWithoutExtension(skeletonPath)}: {names.Count} transforms");
                var missingByBone = new Dictionary<string, int>();
                var parts = 0; var partsWithMissing = 0; var minScale = float.MaxValue; var maxScale = 0f;
                foreach (var folder in new[] { Species, Fighters })
                    foreach (var file in Directory.GetFiles(folder, "*.fbx"))
                    {
                        var part = AssetDatabase.LoadAssetAtPath<GameObject>(file.Replace('\\', '/'));
                        foreach (var smr in part.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                        {
                            parts++;
                            var missing = smr.bones.Where(b => b != null && !names.Contains(b.name)).Select(b => b.name).ToList();
                            if (missing.Count > 0) partsWithMissing++;
                            foreach (var m in missing) missingByBone[m] = missingByBone.TryGetValue(m, out var c) ? c + 1 : 1;
                            var size = smr.sharedMesh.bounds.size.magnitude; minScale = Mathf.Min(minScale, size); maxScale = Mathf.Max(maxScale, size);
                        }
                    }

                report.AppendLine($"  parts {parts}, with unmapped bones {partsWithMissing}; unmapped bone names: " + (missingByBone.Count == 0 ? "none" : string.Join(", ", missingByBone.OrderByDescending(p => p.Value).Take(25).Select(p => p.Key + "x" + p.Value))));
            }
        }

        private static void BlendShapes(StringBuilder report)
        {
            foreach (var file in new[] { Species + "/SK_GOBL_BASE_01_10TORS_GO01.fbx", Species + "/SK_GOBL_BASE_01_01HEAD_GO01.fbx", Fighters + "/SK_GOBL_FIGT_01_10TORS_GO01.fbx" })
            {
                var smr = AssetDatabase.LoadAssetAtPath<GameObject>(file).GetComponentInChildren<SkinnedMeshRenderer>();
                var mesh = smr.sharedMesh;
                var names = Enumerable.Range(0, mesh.blendShapeCount).Select(mesh.GetBlendShapeName).Where(n => !n.Contains("mouth") && !n.Contains("eye") && !n.Contains("brow") && !n.Contains("cheek") && !n.Contains("nose") && !n.Contains("jaw"));
                report.AppendLine($"{Path.GetFileNameWithoutExtension(file)}: v{mesh.vertexCount}, blend shapes {mesh.blendShapeCount} (non-face: {string.Join(", ", names)})");
            }
        }

        // ------------------------------------------------------------------ assembly
        /// <param name="species">Face parts and base body from species N (1-10).</param>
        /// <param name="wholeSet">A fighter set (1-10) applied to body regions AND attachments.</param>
        /// <param name="mix">Alternatively upper/lower/head/shoulder from different sets.</param>
        private static GameObject Build(int species, int? wholeSet, Dictionary<string, int> mix, Material material, bool facialHair, bool setOwnsFace = false)
        {
            var skeleton = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(Root + "/Characters/GoblinSpecies/GoblinSpecies_01/GoblinSpecies_01.prefab"));
            PrefabUtility.UnpackPrefabInstance(skeleton, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
            skeleton.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            foreach (var combined in skeleton.GetComponentsInChildren<SkinnedMeshRenderer>(true)) Object.DestroyImmediate(combined.gameObject);
            var animator = skeleton.GetComponentInChildren<Animator>(true); if (animator != null) animator.enabled = false;
            var bones = skeleton.GetComponentsInChildren<Transform>(true).GroupBy(t => t.name).ToDictionary(g => g.Key, g => g.First());

            void Add(string path)
            {
                if (!File.Exists(path)) return;
                var clone = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(path));
                foreach (var smr in clone.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                {
                    // a missing bone falls back to the nearest present ancestor of the part's own bone, else the root
                    var mapped = smr.bones.Select(b =>
                    {
                        for (var t = b; t != null; t = t.parent) if (bones.TryGetValue(t.name, out var found)) return found;
                        return bones["root"];
                    }).ToArray();
                    smr.bones = mapped;
                    smr.rootBone = smr.rootBone != null && bones.TryGetValue(smr.rootBone.name, out var r) ? r : bones["root"];
                    smr.sharedMaterials = Enumerable.Repeat(material, smr.sharedMaterials.Length).ToArray();
                    smr.transform.SetParent(skeleton.transform, false);
                    smr.transform.localPosition = Vector3.zero; smr.transform.localRotation = Quaternion.identity; smr.transform.localScale = Vector3.one;
                }

                Object.DestroyImmediate(clone);
            }

            string Base(string region) => $"{Species}/SK_GOBL_BASE_{species:00}_{region}_GO01.fbx";
            string Base1(string region) => $"{Species}/SK_GOBL_BASE_01_{region}_GO01.fbx";
            string Set(int k, string region) => $"{Fighters}/SK_GOBL_FIGT_{k:00}_{region}_GO01.fbx";

            Add(Base1("01HEAD")); Add(Base1("05EYEL")); Add(Base1("06EYER")); Add(Base1("37TONG"));
            foreach (var region in FaceRegions) Add(setOwnsFace && wholeSet.HasValue ? Set(wholeSet.Value, region) : Base(region));
            if (facialHair) Add(Base("09FCHR"));
            foreach (var region in BodyRegions)
            {
                var k = wholeSet ?? (mix != null ? (region.Contains("TORS") || region.Contains("AUP") || region.Contains("ALW") || region.Contains("HND") ? mix["upper"] : mix["lower"]) : (int?)null);
                Add(k.HasValue ? Set(k.Value, region) : Base1(region));
            }

            if (wholeSet.HasValue) foreach (var region in Attachments) Add(Set(wholeSet.Value, region));
            else if (mix != null)
            {
                Add(Set(mix["head"], "22AHED")); Add(Set(mix["head"], "23AFAC"));
                Add(Set(mix["shoulder"], "29ASHL")); Add(Set(mix["shoulder"], "30ASHR"));
                Add(Set(mix["upper"], "24ABAC")); Add(Set(mix["lower"], "25AHPF")); Add(Set(mix["lower"], "26AHPB")); Add(Set(mix["lower"], "27AHPL")); Add(Set(mix["lower"], "28AHPR"));
            }

            return skeleton;
        }

        private static Color32[] Shoot(PreviewRenderUtility utility, GameObject model, int size, bool head)
        {
            utility.AddSingleGO(model);
            var rect = new Rect(0, 0, size, size);
            utility.BeginPreview(rect, GUIStyle.none);
            var renderers = model.GetComponentsInChildren<Renderer>().Where(r => r.enabled).ToArray();
            var b = renderers[0].bounds; foreach (var r in renderers) b.Encapsulate(r.bounds);
            var halfFov = utility.camera.fieldOfView * 0.5f * Mathf.Deg2Rad;
            var headBone = model.GetComponentsInChildren<Transform>(true).FirstOrDefault(t => t.name == "head");
            var centre = head && headBone != null ? headBone.position + Vector3.up * 0.03f : b.center;
            var distance = head ? 0.42f / Mathf.Tan(halfFov) : Mathf.Max(b.size.y, b.size.x * 0.6f) * 0.62f / Mathf.Tan(halfFov);
            utility.camera.transform.position = centre + new Vector3(0f, 0.05f, distance);
            utility.camera.transform.rotation = Quaternion.LookRotation(centre - utility.camera.transform.position, Vector3.up);
            utility.Render(true);
            var texture = utility.EndPreview();
            var rt = RenderTexture.GetTemporary(size, size, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
            Graphics.Blit(texture, rt);
            var previous = RenderTexture.active; RenderTexture.active = rt;
            var cell = new Texture2D(size, size, TextureFormat.RGBA32, false);
            cell.ReadPixels(new Rect(0, 0, size, size), 0, 0);
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
