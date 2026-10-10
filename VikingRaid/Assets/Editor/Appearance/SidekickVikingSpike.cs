using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace VikingRaid.EditorTools.Appearance
{
    /// <summary>
    /// Step 1 of "Viking Sidekick humans": do the Polygon Viking / War Camp / Generic attachments sit correctly on a Sidekick human? Each is cloned under a Sidekick bone (head, spine_03, pelvis) with local
    /// position zero and the bone's rest rotation undone (the rule that worked on the Polygon rig), scaled by the ratio of the two characters' heights. Writes
    /// <c>appearance_sidekick_viking_{head,body}.png</c> and <c>sidekick_viking_report.txt</c>. Needs graphics; runs across editor ticks (menu: Viking Raid/Appearance/Spike: Viking Attachments On Sidekick Human).
    /// </summary>
    public static class SidekickVikingSpike
    {
        private const string Polygon = "Assets/Synty/PolygonVikingRealm/Prefabs/Characters/SM_Chr_Warrior_Male_01.prefab";
        private static readonly (string Pack, string Folder, string Prefix)[] Sources =
        {
            ("viking", "Assets/Synty/PolygonVikingRealm/Prefabs/Characters/Chr_Attach", "SM_Chr_Attach_"),
            ("warcamp", "Assets/Synty/PolygonGoblinWarCamp/Prefabs/Characters/Chr_Attach", "SM_Chr_Attach_"),
            ("generic", "Assets/Synty/PolygonGeneric/Prefabs/Characters/Attachments", "SM_Gen_Chr_Attach_"),
        };

        // what is wanted from each pack; the rest (beards of the Polygon packs sit on the baked beard, rigged hoods are whole characters, rope/glider/skull/straw have no clear mount)
        private static readonly string[] HeadKeys = { "Hair_", "Helmet", "Crown", "Eyepatch", "Eye_Patch", "Hat_", "Goggles", "Mask", "Bun", "Ponytail" };
        private static readonly string[] BodyKeys = { "Cape", "Pouch", "Bags", "Backpack", "Bottle", "Collar" };

        [MenuItem("Viking Raid/Appearance/Spike: Viking Attachments On Sidekick Human")]
        public static void Run()
        {
            var routine = Routine();
            void Step()
            {
                try { if (routine.MoveNext()) return; }
                catch (System.Exception e) { Debug.LogException(e); }
                EditorApplication.update -= Step;
                EditorApplication.Exit(0);
            }

            EditorApplication.update += Step;
        }

        /// <summary>Pass 2: the Polygon SKINNED pieces (8 hairs, wolf and bear pelts, 4 furs) baked at rest into rigid meshes and carried over at their pose relative to the Polygon Head / Spine_03 bone, plus every beard. <c>appearance_sidekick_viking2_{head,body}.png</c>.</summary>
        [MenuItem("Viking Raid/Appearance/Spike: Viking Skinned Pieces And Beards On Sidekick Human")]
        public static void RunSecondPass()
        {
            var routine = SecondPass();
            void Step()
            {
                try { if (routine.MoveNext()) return; }
                catch (System.Exception e) { Debug.LogException(e); }
                EditorApplication.update -= Step;
                EditorApplication.Exit(0);
            }

            EditorApplication.update += Step;
        }

        private static Transform Find(GameObject root, string name) => root.GetComponentsInChildren<Transform>(true).First(t => t.name == name);

        private static IEnumerator<object> SecondPass()
        {
            var outDir = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "..", "VikingRaid_Logs", "spikes"));
            var report = new StringBuilder();
            var previousAsync = EditorSettings.asyncShaderCompilation;
            EditorSettings.asyncShaderCompilation = false;
            var utility = new PreviewRenderUtility();
            GameObject human = null, polygon = null;
            try
            {
                utility.camera.fieldOfView = 28f; utility.camera.nearClipPlane = 0.03f; utility.camera.farClipPlane = 60f;
                utility.camera.clearFlags = CameraClearFlags.SolidColor; utility.camera.backgroundColor = new Color(0.2f, 0.22f, 0.26f);
                utility.ambientColor = new Color(0.55f, 0.55f, 0.6f);
                utility.lights[0].intensity = 1.15f; utility.lights[0].transform.rotation = Quaternion.Euler(35f, 150f, 0f);
                utility.lights[1].intensity = 0.8f; utility.lights[1].transform.rotation = Quaternion.Euler(335f, 330f, 0f);

                polygon = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(Polygon));
                polygon.transform.SetPositionAndRotation(new Vector3(50f, 0f, 0f), Quaternion.identity);   // out of the way: it is only baked from
                foreach (var smr in polygon.GetComponentsInChildren<SkinnedMeshRenderer>(true)) smr.gameObject.SetActive(false);
                human = SidekickAssembleSpike.Assemble(3, null, "10", false, withHair: false);
                var scale = Height(human) / 1.88f;
                utility.AddSingleGO(human);
                for (var i = 0; i < 4; i++) yield return null;

                const int tile = 220, columns = 8;
                var skinned = polygon.GetComponentsInChildren<SkinnedMeshRenderer>(true).Where(s => s.name.StartsWith("SM_Chr_Attach_") && (s.name.Contains("Hair") || s.name.Contains("Wolf") || s.name.Contains("Bear") || s.name.Contains("Fur"))).OrderBy(s => s.name).ToList();
                var beardPaths = new List<(string pack, string path)>();
                foreach (var source in Sources)
                    foreach (var guid in AssetDatabase.FindAssets("t:Prefab", new[] { source.Folder }))
                    {
                        var path = AssetDatabase.GUIDToAssetPath(guid);
                        var file = Path.GetFileNameWithoutExtension(path);
                        if (file.Contains("Beard") || file.Contains("Moustache") || file.Contains("Chops")) beardPaths.Add((source.Pack, path));
                    }

                var headItems = skinned.Where(s => !s.name.Contains("Fur")).Select(s => (label: s.name.Replace("SM_Chr_Attach_", ""), skin: s, path: (string)null)).ToList();
                headItems.AddRange(beardPaths.Select(b => (label: b.pack + " " + Path.GetFileNameWithoutExtension(b.path).Replace("SM_Chr_Attach_", "").Replace("SM_Gen_Chr_Attach_", ""), skin: (SkinnedMeshRenderer)null, path: b.path)));
                var bodyItems = skinned.Where(s => s.name.Contains("Fur")).Select(s => (label: s.name.Replace("SM_Chr_Attach_", ""), skin: s, path: (string)null)).ToList();

                foreach (var (sheetName, items, headView) in new[] { ("head", headItems, true), ("body", bodyItems, false) })
                {
                    var rows = Mathf.CeilToInt(items.Count / (float)columns);
                    var sheet = new Texture2D(columns * tile, rows * tile, TextureFormat.RGBA32, false);
                    sheet.SetPixels32(Enumerable.Repeat(new Color32(30, 32, 38, 255), sheet.width * sheet.height).ToArray());
                    report.AppendLine($"== {sheetName}: {items.Count} items");
                    for (var i = 0; i < items.Count; i++)
                    {
                        var (label, skin, path) = items[i];
                        GameObject piece;
                        if (skin != null)
                        {
                            // bake at the Polygon rest pose; carry the pose relative to the Polygon bone (Head, or Spine_03 for furs) over to the Sidekick bone
                            var furs = skin.name.Contains("Fur");
                            skin.gameObject.SetActive(true);
                            var baked = new Mesh(); skin.BakeMesh(baked);
                            var polyBone = Find(polygon, furs ? "Spine_03" : "Head");
                            var anchor = Find(human, furs ? "spine_03" : "head");
                            piece = new GameObject("baked_" + label);
                            piece.AddComponent<MeshFilter>().sharedMesh = baked;
                            piece.AddComponent<MeshRenderer>().sharedMaterials = skin.sharedMaterials;
                            var relative = polyBone.worldToLocalMatrix * skin.transform.localToWorldMatrix;
                            skin.gameObject.SetActive(false);
                            piece.transform.SetParent(anchor, false);
                            var polyRot = polyBone.rotation;   // the Polygon bone's rest rotation: undo it for both rigs so the offset is in upright world axes
                            piece.transform.localRotation = Quaternion.Inverse(anchor.rotation) * polyRot * relative.rotation;
                            piece.transform.localPosition = Quaternion.Inverse(anchor.rotation) * (polyRot * relative.GetPosition() * scale);
                            piece.transform.localScale = Vector3.one * scale;
                        }
                        else
                        {
                            piece = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(path));
                            foreach (var c in piece.GetComponentsInChildren<Collider>(true)) Object.DestroyImmediate(c);
                            foreach (var a in piece.GetComponentsInChildren<Animator>(true)) Object.DestroyImmediate(a);
                            var anchor = Find(human, "head");
                            piece.transform.SetParent(anchor, false);
                            piece.transform.localPosition = Vector3.zero; piece.transform.localRotation = Quaternion.Inverse(anchor.rotation); piece.transform.localScale = Vector3.one * scale;
                        }

                        yield return null;
                        var rect = new Rect(0, 0, tile, tile);
                        utility.BeginPreview(rect, GUIStyle.none);
                        var halfFov = utility.camera.fieldOfView * 0.5f * Mathf.Deg2Rad;
                        Vector3 centre, offset;
                        if (headView) { centre = Find(human, "head").position + Vector3.up * 0.04f; offset = new Vector3(0.45f, 0.12f, 1f).normalized * (0.5f / Mathf.Tan(halfFov)); }
                        else
                        {
                            var renderers = human.GetComponentsInChildren<Renderer>().Where(r => r.enabled).ToArray();
                            var b = renderers[0].bounds; foreach (var r in renderers) b.Encapsulate(r.bounds);
                            centre = b.center; offset = new Vector3(0.45f, 0.1f, 1f).normalized * (b.size.y * 0.62f / Mathf.Tan(halfFov));
                        }

                        utility.camera.transform.position = centre + offset;
                        utility.camera.transform.rotation = Quaternion.LookRotation(centre - utility.camera.transform.position, Vector3.up);
                        utility.Render(true);
                        var texture = utility.EndPreview();
                        var rt = RenderTexture.GetTemporary(tile, tile, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
                        Graphics.Blit(texture, rt);
                        var previous = RenderTexture.active; RenderTexture.active = rt;
                        var cell = new Texture2D(tile, tile, TextureFormat.RGBA32, false);
                        cell.ReadPixels(new Rect(0, 0, tile, tile), 0, 0);
                        sheet.SetPixels32((i % columns) * tile, (rows - 1 - i / columns) * tile, tile, tile, cell.GetPixels32());
                        RenderTexture.active = previous; RenderTexture.ReleaseTemporary(rt); Object.DestroyImmediate(cell);
                        report.AppendLine($"{i,3} r{i / columns}c{i % columns} {label}");
                        Object.DestroyImmediate(piece);
                    }

                    sheet.Apply();
                    File.WriteAllBytes(Path.Combine(outDir, $"appearance_sidekick_viking2_{sheetName}.png"), sheet.EncodeToPNG());
                    Object.DestroyImmediate(sheet);
                }
            }
            finally
            {
                utility.Cleanup();
                if (polygon != null) Object.DestroyImmediate(polygon);
                EditorSettings.asyncShaderCompilation = previousAsync;
                File.WriteAllText(Path.Combine(outDir, "sidekick_viking2_report.txt"), report.ToString());
            }

            Debug.Log("[SidekickVikingSpike] second pass done");
        }

        private static string BoneFor(string name)
        {
            var n = name.ToLowerInvariant();
            if (n.Contains("cape") || n.Contains("backpack") || n.Contains("collar")) return "spine_03";
            if (n.Contains("pouch") || n.Contains("bags") || n.Contains("bottle")) return "pelvis";
            return "head";
        }

        private static float Height(GameObject go)
        {
            var renderers = go.GetComponentsInChildren<Renderer>().Where(r => r.enabled && r.gameObject.activeInHierarchy).ToArray();
            var b = renderers[0].bounds; foreach (var r in renderers) b.Encapsulate(r.bounds);
            return b.size.y;
        }

        private static IEnumerator<object> Routine()
        {
            var outDir = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "..", "VikingRaid_Logs", "spikes"));
            Directory.CreateDirectory(outDir);
            var report = new StringBuilder();
            var previousAsync = EditorSettings.asyncShaderCompilation;
            EditorSettings.asyncShaderCompilation = false;
            var utility = new PreviewRenderUtility();
            GameObject human = null;
            try
            {
                utility.camera.fieldOfView = 28f; utility.camera.nearClipPlane = 0.03f; utility.camera.farClipPlane = 60f;
                utility.camera.clearFlags = CameraClearFlags.SolidColor; utility.camera.backgroundColor = new Color(0.2f, 0.22f, 0.26f);
                utility.ambientColor = new Color(0.55f, 0.55f, 0.6f);
                utility.lights[0].intensity = 1.15f; utility.lights[0].transform.rotation = Quaternion.Euler(35f, 150f, 0f);
                utility.lights[1].intensity = 0.8f; utility.lights[1].transform.rotation = Quaternion.Euler(335f, 330f, 0f);

                // the two characters, measured
                var polygon = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(Polygon));
                foreach (var smr in polygon.GetComponentsInChildren<SkinnedMeshRenderer>(true)) smr.gameObject.SetActive(smr.name == "SM_Chr_Warrior_Male_01");
                polygon.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
                var polygonHeight = Height(polygon);
                var polygonHead = polygon.GetComponentsInChildren<Transform>(true).First(t => t.name == "Head").position;
                Object.DestroyImmediate(polygon);

                human = SidekickAssembleSpike.Assemble(3, null, "10", false, withHair: false);
                var humanHeight = Height(human);
                var bone = new System.Func<string, Transform>(n => human.GetComponentsInChildren<Transform>(true).First(t => t.name == n));
                var scale = humanHeight / polygonHeight;
                report.AppendLine($"Polygon height {polygonHeight:F3}, Head bone y {polygonHead.y:F3} ({polygonHead.y / polygonHeight:P0} of height)");
                report.AppendLine($"Sidekick human height {humanHeight:F3}, head bone y {bone("head").position.y:F3} ({bone("head").position.y / humanHeight:P0}), spine_03 y {bone("spine_03").position.y:F3}, pelvis y {bone("pelvis").position.y:F3}");
                report.AppendLine($"scale applied to attachments: {scale:F3}");
                foreach (var n in new[] { "head", "spine_03", "pelvis" }) report.AppendLine($"  {n} rest world euler {bone(n).rotation.eulerAngles}");
                utility.AddSingleGO(human);
                for (var i = 0; i < 4; i++) yield return null;

                const int tile = 220, columns = 8;
                foreach (var (sheetName, keys, headView) in new[] { ("head", HeadKeys, true), ("body", BodyKeys, false) })
                {
                    var items = new List<(string pack, string path, string name)>();
                    foreach (var (pack, folder, prefix) in Sources)
                        foreach (var guid in AssetDatabase.FindAssets("t:Prefab", new[] { folder }))
                        {
                            var path = AssetDatabase.GUIDToAssetPath(guid);
                            var name = Path.GetFileNameWithoutExtension(path);
                            if (name.Contains("Rigged") || name.Contains("Beard") || name.Contains("Chops") || name.Contains("Moustache")) continue;
                            if (pack == "generic" && !(name.Contains("Hair") || name.Contains("Bun") || name.Contains("Ponytail"))) continue;
                            if (keys.Any(k => name.Contains(k))) items.Add((pack, path, name.Replace(prefix, "")));
                        }

                    items = items.OrderBy(i => i.pack).ThenBy(i => i.name).ToList();
                    var rows = Mathf.CeilToInt(items.Count / (float)columns);
                    var sheet = new Texture2D(columns * tile, rows * tile, TextureFormat.RGBA32, false);
                    sheet.SetPixels32(Enumerable.Repeat(new Color32(30, 32, 38, 255), sheet.width * sheet.height).ToArray());
                    report.AppendLine($"== {sheetName}: {items.Count} items, {columns} per row");
                    for (var i = 0; i < items.Count; i++)
                    {
                        var (pack, path, name) = items[i];
                        var anchor = bone(BoneFor(name));
                        var clone = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(path));
                        foreach (var c in clone.GetComponentsInChildren<Collider>(true)) Object.DestroyImmediate(c);
                        foreach (var a in clone.GetComponentsInChildren<Animator>(true)) Object.DestroyImmediate(a);
                        clone.transform.SetParent(anchor, false);
                        clone.transform.localPosition = Vector3.zero;
                        clone.transform.localRotation = Quaternion.Inverse(anchor.rotation);
                        clone.transform.localScale = Vector3.one * scale;
                        yield return null;

                        var rect = new Rect(0, 0, tile, tile);
                        utility.BeginPreview(rect, GUIStyle.none);
                        var halfFov = utility.camera.fieldOfView * 0.5f * Mathf.Deg2Rad;
                        var head = bone("head").position;
                        Vector3 centre, offset;
                        if (headView) { centre = head + Vector3.up * 0.06f; offset = new Vector3(0.45f, 0.12f, 1f).normalized * (0.5f / Mathf.Tan(halfFov)); }
                        else
                        {
                            var renderers = human.GetComponentsInChildren<Renderer>().Where(r => r.enabled).ToArray();
                            var b = renderers[0].bounds; foreach (var r in renderers) b.Encapsulate(r.bounds);
                            centre = b.center; offset = new Vector3(0.25f, 0.1f, -1f).normalized * (b.size.y * 0.62f / Mathf.Tan(halfFov));
                        }

                        utility.camera.transform.position = centre + offset;
                        utility.camera.transform.rotation = Quaternion.LookRotation(centre - utility.camera.transform.position, Vector3.up);
                        utility.Render(true);
                        var texture = utility.EndPreview();
                        var rt = RenderTexture.GetTemporary(tile, tile, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
                        Graphics.Blit(texture, rt);
                        var previous = RenderTexture.active; RenderTexture.active = rt;
                        var cell = new Texture2D(tile, tile, TextureFormat.RGBA32, false);
                        cell.ReadPixels(new Rect(0, 0, tile, tile), 0, 0);
                        sheet.SetPixels32((i % columns) * tile, (rows - 1 - i / columns) * tile, tile, tile, cell.GetPixels32());
                        RenderTexture.active = previous; RenderTexture.ReleaseTemporary(rt); Object.DestroyImmediate(cell);
                        report.AppendLine($"{i,3} r{i / columns}c{i % columns} {pack} {name} [{anchor.name}]");
                        Object.DestroyImmediate(clone);
                    }

                    sheet.Apply();
                    File.WriteAllBytes(Path.Combine(outDir, $"appearance_sidekick_viking_{sheetName}.png"), sheet.EncodeToPNG());
                    Object.DestroyImmediate(sheet);
                }
            }
            finally
            {
                utility.Cleanup();
                EditorSettings.asyncShaderCompilation = previousAsync;
                File.WriteAllText(Path.Combine(outDir, "sidekick_viking_report.txt"), report.ToString());
            }

            Debug.Log("[SidekickVikingSpike] done");
        }
    }
}
