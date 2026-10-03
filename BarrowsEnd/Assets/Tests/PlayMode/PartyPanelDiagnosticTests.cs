using System;
using System.Collections;
using System.IO;
using System.Linq;
using BarrowsEnd.Gate;
using BarrowsEnd.Shell;
using CRPG.Actors;
using CRPG.Core;
using CRPG.UI;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.UIElements;

namespace BarrowsEnd.Tests
{
    /// <summary>Diagnostic: with a Barbarian protagonist and all three companions recruited, the party panel must list every member
    /// with their own hit points. Live play showed a single card.</summary>
    public class PartyPanelDiagnosticTests
    {
        private const string CorePath = "Assets/Scenes/Core.unity";
        private ShellBootstrap shell;
        private string savePath;
        private RenderTexture rt;

        [UnitySetUp]
        public IEnumerator Boot()
        {
            savePath = Path.Combine(Path.GetTempPath(), $"barrowsend-party-{Guid.NewGuid():N}.json");
            ShellBootstrap.StartModeOverride = ShellStartMode.AuthoredParty;
            yield return SceneManager.LoadSceneAsync(CorePath, LoadSceneMode.Single);
            shell = UnityEngine.Object.FindFirstObjectByType<ShellBootstrap>();
            shell.SavePath = savePath;
            rt = new RenderTexture(1920, 1080, 24);
            Camera.main.targetTexture = rt;
            float waited = 0f;
            while (!shell.IsReady && waited < 20f) { waited += Mathf.Min(Time.deltaTime, 0.1f); yield return null; }
            Assert.IsTrue(shell.IsReady);
        }

        [TearDown]
        public void Cleanup()
        {
            ShellBootstrap.StartModeOverride = null;
            if (Camera.main != null) Camera.main.targetTexture = null;
            if (rt != null) { rt.Release(); rt = null; }
            if (File.Exists(savePath)) File.Delete(savePath);
        }

        [UnityTest]
        public IEnumerator EveryRecruitedMemberHasItsOwnCardWithItsOwnHitPoints()
        {
            foreach (var key in new[] { "garrick", "elara", "silas" }) { shell.RecruitCompanion(key); yield return null; }
            yield return new WaitForSeconds(0.5f);

            var session = shell.Services.Session;
            Assert.AreEqual(4, session.Party.Members.Count);
            var hud = UnityEngine.Object.FindFirstObjectByType<HudRoot>().GetComponent<UIDocument>().rootVisualElement;
            var cards = hud.Query<VisualElement>().Where(e => e.name != null && e.name.StartsWith("party-card-") && e.name != "party-card-name" && e.name != "party-card-hp").ToList();
            var summary = string.Join(" | ", cards.Select(c => $"{c.name}: {c.Q<Label>("party-card-name")?.text} {c.Q<Label>("party-card-hp")?.text} world={c.worldBound}"));
            Debug.Log($"[PartyPanel] members={session.Party.Members.Count} cards={cards.Count}: {summary}");

            // Dump a screenshot for a human to look at.
            yield return new WaitForEndOfFrame();
            var prev = RenderTexture.active;
            RenderTexture.active = rt;
            var tex = new Texture2D(rt.width, rt.height, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0);
            tex.Apply();
            RenderTexture.active = prev;
            File.WriteAllBytes(Path.Combine(Path.GetTempPath(), "barrowsend-party-panel.png"), tex.EncodeToPNG());

            Assert.AreEqual(4, cards.Count, "one card per party member");
        }
    }
}
