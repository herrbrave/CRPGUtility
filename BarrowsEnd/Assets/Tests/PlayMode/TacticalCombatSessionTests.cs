using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using BarrowsEnd.Gate;
using BarrowsEnd.Shell;
using CRPG.Actors;
using CRPG.Core;
using CRPG.InputSystem;
using CRPG.UI;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.UIElements;

namespace BarrowsEnd.Tests
{
    /// <summary>Task 6.1 (2D-Campaign-Phase6-CampaignContent.md, task breakdown item 6): the palisade-breach
    /// tutorial's full tactical combat stack, end to end, against the REAL Core scene -- the single riskiest new
    /// mechanism in this task (the toolkit's own combat stack, proven only against 3D NavMesh movement before
    /// now, wired here for the first time against BarrowsEnd's 2D grid). Drives the fight through the REAL
    /// ability-bar UI click and the REAL AbilityTargetingGate click (the same click router every other test in
    /// this project already exercises), not a mocked/direct scheduler call. Renders into a real-sized
    /// RenderTexture (ShellCaptureTests.cs's own established precedent): the batch game view is only 640x480, so
    /// a HUD region's fixed panel-unit width eats a disproportionately huge fraction of it, leaving almost no
    /// screen-safe click position at that resolution (found live while writing this test).</summary>
    public class TacticalCombatSessionTests
    {
        private const string CorePath = "Assets/Scenes/Core.unity";
        private static readonly DefinitionId HitPoints = new("resource-hp");
        private static readonly DefinitionId HostileFaction = new("faction-hostile");

        private ShellBootstrap shell;
        private string savePath;
        private RenderTexture rt;

        [UnitySetUp]
        public IEnumerator Boot()
        {
            savePath = Path.Combine(Path.GetTempPath(), $"barrowsend-combat-test-{Guid.NewGuid():N}.json");
            ShellBootstrap.StartModeOverride = ShellStartMode.AuthoredParty; // skip the title screen; real 1-member boot (no MembersOverride)
            yield return SceneManager.LoadSceneAsync(CorePath, LoadSceneMode.Single);
            shell = UnityEngine.Object.FindFirstObjectByType<ShellBootstrap>();
            Assert.IsNotNull(shell);
            shell.SavePath = savePath;
            yield return WaitUntil(() => shell.IsReady, 20f, "the shell finished booting");

            rt = new RenderTexture(1920, 1080, 24);
            Camera.main.targetTexture = rt;
            yield return null;
        }

        [TearDown]
        public void Cleanup()
        {
            ShellBootstrap.StartModeOverride = null;
            if (Camera.main != null) Camera.main.targetTexture = null;
            if (rt != null) { rt.Release(); rt = null; }
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

        // Mirrors TownProofFixturesTests.cs's/RecruitCompanionTests.cs's own Click helper (the real click stack).
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

        private VisualElement HudRootElement() =>
            UnityEngine.Object.FindFirstObjectByType<HudRoot>().GetComponent<UIDocument>().rootVisualElement;

        [UnityTest]
        public IEnumerator AttackingAWeakenedRatThroughTheRealAbilityBarAndTargetingClickWinsAndEndsCombat()
        {
            var session = shell.Services.Session;
            var protagonistId = session.Party.Members[0];
            var protagonistAgent = GateSessionServices.FindAgent(protagonistId);
            Assert.IsNotNull(protagonistAgent);

            Assert.IsTrue(session.RulesDatabase.TryResolve<NpcDefinition>(new DefinitionId("npc-giant-rat"), out var ratDefinition));
            var ratSpot = protagonistAgent.transform.position + new Vector3(1f, 0f, 0f); // within the 1.5-range ability
            var ratAgent = shell.Services.Spawner.SpawnNpc("combat-test-rat", ratDefinition, ratSpot, Quaternion.identity);

            // Weakened to 1 HP so the protagonist's first (always-hits, per this task's own damage-only ability
            // design) Strike is guaranteed to kill it deterministically, without depending on real dice rolls.
            Assert.IsTrue(session.Characters.TryGetCharacter(ratAgent.EntityId, out var ratCharacter));
            Assert.IsTrue(ratCharacter.TryGetResource(HitPoints, out var ratHp));
            ratHp.Current = 1;

            var brain = GiantRatBrain.BuildAndRegister(session.RulesDatabase);
            var combatants = new List<(RuntimeEntityId, DefinitionId)> { (protagonistId, TacticalCombatSession.PartyFaction), (ratAgent.EntityId, HostileFaction) };
            bool? partyWon = null;
            shell.CombatSession.StartEncounter(combatants, HostileFaction, brain, won => partyWon = won);

            Assert.IsTrue(shell.CombatSession.IsActive);
            yield return null;

            // While combat is active, an ordinary ground click must NOT move anyone -- GateCommandSource.
            // CombatMoveOverride suppressing PartyExplorationController is this task's own fix for a real hazard
            // the toolkit's own TacticalPlayerMoveAdapter doc comment names as unsolved by any shipped example
            // (two independent Move subscribers otherwise both reacting to the same click).
            var beforeSuppressedClick = protagonistAgent.transform.position;
            Click(protagonistAgent.transform.position + new Vector3(-3f, 0f, 0f));
            yield return Seconds(0.5f);
            Assert.AreEqual(beforeSuppressedClick, protagonistAgent.transform.position, "exploration movement is suppressed during combat");

            var strikeButton = HudRootElement().Query<Button>().ToList().FirstOrDefault(b => b.text == "Strike");
            Assert.IsNotNull(strikeButton, "the real AbilityBarView shows the protagonist's real Strike ability");
            UiClick.Pointer(strikeButton);
            yield return null;

            Click(ratAgent.transform.position); // the real AbilityTargetingGate click, through the same click router every other click test in this project uses
            yield return Seconds(2f);

            Assert.IsFalse(shell.CombatSession.IsActive, "combat ended after the only hostile died");
            Assert.IsTrue(partyWon.HasValue && partyWon.Value, "the party won");

            // Exploration movement is restored once combat ends.
            var beforeRestoredClick = protagonistAgent.transform.position;
            Click(protagonistAgent.transform.position + new Vector3(-3f, 0f, 0f));
            yield return Seconds(1.5f);
            Assert.Greater(Vector3.Distance(beforeRestoredClick, protagonistAgent.transform.position), 0.5f, "exploration movement works again after combat");
        }
    }
}
