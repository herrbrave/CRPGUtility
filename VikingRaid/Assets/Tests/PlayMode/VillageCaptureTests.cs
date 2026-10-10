using System.Collections;
using System.IO;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.TestTools;
using VikingRaid.Village;

namespace VikingRaid.Tests
{
    /// <summary>
    /// Visual gate for the village scene (a human judges the composition; the asserts only catch black frames). Screen capture never returns in a batch run, so cameras render into render
    /// textures. Written to <c>VikingRaid_Logs/spikes/village_*.png</c>: <c>start</c> (the toolkit camera on the party, as the player first sees it), <c>overview</c> (a high oblique shot of the
    /// whole village), <c>top</c> (straight down), and <c>navmesh</c> (a map: walkable points in green, the party start in yellow, NPCs in cyan, heroes in white).
    /// </summary>
    public class VillageCaptureTests : VillageTestBase
    {
        private const int W = 1280, H = 720;
        private static string OutDir => Path.GetFullPath(Path.Combine(Application.dataPath, "..", "..", "VikingRaid_Logs", "spikes"));

        private static Texture2D Read(RenderTexture rt)
        {
            var tex = new Texture2D(rt.width, rt.height, TextureFormat.RGBA32, false);
            RenderTexture.active = rt; tex.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0); tex.Apply(); RenderTexture.active = null;
            return tex;
        }

        private IEnumerator Shoot(Camera camera, string name)
        {
            var rt = new RenderTexture(W, H, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
            var previous = camera.targetTexture;
            camera.targetTexture = rt;
            for (var i = 0; i < 3; i++) yield return null;
            var tex = Read(rt);
            var pixels = tex.GetPixels32(); double luma = 0;
            foreach (var p in pixels) luma += (0.2126 * p.r + 0.7152 * p.g + 0.0722 * p.b) / 255.0;
            luma /= pixels.Length;
            Directory.CreateDirectory(OutDir);
            File.WriteAllBytes(Path.Combine(OutDir, $"village_{name}.png"), tex.EncodeToPNG());
            Debug.Log($"[VillageCapture] {name}: mean luminance {luma:F3}");
            camera.targetTexture = previous;
            Object.DestroyImmediate(tex); rt.Release(); Object.DestroyImmediate(rt);
            Assert.Greater(luma, 0.03, $"{name} is black");
        }

        private Camera TemporaryCamera(string name)
        {
            var go = new GameObject(name);
            var camera = go.AddComponent<Camera>();
            camera.CopyFrom(Cam);
            camera.depth = Cam.depth + 10;
            var main = Cam.GetComponent<UnityEngine.Rendering.Universal.UniversalAdditionalCameraData>();
            var extra = go.AddComponent<UnityEngine.Rendering.Universal.UniversalAdditionalCameraData>();
            if (main != null) { extra.renderPostProcessing = main.renderPostProcessing; extra.antialiasing = main.antialiasing; }

            return camera;
        }

        [UnityTest]
        public IEnumerator CaptureTheVillage()
        {
            yield return Shoot(Cam, "start");

            var bounds = Village.Location.CameraBoundsXZ.Value;
            var start = Village.PartyStart.position;
            var centre = new Vector3(bounds.center.x, start.y, bounds.center.y);
            var wasFog = RenderSettings.fog;
            RenderSettings.fog = false;   // the demo's fog is thick enough to hide a village from 150 m up

            var top = TemporaryCamera("TopCam");
            top.orthographic = true; top.orthographicSize = bounds.height * 0.5f + 10f; top.farClipPlane = 3000f;
            top.transform.position = centre + Vector3.up * 400f;
            top.transform.rotation = Quaternion.Euler(90f, 0f, 0f);
            yield return Shoot(top, "top");
            Object.Destroy(top.gameObject);

            var overview = TemporaryCamera("OverviewCam");
            overview.fieldOfView = 50f; overview.farClipPlane = 3000f;
            overview.transform.position = centre + new Vector3(-90f, 150f, -190f);
            overview.transform.LookAt(centre);
            yield return Shoot(overview, "overview");
            Object.Destroy(overview.gameObject);

            var around = TemporaryCamera("StartCam");
            around.fieldOfView = 55f; around.farClipPlane = 3000f;
            around.transform.position = start + new Vector3(-30f, 45f, -45f);
            around.transform.LookAt(start);
            yield return Shoot(around, "around_start");
            Object.Destroy(around.gameObject);
            RenderSettings.fog = wasFog;

            yield return NavMeshMap(bounds);
            AssertNoErrorsLogged();
        }

        private IEnumerator NavMeshMap(Rect bounds)
        {
            const float Step = 1.5f;
            var w = Mathf.CeilToInt(bounds.width / Step); var h = Mathf.CeilToInt(bounds.height / Step);
            var tex = new Texture2D(w, h, TextureFormat.RGBA32, false);
            var pixels = Enumerable.Repeat(new Color32(20, 20, 24, 255), w * h).ToArray();
            var y = Village.PartyStart.position.y;
            var path = new NavMeshPath();
            var start = Village.PartyStart.position;
            for (var j = 0; j < h; j++)
                for (var i = 0; i < w; i++)
                {
                    var p = new Vector3(bounds.xMin + (i + 0.5f) * Step, y, bounds.yMin + (j + 0.5f) * Step);
                    if (!NavMesh.SamplePosition(p, out var hit, 4f, NavMesh.AllAreas) || Vector2.Distance(new Vector2(hit.position.x, hit.position.z), new Vector2(p.x, p.z)) > Step) continue;
                    var connected = NavMesh.CalculatePath(start, hit.position, NavMesh.AllAreas, path) && path.status == NavMeshPathStatus.PathComplete;
                    pixels[j * w + i] = connected ? new Color32(40, 170, 80, 255) : new Color32(190, 70, 50, 255);   // green: reachable from the party start; red: an island
                }

            void Dot(Vector3 world, Color32 colour, int radius)
            {
                var ci = Mathf.RoundToInt((world.x - bounds.xMin) / Step); var cj = Mathf.RoundToInt((world.z - bounds.yMin) / Step);
                for (var dj = -radius; dj <= radius; dj++) for (var di = -radius; di <= radius; di++) { var a = ci + di; var b = cj + dj; if (a >= 0 && a < w && b >= 0 && b < h) pixels[b * w + a] = colour; }
            }

            Dot(Village.PartyStart.position, new Color32(255, 220, 0, 255), 2);
            foreach (var npc in Village.Npcs.Values) Dot(npc.transform.position, new Color32(0, 220, 255, 255), 1);
            foreach (var id in Village.Party) Dot(Position(id), new Color32(255, 255, 255, 255), 1);
            tex.SetPixels32(pixels); tex.Apply();
            Directory.CreateDirectory(OutDir);
            File.WriteAllBytes(Path.Combine(OutDir, "village_navmesh.png"), tex.EncodeToPNG());
            Object.DestroyImmediate(tex);
            Debug.Log($"[VillageCapture] navmesh map {w}x{h} px at {Step} m per pixel, bounds {bounds}");
            yield return null;
        }
    }
}
