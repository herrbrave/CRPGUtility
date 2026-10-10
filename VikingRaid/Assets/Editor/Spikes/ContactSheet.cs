using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace VikingRaid.EditorTools.Spikes
{
    /// <summary>Task 0 spike (a): renders groups of character prefabs into contact-sheet PNGs (row-major, order listed in
    /// the report) and measures the fraction of magenta pixels per cell -- answers "do the Synty shader graphs render under
    /// URP 17.6" and "are the War Camp characters goblins" with evidence a person can look at. Needs a real graphics
    /// device: run WITHOUT -nographics.</summary>
    public static class ContactSheet
    {
        private const int CellW = 256, CellH = 384;

        [MenuItem("Viking Raid/Spikes/Render Character Contact Sheets")]
        public static void RenderAll()
        {
            var outDir = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "..", "VikingRaid_Logs", "spikes"));
            Directory.CreateDirectory(outDir);
            var report = new StringBuilder();

            Render("vikings", Find("Assets/Synty/PolygonVikingRealm/Prefabs/Characters", "SM_Chr_*.prefab", false), 5, outDir, report);
            Render("sidekick_goblins", Find("Assets/Synty/SidekickCharacters/Characters", "*.prefab", true), 5, outDir, report);
            Render("warcamp", Find("Assets/Synty/PolygonGoblinWarCamp/Prefabs/Characters", "SM_Chr_*.prefab", false), 5, outDir, report);

            File.WriteAllText(Path.Combine(outDir, "contact_report.txt"), report.ToString());
            Debug.Log("[ContactSheet] done -> " + outDir + "\n" + report);
        }

        private static List<string> Find(string folder, string pattern, bool recursive)
        {
            var abs = Path.GetFullPath(Path.Combine(Application.dataPath, "..", folder));
            return Directory.GetFiles(abs, pattern, recursive ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly)
                .Select(p => folder + "/" + Path.GetRelativePath(abs, p).Replace('\\', '/'))
                .OrderBy(p => p).ToList();
        }

        private static void Render(string name, List<string> prefabPaths, int cols, string outDir, StringBuilder report)
        {
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            RenderSettings.ambientMode = AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(0.55f, 0.55f, 0.6f);
            var light = new GameObject("Light").AddComponent<Light>();
            light.type = LightType.Directional;
            light.intensity = 1.1f;
            light.transform.rotation = Quaternion.Euler(35f, 150f, 0f); // lights the +Z (front) side

            var camGo = new GameObject("Cam");
            var cam = camGo.AddComponent<Camera>();
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.22f, 0.25f, 0.3f);
            cam.fieldOfView = 25f;
            cam.nearClipPlane = 0.1f;
            cam.farClipPlane = 100f;
            var rt = new RenderTexture(CellW, CellH, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
            cam.targetTexture = rt;

            var rows = Mathf.CeilToInt(prefabPaths.Count / (float)cols);
            var sheet = new Texture2D(cols * CellW, rows * CellH, TextureFormat.RGBA32, false);
            var cell = new Texture2D(CellW, CellH, TextureFormat.RGBA32, false);
            report.AppendLine($"== {name} ({prefabPaths.Count}) cols={cols}, row-major from top-left");

            for (var i = 0; i < prefabPaths.Count; i++)
            {
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPaths[i]);
                if (prefab == null) { report.AppendLine($"{i,2} MISSING {prefabPaths[i]}"); continue; }

                var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
                var renderers = go.GetComponentsInChildren<Renderer>().Where(r => r.enabled && r.gameObject.activeInHierarchy).ToArray();
                if (renderers.Length == 0) { report.AppendLine($"{i,2} NO-RENDERERS {prefabPaths[i]}"); Object.DestroyImmediate(go); continue; }

                var bounds = renderers[0].bounds;
                foreach (var r in renderers) bounds.Encapsulate(r.bounds);
                var dist = Mathf.Max(bounds.size.y, bounds.size.x * (CellH / (float)CellW) * 1.2f) / (2f * Mathf.Tan(cam.fieldOfView * 0.5f * Mathf.Deg2Rad)) * 1.15f;
                camGo.transform.position = bounds.center + new Vector3(0f, bounds.size.y * 0.05f, dist);
                camGo.transform.LookAt(bounds.center);

                var request = new UniversalRenderPipeline.SingleCameraRequest { destination = rt };
                if (RenderPipeline.SupportsRenderRequest(cam, request)) RenderPipeline.SubmitRenderRequest(cam, request);
                else cam.Render();

                RenderTexture.active = rt;
                cell.ReadPixels(new Rect(0, 0, CellW, CellH), 0, 0);
                cell.Apply();
                RenderTexture.active = null;

                var pixels = cell.GetPixels32();
                var pink = pixels.Count(p => p.r > 230 && p.g < 40 && p.b > 230);
                var bg = pixels.Count(p => Mathf.Abs(p.r - 56) < 6 && Mathf.Abs(p.g - 64) < 6 && Mathf.Abs(p.b - 77) < 6);
                var covered = 100f * (pixels.Length - bg) / pixels.Length;

                var col = i % cols;
                var row = rows - 1 - (i / cols); // texture origin is bottom-left; keep row-major from the TOP
                sheet.SetPixels(col * CellW, row * CellH, CellW, CellH, cell.GetPixels());
                report.AppendLine($"{i,2} r{i / cols}c{col} pink={100f * pink / pixels.Length:0.0}% subject={covered:0}% size={bounds.size:F2} {Path.GetFileNameWithoutExtension(prefabPaths[i])}");
                Object.DestroyImmediate(go);
            }

            sheet.Apply();
            File.WriteAllBytes(Path.Combine(outDir, $"contact_{name}.png"), sheet.EncodeToPNG());
            Object.DestroyImmediate(sheet);
            Object.DestroyImmediate(cell);
            cam.targetTexture = null;
            rt.Release();
            Object.DestroyImmediate(rt);
        }
    }
}
