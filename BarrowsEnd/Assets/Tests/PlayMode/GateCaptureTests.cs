using System.Collections;
using System.IO;
using BarrowsEnd.Gate;
using CRPG.Gameplay;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace BarrowsEnd.Tests
{
    /// <summary>
    /// Task 2.7b: renders the gate scene through its real camera into a RenderTexture (never ScreenCapture, which hangs in batch mode) and
    /// saves PNGs under %TEMP%/sp for a human to look at. The assertions only prove the frame is not blank; the eyes are the real test.
    /// </summary>
    public class GateCaptureTests
    {
        private static void Save(RenderTexture rt, string name)
        {
            var previous = RenderTexture.active;
            RenderTexture.active = rt;
            var tex = new Texture2D(rt.width, rt.height, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0);
            tex.Apply();
            var dir = Path.Combine(System.Environment.GetEnvironmentVariable("TEMP") ?? Path.GetTempPath(), "sp");
            Directory.CreateDirectory(dir);
            File.WriteAllBytes(Path.Combine(dir, name), tex.EncodeToPNG());

            // Not blank: at least some pixels differ from the first pixel (a clear colour).
            var first = tex.GetPixel(0, 0);
            var different = 0;
            foreach (var pixel in tex.GetPixels(0, 0, rt.width, rt.height)) if (pixel != first) different++;
            Object.Destroy(tex);
            RenderTexture.active = previous;
            Assert.Greater(different, 1000, $"{name}: the frame has content");
        }

        [UnityTest]
        public IEnumerator TheGateRendersAtIntegerScalesAndCapturesFrames()
        {
            yield return SceneManager.LoadSceneAsync("Assets/Scenes/Gate/PhaseTwoGate.unity", LoadSceneMode.Single);
            var gate = Object.FindFirstObjectByType<GateBootstrap>();
            gate.SavePath = Path.Combine(Path.GetTempPath(), "barrowsend-gate-capture.json");
            for (var i = 0; i < 10; i++) yield return null;
            Assert.IsTrue(gate.IsReady);

            var camera = Camera.main;
            var gateCamera = camera.GetComponent<GateCamera>();
            var rt = new RenderTexture(1920, 1080, 24);
            camera.targetTexture = rt;

            gateCamera.SetScale(3);
            for (var i = 0; i < 6; i++) yield return null;
            Assert.AreEqual(1080f / (2f * 16f * 3f), camera.orthographicSize, 0.001f, "integer scale: one source pixel is exactly three screen pixels");
            Save(rt, "gate_x3.png");

            gateCamera.SetScale(4);
            for (var i = 0; i < 6; i++) yield return null;
            Save(rt, "gate_x4.png");

            // A hit flash, a downed member and gear, in one frame.
            var services = gate.Services;
            var dessa = services.Session.Party.Members[3];
            services.ToggleEquip(dessa, "item-iron-helm");
            services.Damage(services.Session.Party.Members[1], "ability-debug-hit");
            services.Session.Characters.TryGetCharacter(services.Session.Party.Members[2], out var corvin);
            corvin.TryGetResource(GateSessionServices.HitPoints, out var hp);
            hp.Current = 1;
            services.Damage(services.Session.Party.Members[2], "ability-debug-hit");
            gateCamera.SetScale(5);
            for (var i = 0; i < 400; i++) yield return null;
            Save(rt, "gate_x5_states.png");

            camera.targetTexture = null;
            rt.Release();
        }
    }
}
