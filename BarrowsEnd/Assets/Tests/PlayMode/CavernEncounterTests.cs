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
using CRPG.Gameplay;
using CRPG.InputSystem;
using CRPG.Quests;
using CRPG.UI;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.UIElements;

namespace BarrowsEnd.Tests
{
    /// <summary>Task 6.2: the authored goblin encounters in Cavern_Upper / Cavern_Middle, fought for real -- spawns must
    /// be reachable from the party, a won fight records its win and shared progress keys, and the two Upper patrols count
    /// toward ONE objective key (the review's B3).</summary>
    public class CavernEncounterTests
    {
        private const string CorePath = "Assets/Scenes/Core.unity";
        private const string UpperPath = "Assets/Scenes/Locations/Cavern_Upper.unity";
        private const string MiddlePath = "Assets/Scenes/Locations/Cavern_Middle.unity";
        private const string UpperProgressKey = "act2-upper-patrol-cleared";   // BarrowsEndCavernsAuthor.UpperClearedProgressKey (Editor-only)
        private const string MiddleProgressKey = "act2-middle-barricade-cleared";
        private static readonly DefinitionId HitPoints = new("resource-hp");

        private ShellBootstrap shell;
        private string savePath;
        private RenderTexture rt;

        private IEnumerator BootAt(string scenePath, string destinationId)
        {
            savePath = Path.Combine(Path.GetTempPath(), $"barrowsend-cavern-{Guid.NewGuid():N}.json");
            ShellBootstrap.StartModeOverride = ShellStartMode.AuthoredParty;
            ShellBootstrap.StartingSceneOverride = (scenePath, destinationId);
            yield return SceneManager.LoadSceneAsync(CorePath, LoadSceneMode.Single);
            shell = UnityEngine.Object.FindFirstObjectByType<ShellBootstrap>();
            shell.SavePath = savePath;
            rt = new RenderTexture(1920, 1080, 24);
            Camera.main.targetTexture = rt;
            yield return WaitUntil(() => shell.IsReady, 20f, "the shell finished booting");
            yield return Seconds(0.3f);

            // A sturdy protagonist: these tests are about encounter wiring, not about the dice.
            var session = shell.Services.Session;
            Assert.IsTrue(session.Characters.TryGetCharacter(session.Party.Members[0], out var protagonist));
            protagonist.AddResource(new ResourceState(HitPoints, 500));
        }

        [TearDown]
        public void Cleanup()
        {
            ShellBootstrap.StartModeOverride = null;
            ShellBootstrap.StartingSceneOverride = null;
            if (Camera.main != null) Camera.main.targetTexture = null;
            if (rt != null) { rt.Release(); rt = null; }
            if (File.Exists(savePath)) File.Delete(savePath);
            if (File.Exists(savePath + ".checkpoint")) File.Delete(savePath + ".checkpoint");
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

        private VisualElement Hud() => UnityEngine.Object.FindFirstObjectByType<HudRoot>().GetComponent<UIDocument>().rootVisualElement;

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

        private List<RuntimeEntityId> HostilesOf(string firedKey, int count) =>
            Enumerable.Range(1, count)
                .Select(n => (ok: shell.Services.Spawner.TryGetCharacterId($"{firedKey}-{n}", out var id, out _, out _), id))
                .Where(t => t.ok).Select(t => t.id).ToList();

        /// <summary>Walks the protagonist into <paramref name="triggerName"/>, checks every spawned goblin can path to the
        /// party, weakens them to 1 HP, then plays the fight like a player (step in, Strike) until the encounter ends.</summary>
        private IEnumerator FightTrigger(string triggerName, int expectedGoblins, Action<EncounterTrigger> onWon = null)
        {
            var session = shell.Services.Session;
            var trigger = UnityEngine.Object.FindObjectsByType<EncounterTrigger>(FindObjectsInactive.Include, FindObjectsSortMode.None).Single(t => t.name == triggerName);
            var protagonistId = session.Party.Members[0];
            var protagonist = GateSessionServices.FindAgent(protagonistId);

            protagonist.Motor.Warp(trigger.transform.position);
            Physics.SyncTransforms();
            yield return WaitUntil(() => shell.CombatSession.IsActive, 5f, $"{triggerName} fired");
            yield return Seconds(0.3f);

            var hostiles = HostilesOf(trigger.FiredKey, 6);
            Assert.AreEqual(expectedGoblins, hostiles.Count, $"{triggerName} spawned {expectedGoblins} goblins");
            foreach (var id in hostiles)
            {
                var agent = GateSessionServices.FindAgent(id);
                Assert.IsTrue(shell.Services.Spawner.GridNavigation.TryCalculatePath(agent.transform.position, protagonist.transform.position, out _),
                    $"a goblin spawned at {agent.transform.position} can path to the party at {protagonist.transform.position}");
                Assert.IsTrue(session.Characters.TryGetCharacter(id, out var goblin));
                goblin.TryGetResource(HitPoints, out var hp);
                hp.Current = 1;
            }

            for (var guard = 0; guard < 80 && shell.CombatSession.IsActive; guard++)
            {
                var turn = shell.CombatSession.CurrentTurnId;
                if (turn == protagonistId)
                {
                    var target = hostiles.Where(h => Hp(h) > 0).Select(GateSessionServices.FindAgent).Where(a => a != null)
                        .OrderBy(a => Vector3.Distance(a.transform.position, protagonist.transform.position)).FirstOrDefault();
                    if (target == null) break;

                    var distance = Vector3.Distance(protagonist.transform.position, target.transform.position);
                    if (distance > 1.3f)
                    {
                        Click(Vector3.MoveTowards(protagonist.transform.position, target.transform.position, distance - 1.0f));
                        yield return Seconds(2.5f);
                    }

                    var strike = Hud().Query<Button>().ToList().FirstOrDefault(b => b.text == "Strike");
                    if (strike != null)
                    {
                        UiClick.Pointer(strike);
                        yield return null;
                        Click(target.transform.position);
                    }
                    yield return Seconds(1.0f);
                }
                else yield return Seconds(0.3f);
            }

            Assert.IsFalse(shell.CombatSession.IsActive, $"{triggerName}'s encounter ended");
            onWon?.Invoke(trigger);
        }

        [UnityTest]
        public IEnumerator BothUpperPatrolsSpawnReachableGoblinsAndCountTowardOneSharedProgressKey()
        {
            yield return BootAt(UpperPath, "from-town");
            var session = shell.Services.Session;
            Assert.AreEqual(0, session.Timeline.CountOccurrences(UpperProgressKey));

            yield return FightTrigger("GoblinPatrolSouth", 2);
            Assert.IsTrue(session.Timeline.HasOccurred("encounter-upper-south-won"));
            Assert.AreEqual(1, session.Timeline.CountOccurrences(UpperProgressKey));

            yield return FightTrigger("GoblinPatrolNorth", 2);
            Assert.IsTrue(session.Timeline.HasOccurred("encounter-upper-north-won"));
            Assert.AreEqual(2, session.Timeline.CountOccurrences(UpperProgressKey),
                "both patrols feed the one key a targetCount-2 objective counts");
        }

        [UnityTest]
        public IEnumerator TheMiddleBarricadeSpawnsThreeReachableGoblinsAndRecordsItsOwnProgressKey()
        {
            yield return BootAt(MiddlePath, "from-upper");
            var session = shell.Services.Session;

            yield return FightTrigger("GoblinBarricade", 3);
            Assert.IsTrue(session.Timeline.HasOccurred("encounter-middle-barricade-won"));
            Assert.AreEqual(1, session.Timeline.CountOccurrences(MiddleProgressKey));
        }

        [UnityTest]
        public IEnumerator ClickingAMushroomPatchHarvestsItOnceAndItStaysGoneAfterARefresh()
        {
            yield return BootAt(UpperPath, "from-town");
            var session = shell.Services.Session;
            var west = UnityEngine.Object.FindObjectsByType<HarvestInteractionAdapter>(FindObjectsInactive.Include, FindObjectsSortMode.None).Single(n => n.name == "MushroomsWest");
            Assert.IsTrue(west.gameObject.activeSelf);
            Assert.AreEqual(0, session.Timeline.CountOccurrences(HarvestInteractionAdapter.SharedKey));

            Click(west.transform.position);
            yield return WaitUntil(() => session.Timeline.HasOccurred(west.NodeKey), 15f, "the party walked to the patch and harvested it");

            Assert.AreEqual(1, session.Timeline.CountOccurrences(HarvestInteractionAdapter.SharedKey));
            Assert.IsFalse(west.gameObject.activeSelf, "the harvested patch disappears");
            StringAssert.StartsWith("Harvested giant mushrooms", shell.LastMessage);

            west.RefreshState(); // what a scene load / save load does: it must stay hidden, never re-offer
            Assert.IsFalse(west.gameObject.activeSelf);
        }

        [UnityTest]
        public IEnumerator EnteringTheCavernsStartsTheDescentAndEachWonFightAdvancesItUntilTheBarricadeCompletesIt()
        {
            yield return BootAt(UpperPath, "from-town");
            var session = shell.Services.Session;
            var questId = new DefinitionId("quest-the-descent");
            // The cavern's entry point sits inside the quest-start volume, so arriving IS entering.
            yield return WaitUntil(() => session.Quests.TryGetQuest(questId, out _), 5f, "the quest started on arrival in the caverns");
            Assert.AreEqual("Quest started: The Descent", shell.LastMessage);
            session.Quests.TryGetQuest(questId, out var quest);
            Assert.AreEqual(QuestStatus.Active, quest.Status);

            yield return FightTrigger("GoblinPatrolSouth", 2);
            Assert.IsFalse(quest.Objectives["clear-upper-patrols"].IsComplete, "one patrol of two is not enough");
            yield return FightTrigger("GoblinPatrolNorth", 2, _ => { });
            yield return WaitUntil(() => quest.Objectives["clear-upper-patrols"].IsComplete, 5f, "both patrols complete the first objective (quest refreshed by the encounter's win)");
            Assert.AreEqual(QuestStatus.Active, quest.Status, "the barricade is still ahead");

            session.Timeline.Record(MiddleProgressKey, TimelineValue.Flag); // the Middle fight itself is covered separately
            shell.RefreshAllActiveQuests();
            Assert.AreEqual(QuestStatus.Completed, quest.Status);
            Assert.AreEqual("Quest complete: The Descent", shell.LastMessage);
        }
    }
}
