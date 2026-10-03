using System;
using System.Collections;
using System.IO;
using System.Linq;
using BarrowsEnd.Gate;
using BarrowsEnd.Shell;
using CRPG.Core;
using CRPG.UI;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.UIElements;

namespace BarrowsEnd.Tests
{
    /// <summary>Task 5.1: the real HUD (HudBootstrap, replacing ShellPanel) end to end, driven through the real
    /// click stack. Boots the same way ShellTests does (AuthoredParty, straight into the town).</summary>
    public class HudBootstrapTests
    {
        private const string CorePath = "Assets/Scenes/Core.unity";

        private ShellBootstrap shell;
        private HudBootstrap hud;
        private HudRoot hudRoot;
        private string savePath;

        [UnitySetUp]
        public IEnumerator Boot()
        {
            savePath = Path.Combine(Path.GetTempPath(), $"barrowsend-hud-test-{Guid.NewGuid():N}.json");
            ShellBootstrap.StartModeOverride = ShellStartMode.AuthoredParty;
            // Task 6.1: this file's assertions assume the OLD 4-member boot (all-four-cards, Members[1] card
            // selection) -- the real Core scene now authors only the protagonist, so keep booting the demo roster.
            ShellBootstrap.MembersOverride = GateLayout.DefaultMembers();
            yield return SceneManager.LoadSceneAsync(CorePath, LoadSceneMode.Single);
            shell = UnityEngine.Object.FindFirstObjectByType<ShellBootstrap>();
            Assert.IsNotNull(shell);
            shell.SavePath = savePath;
            yield return WaitUntil(() => shell.IsReady, 20f, "the shell finished booting");

            hud = UnityEngine.Object.FindFirstObjectByType<HudBootstrap>();
            Assert.IsNotNull(hud, "the authored Core scene has a HudBootstrap");
            hudRoot = hud.GetComponent<HudRoot>();
            Assert.IsNotNull(hudRoot, "HudBootstrap's GameObject also carries a HudRoot (RequireComponent)");
            yield return WaitUntil(() => Root().Q(name: "party-roster") != null, 10f, "the HUD finished building its first frame after IsReady");
            yield return null; // one more frame so GeometryChangedEvent has fired on the hp-bars
        }

        [TearDown]
        public void Cleanup()
        {
            ShellBootstrap.StartModeOverride = null;
            ShellBootstrap.MembersOverride = null;
            if (File.Exists(savePath)) File.Delete(savePath);
        }

        private static IEnumerator Seconds(float seconds)
        {
            var elapsed = 0f;
            while (elapsed < seconds) { elapsed += Mathf.Min(Time.deltaTime, 0.1f); yield return null; }
        }

        private static IEnumerator WaitUntil(Func<bool> condition, float seconds, string what)
        {
            var elapsed = 0f;
            while (!condition() && elapsed < seconds) { elapsed += Mathf.Min(Time.deltaTime, 0.1f); yield return null; }
            Assert.IsTrue(condition(), $"timed out waiting for: {what}");
        }

        private VisualElement Root() => hud.GetComponent<UIDocument>().rootVisualElement;
        private GameSessionAccessor Session => new(shell.Services.Session);

        // Thin wrapper so this file doesn't need `using CRPG.Gameplay;` just for one property read.
        private readonly struct GameSessionAccessor
        {
            private readonly CRPG.Gameplay.GameSession session;
            public GameSessionAccessor(CRPG.Gameplay.GameSession session) => this.session = session;
            public CRPG.Party.PartyState Party => session.Party;
            public CRPG.Actors.ICharacterRegistry Characters => session.Characters;
        }

        [UnityTest]
        public IEnumerator ThePartyCardsFollowHitPointChangesWithoutAnySelectionChange()
        {
            yield return null;
            var memberId = Session.Party.Members[1];
            Session.Characters.TryGetCharacter(memberId, out var character);
            character.TryGetResource(BarrowsEnd.Gate.GateSessionServices.HitPoints, out var hp);
            var startingHp = hp.Current;

            hp.Spend(3); // combat damage never touches Party.Changed
            yield return null;
            yield return null;

            var label = Root().Q(name: "party-roster").Q(name: $"party-card-{memberId}").Q<Label>(name: "party-card-hp");
            Assert.AreEqual($"HP {startingHp - 3} / {hp.Max}", label.text, "the card shows the damage as it lands");
        }

        [UnityTest]
        public IEnumerator TheHudShowsAllFourPartyMembersWithCorrectNamesAndHp()
        {
            yield return null;
            var roster = Root().Q(name: "party-roster");
            var cards = roster.Children().ToList();
            Assert.AreEqual(4, cards.Count, "all four authored members got a card");

            foreach (var memberId in Session.Party.Members)
            {
                Session.Characters.TryGetCharacter(memberId, out var character);
                character.TryGetResource(BarrowsEnd.Gate.GateSessionServices.HitPoints, out var hp);
                var card = roster.Q(name: $"party-card-{memberId}");
                Assert.IsNotNull(card, $"a card exists for {memberId}");
                Assert.AreEqual($"HP {hp.Current} / {hp.Max}", card.Q<Label>(name: "party-card-hp").text);
            }
        }

        [UnityTest]
        public IEnumerator ClickingAMembersStripSelectsIt()
        {
            var secondMember = Session.Party.Members[1];
            var card = Root().Q(name: $"party-card-{secondMember}");
            Assert.IsFalse(Session.Party.IsSelected(secondMember), "not selected before the click (only the active member is selected on boot)");

            UiClick.Pointer(card);
            yield return null;

            Assert.IsTrue(Session.Party.IsSelected(secondMember), "PartyPresenter.SelectCharacter really ran");
            // Refresh() rebuilds the roster from scratch (Clear + re-add), so `card` is now a removed, stale
            // element -- query for the new one the click's own Refresh() created.
            var cardAfter = Root().Q(name: $"party-card-{secondMember}");
            Assert.IsTrue(cardAfter.ClassListContains("party-card--selected"));
        }

        [UnityTest]
        public IEnumerator AClickOutsideBothHudRegionsIsNotCapturedButAClickInsideARegionIs()
        {
            var capture = new UIToolkitPointerCapture();
            var roster = Root().Q(name: "party-roster");
            var insideRoster = roster.worldBound.center;
            var outside = new Vector2(Screen.width * 0.5f, Screen.height * 0.5f); // the screen centre: clear of both top-corner regions

            yield return null;
            Assert.IsTrue(capture.IsPointerOverUI(insideRoster), "a point inside the roster region must be captured");
            Assert.IsFalse(capture.IsPointerOverUI(outside), "the screen centre, outside both HUD regions, must NOT be captured");
        }

        [UnityTest]
        public IEnumerator SaveButtonWritesARealSaveFile()
        {
            Assert.IsFalse(File.Exists(savePath));
            UiClick.Pointer(Root().Q(name: "save-button"));
            yield return null;
            Assert.IsTrue(File.Exists(savePath), "the real Save button called ShellBootstrap.SaveNow");
        }

        [UnityTest]
        public IEnumerator ZoomButtonsChangeTheCameraRigsScale()
        {
            var before = shell.CameraRig.Rig.Scale;
            UiClick.Pointer(Root().Q(name: "zoom-in-button"));
            yield return null;
            Assert.AreNotEqual(before, shell.CameraRig.Rig.Scale, "Zoom + called the real camera rig, not a no-op");
        }

        [UnityTest]
        public IEnumerator AfterLoadTheHudShowsTheSavedHpNotWhateverWasThereWhenLoadWasClicked()
        {
            // The test this whole task exists to make possible (Opus review, Task 5.1): "shows the restored HP"
            // can only fail if something actually changes the HP between save and load.
            var memberId = Session.Party.Members[0];
            Session.Characters.TryGetCharacter(memberId, out var character);
            character.TryGetResource(BarrowsEnd.Gate.GateSessionServices.HitPoints, out var hp);
            var savedCurrent = hp.Current;
            Assert.IsTrue(shell.SaveNow());

            // Damage AFTER the save, in memory only (a direct ResourceState mutation, not through an ability --
            // it deliberately does NOT raise PartyState.Changed, so it does not by itself refresh the HUD; that
            // is not what this test is checking). What matters is what the HUD shows once something DOES
            // rebuild it -- here, the load.
            hp.Current = Mathf.Max(1, savedCurrent - 5);
            yield return null;

            UiClick.Pointer(Root().Q(name: "load-button"));
            yield return WaitUntil(() => !shell.IsLoading, 20f, "the load finished");
            yield return Seconds(0.3f);

            var reloadedMemberId = Session.Party.Members[0]; // same fixed id, a NEW session/PartyState underneath
            var reloadedCard = Root().Q(name: $"party-card-{reloadedMemberId}");
            Assert.IsNotNull(reloadedCard, "the HUD rebuilt against the new session (PartyView.Rebind ran)");
            Assert.AreEqual($"HP {savedCurrent} / {hp.Max}", reloadedCard.Q<Label>(name: "party-card-hp").text, "the SAVED (undamaged) HP shows, not the in-memory damaged value from before the load");
        }
    }
}
