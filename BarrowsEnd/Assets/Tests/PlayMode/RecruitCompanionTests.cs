using System;
using System.Collections;
using System.IO;
using System.Linq;
using System.Reflection;
using BarrowsEnd.Gate;
using BarrowsEnd.Shell;
using CRPG.InputSystem;
using CRPG.Saving;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace BarrowsEnd.Tests
{
    /// <summary>Task 6.1 (2D-Campaign-Phase6-CampaignContent.md, task breakdown item 4): the recruit mechanism
    /// end to end against the REAL Core scene (no StartingSceneOverride/MembersOverride here on purpose -- proving
    /// the real 1-member boot and ShellBootstrap.RecruitCompanion is the whole point). This is this task's
    /// highest-risk new mechanism per the plan's own Risk 2: no existing code built a fresh CharacterState + a
    /// fresh controllable CharacterAgent outside of SpawnParty's own boot-time loop, or registered a motor with a
    /// live PartyExplorationController outside of GateSessionServices.Rebind, before this task.</summary>
    public class RecruitCompanionTests
    {
        private const string CorePath = "Assets/Scenes/Core.unity";
        private ShellBootstrap shell;
        private string savePath;

        [UnitySetUp]
        public IEnumerator Boot()
        {
            savePath = Path.Combine(Path.GetTempPath(), $"barrowsend-recruit-test-{Guid.NewGuid():N}.json");
            ShellBootstrap.StartModeOverride = ShellStartMode.AuthoredParty; // skip the title screen
            yield return SceneManager.LoadSceneAsync(CorePath, LoadSceneMode.Single);
            shell = UnityEngine.Object.FindFirstObjectByType<ShellBootstrap>();
            Assert.IsNotNull(shell);
            shell.SavePath = savePath;
            yield return WaitUntil(() => shell.IsReady, 20f, "the shell finished booting");
        }

        [TearDown]
        public void Cleanup()
        {
            ShellBootstrap.StartModeOverride = null;
            if (File.Exists(savePath)) File.Delete(savePath);
        }

        private static IEnumerator WaitUntil(Func<bool> condition, float seconds, string what)
        {
            var elapsed = 0f;
            while (!condition() && elapsed < seconds) { elapsed += Mathf.Min(Time.deltaTime, 0.1f); yield return null; }
            Assert.IsTrue(condition(), $"timed out waiting for: {what}");
        }

        private static IEnumerator Seconds(float seconds)
        {
            var elapsed = 0f;
            while (elapsed < seconds) { elapsed += Mathf.Min(Time.deltaTime, 0.1f); yield return null; }
        }

        // Mirrors TownProofFixturesTests.cs's own Click helper (the real click stack, screen-space).
        private void Click(Vector3 world)
        {
            var source = UnityEngine.Object.FindFirstObjectByType<InputSystemCommandSource>();
            var router = typeof(InputSystemCommandSource).GetProperty("Router", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(source);
            var routerType = router.GetType();
            var cam = Camera.main;
            var screen = (Vector2)cam.WorldToScreenPoint(world);
            var ray = cam.ScreenPointToRay(screen);
            Physics.SyncTransforms();
            routerType.GetMethod("HandlePointerDown").Invoke(router, new object[] { screen, ray });
            routerType.GetMethod("HandlePointerUp").Invoke(router, new object[] { screen, ray, cam, false });
        }

        [UnityTest]
        public IEnumerator ARealBootStartsWithOnlyTheProtagonistControllable()
        {
            yield return null;
            Assert.AreEqual(1, shell.Services.Session.Party.Members.Count);
        }

        [UnityTest]
        public IEnumerator RecruitingACompanionAddsAControllableSelectableCorrectlyNamedPartyMemberWithNoSceneReload()
        {
            var before = shell.Services.Session.Party.Members.Count;
            var activeScene = SceneManager.GetActiveScene();

            shell.RecruitCompanion("garrick");
            yield return null;

            Assert.AreEqual(activeScene, SceneManager.GetActiveScene(), "no scene reload happened");
            var id = CompanionRoster.IdFor("garrick");
            Assert.AreEqual(before + 1, shell.Services.Session.Party.Members.Count);
            Assert.IsTrue(shell.Services.Session.Party.Members.Contains(id));
            Assert.AreEqual("Garrick", shell.DisplayNameFor(id));

            var agent = GateSessionServices.FindAgent(id);
            Assert.IsNotNull(agent, "a real CharacterAgent exists for the recruit");
            Assert.IsNotNull(agent.Motor, "agent.Motor is null right after recruiting");

            // Recruiting fell back to the active member's own position (no Town fixture authored yet -- that's
            // item 8/10's job), so the recruit spawns co-located with the protagonist. Warp clear of them first
            // so the click below unambiguously targets open ground, not another character's own collider (which
            // the click router resolves to Select, not Move -- a confound in the TEST's geometry, not a
            // real-content concern, since a real Town fixture will never spawn a recruit on top of anyone).
            agent.Motor.Warp(agent.transform.position + new Vector3(6f, 0f, 6f));
            yield return null;

            shell.Services.Session.Party.Select(id);
            var from = agent.transform.position;
            Click(new Vector3(from.x + 2f, 0f, from.z));
            yield return Seconds(1.5f);
            Assert.Greater(Vector3.Distance(from, agent.transform.position), 0.5f,
                "the recruit walks when the ground is clicked -- proves the motor was registered with the live " +
                "PartyExplorationController (GateSessionServices.RegisterMemberMotor), not just added to PartyState");
        }

        [UnityTest]
        public IEnumerator RecruitingTheSameCompanionTwiceIsIdempotent()
        {
            shell.RecruitCompanion("garrick");
            yield return null;
            var countAfterFirst = shell.Services.Session.Party.Members.Count;

            Assert.DoesNotThrow(() => shell.RecruitCompanion("garrick"));
            yield return null;

            Assert.AreEqual(countAfterFirst, shell.Services.Session.Party.Members.Count);
        }

        [UnityTest]
        public IEnumerator ARecruitedCompanionSurvivesASaveThenALoadAfterAFreshBoot()
        {
            shell.RecruitCompanion("garrick");
            yield return null;
            Assert.IsTrue(shell.Services.Session.Party.Members.Contains(CompanionRoster.IdFor("garrick")));
            Assert.IsTrue(shell.SaveNow(), "saved");

            // Simulate a process restart: destroy this ShellBootstrap entirely and boot a brand-new Core scene
            // instance, whose own partyVisualPrefabs dictionary starts empty (a per-instance field, not static) --
            // if SpawnParty didn't pre-populate the 3 fixed companion ids unconditionally at boot, restoring
            // Garrick here would throw ViewBindingFailed (the real crash the Opus review found in the v1 design,
            // not a hypothetical).
            ShellBootstrap.StartModeOverride = ShellStartMode.AuthoredParty;
            yield return SceneManager.LoadSceneAsync(CorePath, LoadSceneMode.Single);
            var freshShell = UnityEngine.Object.FindFirstObjectByType<ShellBootstrap>();
            Assert.IsNotNull(freshShell);
            freshShell.SavePath = savePath;
            yield return WaitUntil(() => freshShell.IsReady, 20f, "the fresh shell finished booting");
            Assert.AreEqual(1, freshShell.Services.Session.Party.Members.Count, "a fresh boot starts with only the protagonist");

            var outcome = LoadOutcome.LoadAlreadyInProgress;
            var done = false;
            freshShell.LoadNow(o => { outcome = o; done = true; });
            yield return WaitUntil(() => done, 20f, "the load finished");

            Assert.AreEqual(LoadOutcome.Success, outcome);
            Assert.IsTrue(freshShell.Services.Session.Party.Members.Contains(CompanionRoster.IdFor("garrick")), "Garrick restored");
            Assert.AreEqual("Garrick", freshShell.DisplayNameFor(CompanionRoster.IdFor("garrick")));
            var agent = GateSessionServices.FindAgent(CompanionRoster.IdFor("garrick"));
            Assert.IsNotNull(agent, "the restored recruit has a real, bound CharacterAgent");
        }
    }
}
