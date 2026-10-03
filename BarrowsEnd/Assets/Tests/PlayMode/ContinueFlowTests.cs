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
    /// <summary>Live play: "when I load a save only the main character appears in the party". The reproduction path is the one a player
    /// takes: play a game with recruited companions, save, quit to a fresh boot, choose Continue on the title -- the HUD party panel
    /// must then list the whole saved party with the saved hit points.</summary>
    public class ContinueFlowTests
    {
        private const string CorePath = "Assets/Scenes/Core.unity";
        private static readonly DefinitionId HitPoints = new("resource-hp");

        private string savePath;
        private RenderTexture rt;

        [TearDown]
        public void Cleanup()
        {
            ShellBootstrap.StartModeOverride = null;
            if (Camera.main != null) Camera.main.targetTexture = null;
            if (rt != null) { rt.Release(); rt = null; }
            if (savePath != null && File.Exists(savePath)) File.Delete(savePath);
        }

        private static IEnumerator WaitUntil(Func<bool> condition, float seconds, string what)
        {
            var elapsed = 0f;
            while (!condition() && elapsed < seconds) { elapsed += Mathf.Min(Time.deltaTime, 0.1f); yield return null; }
            Assert.IsTrue(condition(), $"timed out waiting for: {what}");
        }

        private static VisualElement RootOf<T>() where T : Component => UnityEngine.Object.FindFirstObjectByType<T>().GetComponent<UIDocument>().rootVisualElement;

        private IEnumerator LoadTitle()
        {
            ShellBootstrap.StartModeOverride = ShellStartMode.Title;
            yield return SceneManager.LoadSceneAsync(CorePath, LoadSceneMode.Single);
            var shell = UnityEngine.Object.FindFirstObjectByType<ShellBootstrap>();
            shell.SavePath = savePath;
            rt = new RenderTexture(1920, 1080, 24);
            Camera.main.targetTexture = rt;
            yield return WaitUntil(() => shell.Phase == ShellStartPhase.Title, 20f, "the title");
            yield return new WaitForSeconds(0.3f);
        }

        [UnityTest]
        public IEnumerator ContinuingFromTheTitleShowsTheWholeSavedPartyWithTheirSavedHitPoints()
        {
            savePath = Path.Combine(Path.GetTempPath(), $"barrowsend-continue-{Guid.NewGuid():N}.json");

            // First life: New Game as a Barbarian, recruit everyone, wound the protagonist and Silas, save.
            yield return LoadTitle();
            var shell = UnityEngine.Object.FindFirstObjectByType<ShellBootstrap>();
            UiClick.Pointer(RootOf<ShellTitleScreen>().Q<Button>("new-game-button"));
            yield return WaitUntil(() => shell.Phase == ShellStartPhase.Creating, 5f, "the creator");
            yield return new WaitForSeconds(0.3f);
            shell.CreatorModel.ClassRow.Select(new DefinitionId("class-barbarian"));
            yield return null;
            UiClick.Pointer(RootOf<CharacterCreatorScreen>().Q<Button>("confirm-button"));
            yield return WaitUntil(() => shell.IsReady, 30f, "the shell finished booting");
            foreach (var key in new[] { "garrick", "elara", "silas" }) { shell.RecruitCompanion(key); yield return null; }

            var session = shell.Services.Session;
            var saved = session.Party.Members.ToList();
            Assert.AreEqual(4, saved.Count);
            session.Characters.TryGetCharacter(saved[0], out var hero);
            hero.TryGetResource(HitPoints, out var heroHp);
            heroHp.Spend(3);
            session.Characters.TryGetCharacter(CompanionRoster.IdFor("silas"), out var silas);
            silas.TryGetResource(HitPoints, out var silasHp);
            silasHp.Spend(12);
            var expected = saved.Select(id => { session.Characters.TryGetCharacter(id, out var c); c.TryGetResource(HitPoints, out var hp); return $"HP {hp.Current} / {hp.Max}"; }).ToList();
            Assert.IsTrue(shell.SaveNow());

            // Second life: a fresh boot, Continue.
            yield return LoadTitle();
            shell = UnityEngine.Object.FindFirstObjectByType<ShellBootstrap>();
            UiClick.Pointer(RootOf<ShellTitleScreen>().Q<Button>("continue-button"));
            yield return WaitUntil(() => shell.Phase == ShellStartPhase.Playing, 30f, "Continue finished loading");
            yield return new WaitForSeconds(0.8f);

            var loaded = shell.Services.Session;
            CollectionAssert.AreEquivalent(saved, loaded.Party.Members.ToList(), "the whole saved party is back");
            var roster = UnityEngine.Object.FindFirstObjectByType<HudRoot>().GetComponent<UIDocument>().rootVisualElement.Q(name: "party-roster");
            Assert.IsNotNull(roster);
            Assert.AreEqual(4, roster.Children().Count(), "the party panel lists every member, not just the protagonist");
            for (var i = 0; i < saved.Count; i++)
            {
                var hp = roster.Q(name: $"party-card-{saved[i]}")?.Q<Label>(name: "party-card-hp");
                Assert.IsNotNull(hp, $"a card for member {i}");
                Assert.AreEqual(expected[i], hp.text, $"member {i}'s card shows the SAVED hit points, not the boot-time defaults");
            }
        }
    }
}
