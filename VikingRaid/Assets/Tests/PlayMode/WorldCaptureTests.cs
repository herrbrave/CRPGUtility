using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using VikingRaid.Game;

namespace VikingRaid.Tests
{
    /// <summary>
    /// Task 3 visual gate. Loads the real scene in play mode, stands the party and each encounter's goblins on their anchors (temporary: nothing is saved), and renders the views
    /// the user reviews to VikingRaid_Logs/spikes/world_*.png: an overview map with the arenas outlined, the start, each arena, the gate, the camp, an eye-level forest walk and a
    /// metal close-up in the camp's firelight. Numbers cannot judge a composition, so the asserts are only the cheap ones: no magenta (broken shader), not black, not blown out.
    /// </summary>
    public class WorldCaptureTests
    {
        private const int W = 1600, H = 900;
        private static string OutDir => Path.GetFullPath(Path.Combine(Application.dataPath, "..", "..", "VikingRaid_Logs", "spikes"));
        private static readonly string[] Party = { "bjorn", "ulf", "sigrid", "astrid" };

        private readonly List<GameObject> spawned = new();
        private WorldAnchors anchors;
        private Camera cam;
        private RenderTexture rt;
        private readonly List<string> failures = new();

        [UnitySetUp]
        public IEnumerator SetUp()
        {
#if UNITY_EDITOR
            UnityEditor.EditorSettings.asyncShaderCompilation = false;
            yield return UnityEditor.SceneManagement.EditorSceneManager.LoadSceneAsyncInPlayMode("Assets/Scenes/VikingRaid.unity", new LoadSceneParameters(LoadSceneMode.Single));
#endif
            yield return null;
            yield return null;
            anchors = Object.FindFirstObjectByType<WorldAnchors>();
            cam = Camera.main;
            Assert.IsNotNull(cam, "the scene has no Main Camera");
            rt = new RenderTexture(W, H, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
            cam.targetTexture = rt;
            Directory.CreateDirectory(OutDir);
        }

        [UnityTearDown]
        public IEnumerator UnloadWorldAfterTest() => SceneIsolation.UnloadAll();

        [TearDown]
        public void TearDown()
        {
            foreach (var go in spawned) if (go != null) Object.DestroyImmediate(go);
            spawned.Clear();
            if (cam != null) cam.targetTexture = null;
            if (rt != null) rt.Release();
        }

        private static IEnumerator Wait(float seconds)
        {
            var elapsed = 0f;
            do { elapsed += Time.deltaTime; yield return null; } while (elapsed < seconds);
        }

        private GameObject Spawn(string prefabPath, Vector3 position, Vector3 facing)
        {
            GameObject prefab = null;
#if UNITY_EDITOR
            prefab = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
#endif
            Assert.IsNotNull(prefab, prefabPath);
            var go = Object.Instantiate(prefab, position, Quaternion.LookRotation(facing));
            spawned.Add(go);
            var agent = go.GetComponent<NavMeshAgent>();
            if (agent != null) agent.Warp(position);
            return go;
        }

        private void StandParty(WorldAnchors.EncounterAnchors e, Vector3 facing)
        {
            for (var i = 0; i < Party.Length; i++) Spawn(GameDesign.Unit(Party[i]).Prefab, e.partyStage[i].position, facing);
        }

        private void StandGoblins(WorldAnchors.EncounterAnchors e, Vector3 facing)
        {
            for (var i = 0; i < e.goblinSpawns.Length; i++) Spawn(GameDesign.Unit(e.goblinUnitIds[i]).Prefab, e.goblinSpawns[i].position, -facing);
        }

        private Texture2D Render()
        {
            var request = new UniversalRenderPipeline.SingleCameraRequest { destination = rt };
            if (RenderPipeline.SupportsRenderRequest(cam, request)) RenderPipeline.SubmitRenderRequest(cam, request); else cam.Render();
            var tex = new Texture2D(W, H, TextureFormat.RGBA32, false);
            RenderTexture.active = rt;
            tex.ReadPixels(new Rect(0, 0, W, H), 0, 0);
            tex.Apply();
            RenderTexture.active = null;
            return tex;
        }

        private IEnumerator Shoot(string name, Vector3 position, Vector3 lookAt, float fov = 45f, bool orthographic = false, float orthoSize = 0f, float minLuma = 0.06f, float maxLuma = 0.80f)
        {
            cam.orthographic = orthographic;
            if (orthographic) cam.orthographicSize = orthoSize; else cam.fieldOfView = fov;
            cam.transform.position = position;
            cam.transform.rotation = Quaternion.LookRotation(lookAt - position, orthographic ? Vector3.forward : Vector3.up);   // the map has the road running left to right
            Object.DestroyImmediate(Render());     // discarded warm-up: the first frame after a camera move can still be a placeholder
            yield return null;
            var tex = Render();
            var pixels = tex.GetPixels32();
            double luma = 0; var magenta = 0;
            foreach (var p in pixels)
            {
                luma += (0.2126 * p.r + 0.7152 * p.g + 0.0722 * p.b) / 255.0;
                if (p.r > 200 && p.b > 200 && p.g < 60) magenta++;
            }

            luma /= pixels.Length;
            File.WriteAllBytes(Path.Combine(OutDir, $"world_{name}.png"), tex.EncodeToPNG());
            Object.DestroyImmediate(tex);
            Debug.Log($"[WorldCapture] {name}: mean luminance {luma:F3}, magenta {100.0 * magenta / pixels.Length:F3}%");
            if (magenta > pixels.Length * 0.002) failures.Add($"{name}: {100.0 * magenta / pixels.Length:F2}% magenta (a broken shader)");
            if (luma < minLuma) failures.Add($"{name}: too dark (mean luminance {luma:F3})");
            if (luma > maxLuma) failures.Add($"{name}: blown out (mean luminance {luma:F3})");
        }

        [UnityTest]
        public IEnumerator CaptureTheWorld()
        {
            var e = anchors.encounters;
            var yard = WorldLayout.Arenas[3]; var forecourt = WorldLayout.Arenas[2];

            // ---- the overview map: top-down over the whole road with every arena outlined
            var lines = new GameObject("MapOutlines"); spawned.Add(lines);
            var lineMaterial = new Material(Shader.Find("Sprites/Default"));
            foreach (var arena in WorldLayout.Arenas)
            {
                var lr = new GameObject(arena.Id).AddComponent<LineRenderer>();
                lr.transform.SetParent(lines.transform);
                lr.material = lineMaterial; lr.startColor = lr.endColor = new Color(1f, 0.9f, 0.1f); lr.widthMultiplier = 0.9f; lr.loop = true; lr.positionCount = 4; lr.useWorldSpace = true;
                lr.SetPositions(new[] { arena.ToWorld(0, -arena.Width / 2), arena.ToWorld(arena.Length, -arena.Width / 2), arena.ToWorld(arena.Length, arena.Width / 2), arena.ToWorld(0, arena.Width / 2) }.Select(p => p + Vector3.up * 30f).ToArray());
            }

            var centre = WorldLayout.PointAt(WorldLayout.RoadLength / 2f);
            RenderSettings.fog = false;     // exponential fog is fully opaque at 400 m: the map would be a grey sheet
            yield return Shoot("overview", centre + Vector3.up * 150f, centre, orthographic: true, orthoSize: 135f, minLuma: 0.03f);
            RenderSettings.fog = true;
            Object.DestroyImmediate(lines);

            // ---- populate the party at the first arena and look at the start
            for (var i = 0; i < Party.Length; i++) Spawn(GameDesign.Unit(Party[i]).Prefab, anchors.startSlots[i].position, Vector3.right);
            yield return Wait(1.5f);
            yield return Shoot("view_start", new Vector3(-13f, 4.5f, 8f), new Vector3(2f, 1.2f, -1f), fov: 55f);

            // ---- E1 and E2: party and goblins on their anchors, seen from behind the party
            for (var i = 0; i < 2; i++)
            {
                var a = WorldLayout.Arenas[i];
                StandParty(e[i], a.Forward); StandGoblins(e[i], a.Forward);
                yield return Wait(1.0f);
                yield return Shoot($"view_e{i + 1}", a.ToWorld(-9f, 5f) + Vector3.up * 11f, a.ToWorld(26f, 0f), fov: 48f);
            }

            // ---- E3 at the gate
            StandParty(e[2], forecourt.Forward); StandGoblins(e[2], forecourt.Forward);
            yield return Wait(1.0f);
            yield return Shoot("view_gate", forecourt.ToWorld(-6f, 6f) + Vector3.up * 8f, forecourt.ToWorld(46f, 0f) + Vector3.up * 3f, fov: 52f);
            yield return Shoot("view_gate_close", forecourt.ToWorld(30f, -8f) + Vector3.up * 3.5f, forecourt.ToWorld(48f, 0f) + Vector3.up * 3.5f, fov: 55f);

            // ---- E4 in the camp heart
            StandParty(e[3], yard.Forward); StandGoblins(e[3], yard.Forward);
            yield return Wait(1.5f);
            yield return Shoot("view_camp", yard.ToWorld(-6f, 7f) + Vector3.up * 9f, yard.ToWorld(36f, 0f) + Vector3.up * 3f, fov: 55f);
            yield return Shoot("view_camp_hq", yard.ToWorld(22f, -4f) + Vector3.up * 4f, yard.ToWorld(52f, 0f) + Vector3.up * 5f, fov: 60f);

            // ---- eye level on the road (woods B), and a hero in firelight for the metal
            var s = WorldLayout.Arenas[1].WestS - 30f;
            yield return Shoot("view_forest_walk", WorldLayout.PointAt(s) + Vector3.up * 1.7f, WorldLayout.PointAt(s + 20f) + Vector3.up * 2.5f, fov: 62f);
            var bjorn = spawned.First(g => g != null && g.name.StartsWith("Hero_Bjorn") && Vector3.Distance(g.transform.position, e[3].partyStage[0].position) < 0.5f);
            var at = bjorn.transform.position;
            yield return Shoot("closeup_metal", at + yard.Forward * 2.7f + yard.Right * 1.1f + Vector3.up * 1.5f, at + Vector3.up * 1.15f, fov: 38f);

            Assert.IsEmpty(failures, string.Join("\n", failures));
        }
    }

}
