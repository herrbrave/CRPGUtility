using System;
using System.Collections;
using System.IO;
using System.Reflection;
using BarrowsEnd.Shell;
using CRPG.InputSystem;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace BarrowsEnd.Tests
{
    /// <summary>Task 3.4: renders the running shell through its real camera (into a RenderTexture, never ScreenCapture) for a human to look at: the town after boot,
    /// the house door opened, and the cave after travelling. PNGs go to %TEMP%/sp. The assertions only prove the frames are not blank.</summary>
    public class ShellCaptureTests
    {
        private static void Save(RenderTexture rt, string name)
        {
            var previous = RenderTexture.active;
            RenderTexture.active = rt;
            var tex = new Texture2D(rt.width, rt.height, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0);
            tex.Apply();
            var dir = Path.Combine(Environment.GetEnvironmentVariable("TEMP") ?? Path.GetTempPath(), "sp");
            Directory.CreateDirectory(dir);
            File.WriteAllBytes(Path.Combine(dir, name), tex.EncodeToPNG());
            var first = tex.GetPixel(0, 0);
            var different = 0;
            foreach (var pixel in tex.GetPixels()) if (pixel != first) different++;
            UnityEngine.Object.Destroy(tex);
            RenderTexture.active = previous;
            Assert.Greater(different, 1000, $"{name} has content");
        }

        private static IEnumerator Seconds(float seconds)
        {
            var elapsed = 0f;
            while (elapsed < seconds) { elapsed += Mathf.Min(Time.deltaTime, 0.1f); yield return null; }
        }

        private static void Click(Vector3 world, RenderTexture rt)
        {
            var source = UnityEngine.Object.FindFirstObjectByType<InputSystemCommandSource>();
            var router = typeof(InputSystemCommandSource).GetProperty("Router", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(source);
            var cam = Camera.main;
            var screen = (Vector2)cam.WorldToScreenPoint(world);
            var ray = cam.ScreenPointToRay(screen);
            Physics.SyncTransforms();
            router.GetType().GetMethod("HandlePointerDown").Invoke(router, new object[] { screen, ray });
            router.GetType().GetMethod("HandlePointerUp").Invoke(router, new object[] { screen, ray, cam, false });
        }

        [UnityTest]
        public IEnumerator TheTownAndTheCaveRenderThroughTheShellsCamera()
        {
            var savePath = Path.Combine(Path.GetTempPath(), "barrowsend-shell-capture.json");
            yield return SceneManager.LoadSceneAsync("Assets/Scenes/Core.unity", LoadSceneMode.Single);
            var shell = UnityEngine.Object.FindFirstObjectByType<ShellBootstrap>();
            shell.SavePath = savePath;
            var waited = 0f;
            while (!shell.IsReady && waited < 20f) { waited += Mathf.Min(Time.deltaTime, 0.1f); yield return null; }
            Assert.IsTrue(shell.IsReady);

            var rt = new RenderTexture(1920, 1080, 24);
            Camera.main.targetTexture = rt;
            yield return Seconds(1.5f);
            Save(rt, "shell_town_start.png");

            // Walk the lead member up to the house, open the door, then travel to the cave.
            Click(new Vector3(6.5f, 0f, 11.5f), rt);
            yield return Seconds(14f);
            Save(rt, "shell_town_door.png");

            Click(new Vector3(14.5f, 0f, 19.5f), rt);
            var elapsed = 0f;
            while (!(SceneManager.GetActiveScene().path.Contains("Cave") && !shell.Gate.IsBusy) && elapsed < 90f) { elapsed += Mathf.Min(Time.deltaTime, 0.1f); yield return null; }
            yield return Seconds(1.5f);
            Camera.main.targetTexture = rt;
            Save(rt, "shell_cave.png");

            Camera.main.targetTexture = null;
            rt.Release();
            if (File.Exists(savePath)) File.Delete(savePath);
        }
    }
}
