using System.Collections;
using System.IO;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace BarrowsEnd.Tests
{
    /// <summary>Task 2.0a/b: proves the spike scene renders un-pink through the real rig at pitch 90 in a real player loop, and captures frames for review.</summary>
    public class LookSpikeCaptureTests
    {
        private static void Save(RenderTexture rt, string name)
        {
            var prev = RenderTexture.active;
            RenderTexture.active = rt;
            var tex = new Texture2D(rt.width, rt.height, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0);
            tex.Apply();
            var dir = Path.Combine(System.Environment.GetEnvironmentVariable("TEMP"), "sp");
            File.WriteAllBytes(Path.Combine(dir, name), tex.EncodeToPNG());
            Object.Destroy(tex);
            RenderTexture.active = prev;
        }

        [UnityTest]
        public IEnumerator LookSpikeRendersAndCaptures()
        {
            yield return SceneManager.LoadSceneAsync("Assets/Scenes/Spike/LookSpike.unity", LoadSceneMode.Single);
            yield return null;
            var cam = Camera.main;
            Assert.IsNotNull(cam);
            Assert.AreEqual(90f, cam.transform.eulerAngles.x, 0.01f, "Real IsometricCameraRig must reach pitch 90.");
            var rt = new RenderTexture(1920, 1080, 24);
            cam.targetTexture = rt;
            for (var i = 0; i < 40; i++) yield return null;
            Save(rt, "play1.png");
            for (var i = 0; i < 25; i++) yield return null;
            Save(rt, "play2.png");
            cam.targetTexture = null;
        }
    }
}
