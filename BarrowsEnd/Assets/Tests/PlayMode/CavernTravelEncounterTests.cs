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
    /// <summary>Task 6.2 live-gate regression: the Middle-level barricade fight reached the way a player reaches it --
    /// by fighting through Cavern_Upper and TRAVELLING through the real arch -- not by booting straight into the level.
    /// Live play found the Middle trigger (loaded additively at the same world coordinates) firing on the party's walk to
    /// the Upper exit, spawning the goblins into the scene about to unload: a fight against vanished enemies, with the
    /// encounter burned for good. Goblins must exist, be visible, reach the party and actually act.</summary>
    public class CavernTravelEncounterTests
    {
        private const string CorePath = "Assets/Scenes/Core.unity";
        private const string UpperPath = "Assets/Scenes/Locations/Cavern_Upper.unity";
        private const string MiddlePath = "Assets/Scenes/Locations/Cavern_Middle.unity";
        private static readonly DefinitionId HitPoints = new("resource-hp");

        private ShellBootstrap shell;
        private string savePath;
        private RenderTexture rt;

        [UnitySetUp]
        public IEnumerator Boot()
        {
            savePath = Path.Combine(Path.GetTempPath(), $"barrowsend-travel-{Guid.NewGuid():N}.json");
            ShellBootstrap.StartModeOverride = ShellStartMode.AuthoredParty;
            ShellBootstrap.StartingSceneOverride = (UpperPath, "from-town");
            yield return SceneManager.LoadSceneAsync(CorePath, LoadSceneMode.Single);
            shell = UnityEngine.Object.FindFirstObjectByType<ShellBootstrap>();
            shell.SavePath = savePath;
            rt = new RenderTexture(1920, 1080, 24);
            Camera.main.targetTexture = rt;
            yield return WaitUntil(() => shell.IsReady, 20f, "the shell finished booting");
            yield return new WaitForSeconds(0.3f);

            var session = shell.Services.Session;
            session.Characters.TryGetCharacter(session.Party.Members[0], out var hero);
            hero.AddResource(new ResourceState(HitPoints, 500)); // a sturdy hero: this is about wiring, not the dice
        }

        [TearDown]
        public void Cleanup()
        {
            ShellBootstrap.StartModeOverride = null;
            ShellBootstrap.StartingSceneOverride = null;
            if (Camera.main != null) Camera.main.targetTexture = null;
            if (rt != null) { rt.Release(); rt = null; }

            // This test ends mid-fight, and the next fixture (CharacterCreatorScreenTests) loads no scene of its own: the
            // combat HUD left in this one would sit over a corner that fixture treats as empty. Remove it.
            foreach (var hud in UnityEngine.Object.FindObjectsByType<HudRoot>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                UnityEngine.Object.DestroyImmediate(hud.gameObject);
            if (File.Exists(savePath)) File.Delete(savePath);
            if (File.Exists(savePath + ".checkpoint")) File.Delete(savePath + ".checkpoint");
        }

        private static IEnumerator WaitUntil(Func<bool> condition, float seconds, string what)
        {
            var elapsed = 0f;
            while (!condition() && elapsed < seconds) { elapsed += Mathf.Min(Time.deltaTime, 0.1f); yield return null; }
            Assert.IsTrue(condition(), $"timed out waiting for: {what}");
        }

        private void Click(Vector3 world)
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

        private int Hp(RuntimeEntityId id) =>
            shell.Services.Session.Characters.TryGetCharacter(id, out var c) && c.TryGetResource(HitPoints, out var hp) ? hp.Current : -1;

        private VisualElement Hud() => UnityEngine.Object.FindFirstObjectByType<HudRoot>().GetComponent<UIDocument>().rootVisualElement;

        /// <summary>Fires a patrol and plays it out (weakened goblins) so the combat session has real history.</summary>
        private IEnumerator WinFight(string triggerName)
        {
            var session = shell.Services.Session;
            var trigger = UnityEngine.Object.FindObjectsByType<EncounterTrigger>(FindObjectsInactive.Include, FindObjectsSortMode.None).Single(t => t.name == triggerName);
            var heroId = session.Party.Members[0];
            var hero = GateSessionServices.FindAgent(heroId);
            hero.Motor.Warp(trigger.transform.position);
            Physics.SyncTransforms();
            yield return WaitUntil(() => shell.CombatSession.IsActive, 5f, $"{triggerName} fired");
            yield return new WaitForSeconds(0.3f);

            var goblins = Enumerable.Range(1, 4)
                .Select(n => (ok: shell.Services.Spawner.TryGetCharacterId($"{trigger.FiredKey}-{n}", out var id, out _, out _), id))
                .Where(t => t.ok).Select(t => t.id).ToList();
            foreach (var g in goblins)
            {
                session.Characters.TryGetCharacter(g, out var c);
                c.TryGetResource(HitPoints, out var hp);
                hp.Current = 1;
            }

            for (var guard = 0; guard < 80 && shell.CombatSession.IsActive; guard++)
            {
                if (shell.CombatSession.CurrentTurnId == heroId)
                {
                    var target = goblins.Where(g => Hp(g) > 0).Select(GateSessionServices.FindAgent).Where(a => a != null)
                        .OrderBy(a => Vector3.Distance(a.transform.position, hero.transform.position)).FirstOrDefault();
                    if (target == null) break;

                    var d = Vector3.Distance(hero.transform.position, target.transform.position);
                    if (d > 1.3f)
                    {
                        Click(Vector3.MoveTowards(hero.transform.position, target.transform.position, d - 1.0f));
                        yield return new WaitForSeconds(2.5f);
                    }

                    var strike = Hud().Query<Button>().ToList().FirstOrDefault(b => b.text == "Strike");
                    if (strike != null)
                    {
                        UiClick.Pointer(strike);
                        yield return null;
                        Click(target.transform.position);
                    }

                    yield return new WaitForSeconds(1f);
                }
                else yield return new WaitForSeconds(0.3f);
            }

            Assert.IsFalse(shell.CombatSession.IsActive, $"{triggerName} was won");
        }

        [UnityTest]
        public IEnumerator TheBarricadeGoblinsAfterBothPatrolsAndRealTravelAreVisibleReachableAndActuallyAct()
        {
            yield return WinFight("GoblinPatrolSouth");
            yield return WinFight("GoblinPatrolNorth");

            var arch = GameObject.Find("TravelToMiddle");
            Assert.IsNotNull(arch);
            var heroId = shell.Services.Session.Party.Members[0];
            GateSessionServices.FindAgent(heroId).Motor.Warp(arch.transform.position + new Vector3(0f, 0f, -2f));
            Physics.SyncTransforms();
            Click(arch.transform.position);
            yield return WaitUntil(() => SceneManager.GetActiveScene().path == MiddlePath && shell.IsReady, 40f, "travelled to Cavern_Middle");
            yield return new WaitForSeconds(0.5f);

            var session = shell.Services.Session;
            Assert.IsFalse(session.Timeline.HasOccurred("encounter-middle-barricade-triggered"),
                "the Middle barricade must NOT have fired from the Upper level while the party walked to the exit");

            var trigger = UnityEngine.Object.FindObjectsByType<EncounterTrigger>(FindObjectsInactive.Include, FindObjectsSortMode.None).Single(t => t.name == "GoblinBarricade");
            var hero = GateSessionServices.FindAgent(session.Party.Members[0]);
            hero.Motor.Warp(trigger.transform.position);
            Physics.SyncTransforms();
            yield return WaitUntil(() => shell.CombatSession.IsActive, 5f, "the barricade encounter fired once the Middle level was active");
            yield return new WaitForSeconds(0.5f);

            var goblinIds = new List<RuntimeEntityId>();
            for (var n = 1; n <= 3; n++)
            {
                Assert.IsTrue(shell.Services.Spawner.TryGetCharacterId($"{trigger.FiredKey}-{n}", out var id, out _, out _), $"goblin {n} has a spawn record");
                goblinIds.Add(id);
                var agent = GateSessionServices.FindAgent(id);
                Assert.IsNotNull(agent, $"goblin {n} has a live agent");
                Assert.AreEqual(MiddlePath, agent.gameObject.scene.path, $"goblin {n} lives in the Middle scene, not one about to unload");
                Assert.IsTrue(agent.GetComponentsInChildren<SpriteRenderer>(true).Any(r => r.enabled && r.sprite != null), $"goblin {n} has a visible sprite");
                Assert.IsTrue(shell.Services.Spawner.GridNavigation.TryCalculatePath(agent.transform.position, hero.transform.position, out _),
                    $"goblin {n} at {agent.transform.position} can path to the party");
            }

            // End the party's turn and watch the goblins: at least one must hurt the hero within a few rounds.
            var before = Hp(session.Party.Members[0]);
            for (var guard = 0; guard < 40 && Hp(session.Party.Members[0]) >= before; guard++)
            {
                if (session.Party.Members.Contains(shell.CombatSession.CurrentTurnId ?? default))
                {
                    var endTurn = Hud().Query<Button>().ToList().FirstOrDefault(b => b.text == "End Turn");
                    if (endTurn != null) UiClick.Pointer(endTurn);
                }

                yield return new WaitForSeconds(0.5f);
            }

            Assert.Less(Hp(session.Party.Members[0]), before, "the goblins acted and hurt the party");
        }
    }
}
