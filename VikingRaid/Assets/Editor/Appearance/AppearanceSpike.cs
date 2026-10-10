using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace VikingRaid.EditorTools.Appearance
{
    /// <summary>
    /// Gate spike for the NPC appearance creator: which attachments of the three Polygon packs sit correctly on the Viking master rig, how, and what the colour options really do.
    /// <list type="bullet">
    /// <item>Every attachment prefab is classified: <b>Static</b> (a MeshRenderer), <b>OwnRig</b> (skinned to a skeleton the master does not have: capes), <b>MasterClone</b> (a full War Camp / Viking
    /// character with one renderer active: hoods, shawl; only the active renderer is wanted, re-skinned onto the master skeleton by bone ORDER). It is mounted on a bone chosen by category
    /// (Head, Spine_03, Hips) keeping the pose it was authored at (valid only in the rest pose, which is where the master stands): the resulting LOCAL offset is what the catalog will store.</item>
    /// <item>Sheets <c>appearance_{pack}_{n}.png</c>: per attachment [head front | head three-quarter | body (front for head items, back otherwise)]; <c>appearance_palettes.png</c>: the 15 Viking
    /// palette materials; <c>appearance_tint.png</c>: hair and beard under tints, including a multiplier above 1 (can a tint LIGHTEN?). Order and measurements: <c>appearance_report.txt</c>.</item>
    /// </list>
    /// Needs a graphics device: run WITHOUT -nographics. Replaces the open scene (it asks to save first).
    /// </summary>
    public static class AppearanceSpike
    {
        private const string MasterPath = "Assets/Synty/PolygonVikingRealm/Prefabs/Characters/SM_Chr_Warrior_Male_01.prefab";
        private const string BodyName = "SM_Chr_Warrior_Male_01";
        private const int Tile = 192, PerRow = 4, PerSheet = 24;

        private static readonly (string Pack, string Folder)[] Sources =
        {
            ("viking", "Assets/Synty/PolygonVikingRealm/Prefabs/Characters/Chr_Attach"),
            ("warcamp", "Assets/Synty/PolygonGoblinWarCamp/Prefabs/Characters/Chr_Attach"),
            ("generic", "Assets/Synty/PolygonGeneric/Prefabs/Characters/Attachments"),
        };

        private enum Kind { Static, OwnRig, MasterClone }

        [MenuItem("Viking Raid/Appearance/Spike: Attachment Contact Sheets")]
        public static void Run()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            RunUnattended();
        }

        public static void RunUnattended()
        {
            var previousAsync = EditorSettings.asyncShaderCompilation;
            EditorSettings.asyncShaderCompilation = false;   // the first render of a Synty shader graph is a flat white placeholder otherwise; restored below
            try { Render(); }
            finally { EditorSettings.asyncShaderCompilation = previousAsync; }
        }

        private static void Render()
        {
            var outDir = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "..", "VikingRaid_Logs", "spikes"));
            Directory.CreateDirectory(outDir);
            var report = new StringBuilder();

            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            RenderSettings.ambientMode = AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(0.6f, 0.6f, 0.65f);
            var light = new GameObject("Light").AddComponent<Light>();
            light.type = LightType.Directional; light.intensity = 1.1f; light.transform.rotation = Quaternion.Euler(30f, 160f, 0f);
            var camGo = new GameObject("Cam");
            var cam = camGo.AddComponent<Camera>();
            cam.clearFlags = CameraClearFlags.SolidColor; cam.backgroundColor = new Color(0.22f, 0.25f, 0.3f);
            cam.fieldOfView = 25f; cam.nearClipPlane = 0.05f; cam.farClipPlane = 50f;
            var rt = new RenderTexture(Tile, Tile, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
            cam.targetTexture = rt;
            var cell = new Texture2D(Tile, Tile, TextureFormat.RGBA32, false);

            var masterPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(MasterPath) ?? throw new System.InvalidOperationException(MasterPath + " is missing");
            var masterBoneNames = new HashSet<string>(masterPrefab.GetComponentsInChildren<Transform>(true).Select(t => t.name));

            GameObject NewMaster()
            {
                var m = (GameObject)PrefabUtility.InstantiatePrefab(masterPrefab);
                m.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
                foreach (var smr in m.GetComponentsInChildren<SkinnedMeshRenderer>(true)) smr.gameObject.SetActive(smr.name == BodyName);
                return m;
            }

            void Warm() { var m = NewMaster(); Shoot(cam, rt, cell, Bone(m, "Head").position + new Vector3(0f, 0.05f, 1.1f), Bone(m, "Head").position + Vector3.up * 0.05f); Object.DestroyImmediate(m); }
            Warm(); Warm();   // discarded: shader compilation / first-frame placeholders

            var sheets = 0;
            foreach (var (pack, folder) in Sources)
            {
                var prefabs = AssetDatabase.FindAssets("t:Prefab", new[] { folder }).Select(AssetDatabase.GUIDToAssetPath).OrderBy(p => p).ToList();
                report.AppendLine($"== {pack}: {prefabs.Count} attachments in {folder}");
                for (var chunk = 0; chunk * PerSheet < prefabs.Count; chunk++)
                {
                    var items = prefabs.Skip(chunk * PerSheet).Take(PerSheet).ToList();
                    var rows = Mathf.CeilToInt(items.Count / (float)PerRow);
                    var sheet = new Texture2D(PerRow * 3 * Tile, rows * Tile, TextureFormat.RGBA32, false);
                    sheet.SetPixels32(Enumerable.Repeat(new Color32(40, 40, 46, 255), sheet.width * sheet.height).ToArray());
                    report.AppendLine($"-- sheet {pack}_{chunk}: {PerRow} attachments per row, each = [B front | C front | C 3/4] for head items, [B back | C back | C front] for the rest");

                    for (var i = 0; i < items.Count; i++)
                    {
                        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(items[i]);
                        var name = Path.GetFileNameWithoutExtension(items[i]);
                        var kind = Classify(prefab, masterBoneNames);
                        var boneName = MountBone(name);
                        var line = new StringBuilder($"{i,2} r{i / PerRow}c{i % PerRow} {name} [{kind}] bone={(kind == Kind.MasterClone ? "(skinned)" : boneName)}");

                        for (var tile = 0; tile < 3; tile++)
                        {
                            var master = NewMaster();
                            var head = Bone(master, "Head");
                            var mode = tile == 0 ? 'B' : 'C';   // B: identity local pose under the bone. C: at the bone, upright in the world (the bone rest rotation undone)
                            var mounted = Mount(master, prefab, kind, boneName, mode, out var note);
                            if (tile < 2)
                            {
                                var t = mounted != null ? mounted.transform : null;
                                var r = mounted != null ? mounted.GetComponentInChildren<Renderer>() : null;
                                var bone = Bone(master, boneName);
                                line.Append($" | {mode}:");
                                if (tile == 0 && note != "") line.Append(" " + note + ";");
                                if (t != null && kind != Kind.MasterClone) line.Append($" local pos ({t.localPosition.x:F3},{t.localPosition.y:F3},{t.localPosition.z:F3}) euler ({t.localEulerAngles.x:F0},{t.localEulerAngles.y:F0},{t.localEulerAngles.z:F0})");
                                if (r != null) { var c = r.bounds.center - bone.position; line.Append($" centre-from-bone ({c.x:F2},{c.y:F2},{c.z:F2}) size ({r.bounds.size.x:F2},{r.bounds.size.y:F2},{r.bounds.size.z:F2}) mat {(r.sharedMaterial != null ? r.sharedMaterial.name : "none")}"); }
                            }

                            var headItem = boneName == "Head" || kind == Kind.MasterClone;
                            if (headItem && tile < 2) Shoot(cam, rt, cell, head.position + new Vector3(0f, 0.04f, 1.15f), head.position + Vector3.up * 0.06f);
                            else if (headItem) Shoot(cam, rt, cell, head.position + new Vector3(0.75f, 0.2f, 0.85f), head.position + Vector3.up * 0.06f);
                            else
                            {
                                var rs = master.GetComponentsInChildren<Renderer>().Where(r => r.enabled && r.gameObject.activeInHierarchy).ToArray();
                                var b = rs[0].bounds; foreach (var r in rs) b.Encapsulate(r.bounds);
                                Shoot(cam, rt, cell, b.center + new Vector3(0f, 0.1f, tile < 2 ? -5.2f : 5.2f), b.center);   // capes, packs and bags: from behind in B and C, from the front in the last tile
                            }

                            var col = (i % PerRow) * 3 + tile; var row = rows - 1 - i / PerRow;
                            sheet.SetPixels(col * Tile, row * Tile, Tile, Tile, cell.GetPixels());
                            Object.DestroyImmediate(master);
                        }

                        report.AppendLine(line.ToString());
                    }

                    sheet.Apply();
                    File.WriteAllBytes(Path.Combine(outDir, $"appearance_{pack}_{chunk}.png"), sheet.EncodeToPNG());
                    Object.DestroyImmediate(sheet);
                    sheets++;
                }
            }

            PaletteSheet(cam, rt, cell, NewMaster, outDir, report);
            BodySheet(cam, rt, cell, masterPrefab, outDir, report);

            cam.targetTexture = null; rt.Release(); Object.DestroyImmediate(rt); Object.DestroyImmediate(cell);
            File.WriteAllText(Path.Combine(outDir, "appearance_report.txt"), report.ToString());
            Debug.Log($"[AppearanceSpike] {sheets} attachment sheets + palette + tint written to {outDir}\n{report}");
        }

        // ------------------------------------------------------------------ colour sheets
        private static void PaletteSheet(Camera cam, RenderTexture rt, Texture2D cell, System.Func<GameObject> newMaster, string outDir, StringBuilder report)
        {
            var materials = AssetDatabase.FindAssets("t:Material", new[] { "Assets/Synty/PolygonVikingRealm/Materials/Alts" })
                .Select(AssetDatabase.GUIDToAssetPath).Where(p => !p.Contains("/Emissive/") && !p.Contains("/Snow/")).OrderBy(p => p).ToList();
            const int cols = 5;
            var rows = Mathf.CeilToInt(materials.Count / (float)cols);
            var sheet = new Texture2D(cols * 2 * Tile, rows * Tile, TextureFormat.RGBA32, false);
            sheet.SetPixels32(Enumerable.Repeat(new Color32(40, 40, 46, 255), sheet.width * sheet.height).ToArray());
            report.AppendLine($"== palettes: {materials.Count} Viking alt materials, {cols} per row, each = [head | body]");
            for (var i = 0; i < materials.Count; i++)
            {
                var material = AssetDatabase.LoadAssetAtPath<Material>(materials[i]);
                var texture = material.HasProperty("_Base_Texture") ? material.GetTexture("_Base_Texture") : material.mainTexture;
                report.AppendLine($"{i,2} r{i / cols}c{i % cols} {Path.GetFileNameWithoutExtension(materials[i])} texture={(texture != null ? texture.name : "none")}");
                for (var tile = 0; tile < 2; tile++)
                {
                    var master = newMaster();
                    foreach (var r in master.GetComponentsInChildren<Renderer>(true)) r.sharedMaterial = material;
                    var head = Bone(master, "Head");
                    if (tile == 0) Shoot(cam, rt, cell, head.position + new Vector3(0f, 0.04f, 1.15f), head.position + Vector3.up * 0.06f);
                    else
                    {
                        var b = master.GetComponentsInChildren<Renderer>().Where(r => r.enabled && r.gameObject.activeInHierarchy).Select(r => r.bounds).Aggregate((a, c) => { a.Encapsulate(c); return a; });
                        Shoot(cam, rt, cell, b.center + new Vector3(0f, 0.1f, 5.2f), b.center);
                    }

                    sheet.SetPixels(((i % cols) * 2 + tile) * Tile, (rows - 1 - i / cols) * Tile, Tile, Tile, cell.GetPixels());
                    Object.DestroyImmediate(master);
                }
            }

            sheet.Apply();
            File.WriteAllBytes(Path.Combine(outDir, "appearance_palettes.png"), sheet.EncodeToPNG());
            Object.DestroyImmediate(sheet);
        }

        /// <summary>All ten bodies, head and whole figure: does each body bake in a beard or hair of its own (which would clash with the beard and hair rows)?</summary>
        private static void BodySheet(Camera cam, RenderTexture rt, Texture2D cell, GameObject masterPrefab, string outDir, StringBuilder report)
        {
            var names = masterPrefab.GetComponentsInChildren<SkinnedMeshRenderer>(true).Select(r => r.name).Where(n => n.StartsWith("SM_Chr_") && !n.Contains("Attach")).OrderBy(n => n).ToList();
            const int cols = 5;
            var rows = Mathf.CeilToInt(names.Count / (float)cols);
            var sheet = new Texture2D(cols * 3 * Tile, rows * Tile, TextureFormat.RGBA32, false);
            sheet.SetPixels32(Enumerable.Repeat(new Color32(40, 40, 46, 255), sheet.width * sheet.height).ToArray());
            report.AppendLine($"== bodies: {names.Count}, {cols} per row, each = [head front | head 3/4 | body]");
            for (var i = 0; i < names.Count; i++)
            {
                report.AppendLine($"{i,2} r{i / cols}c{i % cols} {names[i]}");
                for (var tile = 0; tile < 3; tile++)
                {
                    var master = (GameObject)PrefabUtility.InstantiatePrefab(masterPrefab);
                    master.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
                    foreach (var smr in master.GetComponentsInChildren<SkinnedMeshRenderer>(true)) smr.gameObject.SetActive(smr.name == names[i]);
                    var head = Bone(master, "Head");
                    if (tile == 0) Shoot(cam, rt, cell, head.position + new Vector3(0f, 0.04f, 1.15f), head.position + Vector3.up * 0.06f);
                    else if (tile == 1) Shoot(cam, rt, cell, head.position + new Vector3(0.75f, 0.2f, 0.85f), head.position + Vector3.up * 0.06f);
                    else
                    {
                        var b = master.GetComponentsInChildren<Renderer>().Where(r => r.enabled && r.gameObject.activeInHierarchy).Select(r => r.bounds).Aggregate((a, c) => { a.Encapsulate(c); return a; });
                        Shoot(cam, rt, cell, b.center + new Vector3(0f, 0.1f, 5.2f), b.center);
                    }

                    sheet.SetPixels(((i % cols) * 3 + tile) * Tile, (rows - 1 - i / cols) * Tile, Tile, Tile, cell.GetPixels());
                    Object.DestroyImmediate(master);
                }
            }

            sheet.Apply();
            File.WriteAllBytes(Path.Combine(outDir, "appearance_bodies.png"), sheet.EncodeToPNG());
            Object.DestroyImmediate(sheet);
        }

        // ------------------------------------------------------------------ classification and mounting
        private static Kind Classify(GameObject prefab, HashSet<string> masterBoneNames)
        {
            var skinned = prefab.GetComponentsInChildren<SkinnedMeshRenderer>(true);
            if (skinned.Length == 0) return Kind.Static;
            var covered = skinned.All(s => s.bones.All(b => b == null || masterBoneNames.Contains(b.name)));
            return covered ? Kind.MasterClone : Kind.OwnRig;
        }

        /// <summary>The bone an attachment follows, by what it is (the pose it keeps is the one it was authored at, so this only decides what it moves with).</summary>
        private static string MountBone(string name)
        {
            var n = name.ToLowerInvariant();
            if (n.Contains("cape") || n.Contains("backpack") || n.Contains("shawl") || n.Contains("collar")) return "Spine_03";
            if (n.Contains("pouch") || n.Contains("bags") || n.Contains("bottle") || n.Contains("rope") || n.Contains("glider")) return "Hips";
            return "Head";
        }

        private static Transform Bone(GameObject master, string name) => master.GetComponentsInChildren<Transform>(true).First(t => t.name == name);

        /// <summary>Mounts the attachment on the master; returns the object that was added (null when nothing could be).</summary>
        private static GameObject Mount(GameObject master, GameObject prefab, Kind kind, string boneName, char mode, out string note)
        {
            note = "";
            var clone = Object.Instantiate(prefab);
            clone.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            clone.name = prefab.name;
            foreach (var c in clone.GetComponentsInChildren<Collider>(true)) Object.DestroyImmediate(c);
            foreach (var a in clone.GetComponentsInChildren<Animator>(true)) Object.DestroyImmediate(a);

            if (kind == Kind.MasterClone)
            {
                var byName = master.GetComponentsInChildren<Transform>(true).GroupBy(t => t.name).ToDictionary(g => g.Key, g => g.First());
                var wanted = clone.GetComponentsInChildren<SkinnedMeshRenderer>(true).Where(s => s.gameObject.activeInHierarchy).ToList();
                var missing = 0;
                foreach (var smr in wanted)
                {
                    var bones = smr.bones.Select(b => b != null && byName.TryGetValue(b.name, out var m) ? m : null).ToArray();   // by ORDER: index i must stay bone i (the bind poses)
                    missing += bones.Count(b => b == null);
                    smr.bones = bones;
                    smr.rootBone = smr.rootBone != null && byName.TryGetValue(smr.rootBone.name, out var root) ? root : master.transform;
                    smr.transform.SetParent(master.transform, false);
                    smr.transform.localPosition = Vector3.zero; smr.transform.localRotation = Quaternion.identity;
                    smr.gameObject.SetActive(true);
                }

                Object.DestroyImmediate(clone);   // the rest of the clone: 16 inactive bodies and a second skeleton
                note = $"extracted {wanted.Count} active renderer(s), {(missing == 0 ? "all bones mapped" : missing + " bones MISSING")}";
                return wanted.Count > 0 ? wanted[0].gameObject : null;
            }

            var bone = Bone(master, boneName);
            clone.transform.SetParent(bone, false);   // the attachments are authored with their pivot AT the bone (mounting them at their authored world pose puts them 1.5 m below the head)
            if (mode == 'C') { clone.transform.localPosition = Vector3.zero; clone.transform.localRotation = Quaternion.Inverse(bone.rotation); }   // upright in the world at the rest pose
            note = kind == Kind.OwnRig ? "own skeleton mounted whole (rigid)" : "";
            return clone;
        }

        private static void Shoot(Camera cam, RenderTexture rt, Texture2D cell, Vector3 position, Vector3 lookAt)
        {
            cam.transform.position = position;
            cam.transform.LookAt(lookAt);
            var request = new UniversalRenderPipeline.SingleCameraRequest { destination = rt };
            if (RenderPipeline.SupportsRenderRequest(cam, request)) RenderPipeline.SubmitRenderRequest(cam, request); else cam.Render();
            RenderTexture.active = rt;
            cell.ReadPixels(new Rect(0, 0, Tile, Tile), 0, 0);
            cell.Apply();
            RenderTexture.active = null;
        }
    }
}
