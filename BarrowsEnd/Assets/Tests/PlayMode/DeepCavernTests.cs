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
using CRPG.Navigation.Grid;
using CRPG.UI;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.UIElements;

namespace BarrowsEnd.Tests
{
    /// <summary>Task 6.3: Cavern_Deep played the way a player reaches it -- real travel from the Middle level, real clicks, the
    /// real lever and portcullis, the real boss trigger. Includes the review's blockers: the sealed arena is unreachable until the
    /// lever; spawns are reachable and in the Deep scene; the gate re-derives its state after travel and after a same-scene load.</summary>
    public class DeepCavernTests
    {
        private const string CorePath = "Assets/Scenes/Core.unity";
        private const string MiddlePath = "Assets/Scenes/Locations/Cavern_Middle.unity";
        private const string DeepPath = "Assets/Scenes/Locations/Cavern_Deep.unity";
        private const string LeverKey = "deep-lever-pulled";
        private static readonly DefinitionId HitPoints = new("resource-hp");

        private ShellBootstrap shell;
        private string savePath;
        private RenderTexture rt;

        private IEnumerator BootAt(string scenePath, string destinationId)
        {
            savePath = Path.Combine(Path.GetTempPath(), $"barrowsend-deep-{Guid.NewGuid():N}.json");
            ShellBootstrap.StartModeOverride = ShellStartMode.AuthoredParty;
            ShellBootstrap.StartingSceneOverride = (scenePath, destinationId);
            yield return SceneManager.LoadSceneAsync(CorePath, LoadSceneMode.Single);
            shell = UnityEngine.Object.FindFirstObjectByType<ShellBootstrap>();
            shell.SavePath = savePath;
            rt = new RenderTexture(1920, 1080, 24);
            Camera.main.targetTexture = rt;
            yield return WaitUntil(() => shell.IsReady, 20f, "the shell finished booting");
            yield return new WaitForSeconds(0.3f);

            var session = shell.Services.Session;
            session.Characters.TryGetCharacter(session.Party.Members[0], out var hero);
            hero.AddResource(new ResourceState(HitPoints, 500));
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

            // Several tests end mid-fight; the next fixture may load no scene of its own (see CavernTravelEncounterTests).
            foreach (var hud in UnityEngine.Object.FindObjectsByType<HudRoot>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                UnityEngine.Object.DestroyImmediate(hud.gameObject);
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

        private static T Named<T>(string name) where T : Component =>
            UnityEngine.Object.FindObjectsByType<T>(FindObjectsInactive.Include, FindObjectsSortMode.None).Single(t => t.name == name);

        private static Vector3 Centre(int x, int y) => new(x + 0.5f, 0f, y + 0.5f);

        private CharacterAgent Hero() => GateSessionServices.FindAgent(shell.Services.Session.Party.Members[0]);

        private bool CanPath(Vector3 from, Vector3 to) => shell.Services.Spawner.GridNavigation.TryCalculatePath(from, to, out _);

        /// <summary>Pre-fires the patrols so the walk to the lever is clear of fights (their spawn/reach tests are separate).</summary>
        private void ClearPatrols()
        {
            var session = shell.Services.Session;
            foreach (var name in new[] { "ThrallPatrolSouth", "ThrallPatrolNorth" })
            {
                var trigger = Named<EncounterTrigger>(name);
                session.Timeline.Record(trigger.FiredKey, TimelineValue.Flag);
                trigger.RefreshFiredState();
            }
        }

        private static readonly string[] Patrols = { "ThrallPatrolSouth", "ThrallPatrolNorth" };

        [UnityTest]
        public IEnumerator TravellingFromTheMiddleLevelArrivesQuietStartsTheQuestAndLeavesTheGateClosed()
        {
            yield return BootAt(MiddlePath, "from-upper");
            var session = shell.Services.Session;
            var barricade = Named<EncounterTrigger>("GoblinBarricade");
            session.Timeline.Record(barricade.FiredKey, TimelineValue.Flag); // the fight on the way is covered by CavernTravelEncounterTests
            barricade.RefreshFiredState();

            var arch = GameObject.Find("TravelToDeep");
            Assert.IsNotNull(arch);
            Hero().Motor.Warp(arch.transform.position + new Vector3(0f, 0f, -2f));
            Physics.SyncTransforms();
            Click(arch.transform.position);
            yield return WaitUntil(() => SceneManager.GetActiveScene().path == DeepPath && shell.IsReady, 40f, "travelled to Cavern_Deep");
            yield return new WaitForSeconds(1f);

            Assert.IsFalse(shell.CombatSession.IsActive, "arriving does not start a fight");
            foreach (var key in new[] { "encounter-deep-south-triggered", "encounter-deep-north-triggered", "encounter-deep-boss-triggered" })
                Assert.IsFalse(shell.Services.Session.Timeline.HasOccurred(key), $"{key} did not fire on arrival");
            Assert.IsTrue(shell.Services.Session.Quests.TryGetQuest(new DefinitionId("quest-the-deep-dark"), out var quest), "the Deep Dark quest started on arrival");
            Assert.AreEqual(CRPG.Quests.QuestStatus.Active, quest.Status);

            var gate = Named<PortcullisGate>("Portcullis");
            Assert.IsFalse(gate.IsOpen);
            var grid = WalkabilityGridProvider.FindInScene(SceneManager.GetActiveScene()).Grid;
            foreach (var x in new[] { 16, 17, 18 }) Assert.IsFalse(grid.IsWalkable(new GridCoordinate(x, 18)), $"gate cell {x},18 blocks");
        }

        [UnityTest]
        public IEnumerator AfterRealTravelTheSealedGateHintAppearsAndThePlayerCanFindAndPullTheLever()
        {
            yield return BootAt(MiddlePath, "from-upper");
            var middleBarricade = Named<EncounterTrigger>("GoblinBarricade");
            shell.Services.Session.Timeline.Record(middleBarricade.FiredKey, TimelineValue.Flag);
            middleBarricade.RefreshFiredState();
            var arch = GameObject.Find("TravelToDeep");
            Hero().Motor.Warp(arch.transform.position + new Vector3(0f, 0f, -2f));
            Physics.SyncTransforms();
            Click(arch.transform.position);
            yield return WaitUntil(() => SceneManager.GetActiveScene().path == DeepPath && shell.IsReady, 40f, "travelled to Cavern_Deep");
            yield return new WaitForSeconds(0.5f);

            var session = shell.Services.Session;
            ClearPatrols();

            // Reaching the sealed gate tells the player what to do.
            Hero().Motor.Warp(Centre(17, 16));
            Physics.SyncTransforms();
            yield return WaitUntil(() => shell.LastMessage != null && shell.LastMessage.Contains("lever"), 4f, "the sealed-gate hint");

            // The lever is close to that corridor and works from a fresh arrival (not just a direct boot into the level).
            var lever = Named<LeverInteractionAdapter>("DeepLever");
            Assert.Less(Vector3.Distance(lever.transform.position, Centre(17, 16)), 6f, "the lever is within sight of the gate approach");
            Click(lever.transform.position);
            yield return WaitUntil(() => session.Timeline.HasOccurred(LeverKey), 40f, "the party walked to the lever and pulled it");
            Assert.IsTrue(Named<PortcullisGate>("Portcullis").IsOpen);
        }

        [UnityTest]
        public IEnumerator EachThrallPatrolSpawnsItsThrallsInTheDeepSceneOnReachableCells([ValueSource(nameof(Patrols))] string triggerName)
        {
            yield return BootAt(DeepPath, "from-middle");
            var session = shell.Services.Session;
            var trigger = Named<EncounterTrigger>(triggerName);
            Hero().Motor.Warp(trigger.transform.position);
            Physics.SyncTransforms();
            yield return WaitUntil(() => shell.CombatSession.IsActive, 5f, $"{triggerName} fired");
            yield return new WaitForSeconds(0.4f);

            for (var n = 1; n <= 2; n++)
            {
                Assert.IsTrue(shell.Services.Spawner.TryGetCharacterId($"{trigger.FiredKey}-{n}", out var id, out _, out _));
                var thrall = GateSessionServices.FindAgent(id);
                Assert.IsNotNull(thrall, $"thrall {n} has a live agent");
                Assert.AreEqual(DeepPath, thrall.gameObject.scene.path);
                Assert.IsTrue(CanPath(thrall.transform.position, Hero().transform.position), $"thrall {n} at {thrall.transform.position} can reach the party");
                Assert.IsTrue(thrall.GetComponentsInChildren<SpriteRenderer>(true).Any(r => r.enabled && r.sprite != null), "visible");
            }
        }

        [UnityTest]
        public IEnumerator TheArenaIsSealedUntilTheLeverIsClickedThenTheBossFightsFromItsAnchor()
        {
            yield return BootAt(DeepPath, "from-middle");
            var session = shell.Services.Session;
            ClearPatrols();

            var arena = Named<EncounterTrigger>("NecromancerArena");
            var start = Hero().transform.position;
            Assert.IsFalse(CanPath(start, arena.transform.position), "the sealed arena cannot be reached before the lever");

            // Even standing inside the arena (warped in), the boss will not fire until the gate has been opened.
            Hero().Motor.Warp(arena.transform.position);
            Physics.SyncTransforms();
            yield return new WaitForSeconds(0.6f);
            yield return new WaitForFixedUpdate();
            Assert.IsFalse(shell.CombatSession.IsActive, "the arena trigger waits for the lever (requiredKey)");
            Hero().Motor.Warp(start);
            Physics.SyncTransforms();
            yield return new WaitForSeconds(0.2f);

            var lever = Named<LeverInteractionAdapter>("DeepLever");
            Click(lever.transform.position);
            yield return WaitUntil(() => session.Timeline.HasOccurred(LeverKey), 60f, "the party walked to the lever and pulled it");

            Assert.IsTrue(Named<PortcullisGate>("Portcullis").IsOpen, "the gate opens");
            Assert.IsTrue(CanPath(Hero().transform.position, arena.transform.position), "and the arena becomes reachable by a real path query");

            Hero().Motor.Warp(arena.transform.position);
            Physics.SyncTransforms();
            yield return WaitUntil(() => shell.CombatSession.IsActive, 6f, "the boss encounter fired once the gate was open");
            yield return new WaitForSeconds(0.4f);

            var anchor = Centre(17, 28);
            for (var n = 1; n <= 3; n++)
            {
                Assert.IsTrue(shell.Services.Spawner.TryGetCharacterId($"{arena.FiredKey}-{n}", out var id, out var npcId, out _), $"boss-wave member {n}");
                var enemy = GateSessionServices.FindAgent(id);
                Assert.IsNotNull(enemy);
                Assert.AreEqual(DeepPath, enemy.gameObject.scene.path);
                Assert.Less(Vector3.Distance(enemy.transform.position, anchor), 4.5f, $"member {n} ({npcId}) spawns around the ritual circle, not on the party");
                Assert.IsTrue(CanPath(enemy.transform.position, Hero().transform.position), $"member {n} can reach the party");
            }

            Assert.IsTrue(shell.Services.Spawner.TryGetCharacterId($"{arena.FiredKey}-1", out _, out var first, out _));
            Assert.AreEqual("npc-necromancer", first.Value, "the necromancer is the first spawn");
            var hud = UnityEngine.Object.FindFirstObjectByType<HudRoot>().GetComponent<UIDocument>().rootVisualElement;
            var labels = hud.Query<Label>().ToList().Select(l => l.text).ToList();
            CollectionAssert.Contains(labels, "Necromancer");
            CollectionAssert.Contains(labels, "Skeleton Thrall 1");
        }

        [UnityTest]
        public IEnumerator TheGateReDerivesItsStateAfterASameSceneLoadOfAnOlderSaveAndAfterTravellingAwayAndBack()
        {
            yield return BootAt(DeepPath, "from-middle");
            ClearPatrols();
            var gate = Named<PortcullisGate>("Portcullis");
            var lever = Named<LeverInteractionAdapter>("DeepLever");
            var grid = WalkabilityGridProvider.FindInScene(SceneManager.GetActiveScene()).Grid;
            var doorway = new GridCoordinate(17, 18);

            Assert.IsTrue(shell.SaveNow(), "an older save, from before the lever");
            var heroId = shell.Services.Session.Party.Members[0];
            InteractionExecutionResultHolder.Reset();
            lever.BeginExecute(heroId, r => InteractionExecutionResultHolder.Last = r);
            Assert.IsTrue(gate.IsOpen);
            Assert.IsTrue(grid.IsWalkable(doorway));

            // A same-scene load keeps the scene (and the runtime grid) but brings a session WITHOUT the lever key.
            var before = shell.Services.Session;
            shell.LoadNow();
            yield return WaitUntil(() => shell.Services.Session != before && shell.IsReady && !shell.IsLoading, 20f, "the older save loaded");
            yield return new WaitForSeconds(0.3f);
            Assert.IsFalse(Named<PortcullisGate>("Portcullis").IsOpen, "an older save closes the gate again");
            Assert.IsFalse(grid.IsWalkable(doorway));
            Assert.IsTrue(Named<LeverInteractionAdapter>("DeepLever").IsAvailable(shell.Services.Session.Party.Members[0]), "and the lever can be pulled again");

            // Pull it for real, travel to the Middle level and back: the persisted key must reopen the gate.
            ClearPatrols();
            Named<LeverInteractionAdapter>("DeepLever").BeginExecute(shell.Services.Session.Party.Members[0], r => { });
            Assert.IsTrue(Named<PortcullisGate>("Portcullis").IsOpen);

            var arch = GameObject.Find("TravelToMiddle");
            Hero().Motor.Warp(arch.transform.position + new Vector3(0f, 0f, 2f));
            Physics.SyncTransforms();
            Click(arch.transform.position);
            yield return WaitUntil(() => SceneManager.GetActiveScene().path == MiddlePath && shell.IsReady, 40f, "travelled up to Cavern_Middle");

            var middleBarricade = Named<EncounterTrigger>("GoblinBarricade");
            shell.Services.Session.Timeline.Record(middleBarricade.FiredKey, TimelineValue.Flag);
            middleBarricade.RefreshFiredState();
            var toDeep = GameObject.Find("TravelToDeep");
            Hero().Motor.Warp(toDeep.transform.position + new Vector3(0f, 0f, -2f));
            Physics.SyncTransforms();
            Click(toDeep.transform.position);
            yield return WaitUntil(() => SceneManager.GetActiveScene().path == DeepPath && shell.IsReady, 40f, "travelled back down to Cavern_Deep");
            yield return new WaitForSeconds(0.5f);

            var reopened = Named<PortcullisGate>("Portcullis");
            var freshGrid = WalkabilityGridProvider.FindInScene(SceneManager.GetActiveScene()).Grid;
            Assert.IsTrue(reopened.IsOpen, "the gate is open again after coming back (not re-closed by a late Start)");
            Assert.IsTrue(freshGrid.IsWalkable(doorway));
        }

        [UnityTest]
        public IEnumerator WinningTheBossFightCompletesTheDeepDarkQuestAndPaysTheGoldReward()
        {
            yield return BootAt(DeepPath, "from-middle");
            var session = shell.Services.Session;
            var questId = new DefinitionId("quest-the-deep-dark");
            Assert.IsTrue(session.Quests.TryGetQuest(questId, out var quest), "started on arrival");

            // The patrols and the lever are covered above; credit them so this test is about the boss.
            ClearPatrols();
            session.Timeline.Record(BarrowsEnd_ThrallKey, TimelineValue.Flag);
            session.Timeline.Record(BarrowsEnd_ThrallKey, TimelineValue.Flag);
            session.Timeline.Record(LeverKey, TimelineValue.Flag);
            Named<PortcullisGate>("Portcullis").Refresh(session);
            shell.RefreshAllActiveQuests();
            Assert.AreEqual(CRPG.Quests.QuestStatus.Active, quest.Status, "the boss is still alive");

            var arena = Named<EncounterTrigger>("NecromancerArena");
            var hero = Hero();
            var heroId = session.Party.Members[0];
            hero.Motor.Warp(arena.transform.position);
            Physics.SyncTransforms();
            yield return WaitUntil(() => shell.CombatSession.IsActive, 6f, "the boss fight started");
            yield return new WaitForSeconds(0.4f);

            var enemies = Enumerable.Range(1, 3)
                .Select(n => (ok: shell.Services.Spawner.TryGetCharacterId($"{arena.FiredKey}-{n}", out var id, out _, out _), id))
                .Where(t => t.ok).Select(t => t.id).ToList();
            Assert.AreEqual(3, enemies.Count);
            foreach (var e in enemies)
            {
                session.Characters.TryGetCharacter(e, out var c);
                c.TryGetResource(HitPoints, out var hp);
                hp.Current = 1;
            }

            var goldBefore = session.Inventory.AllItems.Where(i => i.DefinitionId.Value == "item-gold").Sum(i => i.Quantity);
            var hud = UnityEngine.Object.FindFirstObjectByType<HudRoot>().GetComponent<UIDocument>().rootVisualElement;
            for (var guard = 0; guard < 100 && shell.CombatSession.IsActive; guard++)
            {
                if (shell.CombatSession.CurrentTurnId == heroId)
                {
                    var target = enemies.Select(GateSessionServices.FindAgent).Where(a => a != null)
                        .OrderBy(a => Vector3.Distance(a.transform.position, hero.transform.position)).FirstOrDefault();
                    if (target == null) break;

                    var d = Vector3.Distance(hero.transform.position, target.transform.position);
                    if (d > 1.3f)
                    {
                        Click(Vector3.MoveTowards(hero.transform.position, target.transform.position, d - 1.0f));
                        yield return new WaitForSeconds(2.5f);
                    }

                    var strike = hud.Query<Button>().ToList().FirstOrDefault(b => b.text == "Strike");
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

            Assert.IsFalse(shell.CombatSession.IsActive, "the boss wave was defeated");
            Assert.IsTrue(session.Timeline.HasOccurred("necromancer-defeated"));
            yield return WaitUntil(() => quest.Status == CRPG.Quests.QuestStatus.Completed, 5f, "the Deep Dark quest completed (main-quest completion)");
            Assert.AreEqual("Quest complete: The Deep Dark", shell.LastMessage);
            var goldAfter = session.Inventory.AllItems.Where(i => i.DefinitionId.Value == "item-gold").Sum(i => i.Quantity);
            Assert.AreEqual(goldBefore + 200, goldAfter, "the quest's gold reward was granted once");
        }

        private const string BarrowsEnd_ThrallKey = "act3-thrall-cleared";

        [UnityTest]
        public IEnumerator CompletingTheDeepDarkQuestPlaysTheEpilogueConversationThroughToItsEnd()
        {
            yield return BootAt(DeepPath, "from-middle");
            var session = shell.Services.Session;
            Assert.IsTrue(session.Quests.TryGetQuest(new DefinitionId("quest-the-deep-dark"), out var quest));
            ClearPatrols();
            session.Timeline.Record(BarrowsEnd_ThrallKey, TimelineValue.Flag);
            session.Timeline.Record(BarrowsEnd_ThrallKey, TimelineValue.Flag);
            session.Timeline.Record(LeverKey, TimelineValue.Flag);

            session.Timeline.Record("necromancer-defeated", TimelineValue.Flag); // the boss fight itself is covered above
            shell.RefreshAllActiveQuests();
            Assert.AreEqual(CRPG.Quests.QuestStatus.Completed, quest.Status);

            // The ending conversation opens on its own ...
            yield return WaitUntil(() => UnityEngine.Object.FindFirstObjectByType<CRPG.UI.DialogueView>() != null, 5f, "the epilogue conversation opened");
            string Text() => UnityEngine.Object.FindFirstObjectByType<CRPG.UI.DialogueView>()?.GetComponent<UIDocument>().rootVisualElement.Query<Label>().ToList().Select(l => l.text).FirstOrDefault(t => t != null && t.Contains("necromancer")) ;
            yield return WaitUntil(() => Text() != null, 3f, "the first epilogue line is on screen");

            // ... and plays through to its end, releasing the modal capture (a leaked modal would block every later click).
            for (var guard = 0; guard < 12 && UnityEngine.Object.FindFirstObjectByType<CRPG.UI.DialogueView>() != null; guard++)
            {
                var root = UnityEngine.Object.FindFirstObjectByType<CRPG.UI.DialogueView>().GetComponent<UIDocument>().rootVisualElement;
                var next = root.Query<Button>().ToList().FirstOrDefault(b => b.text == "Continue");
                if (next != null) UiClick.Pointer(next);
                yield return new WaitForSeconds(0.2f);
            }

            yield return new WaitForSeconds(0.3f);
            Assert.IsNull(UnityEngine.Object.FindFirstObjectByType<CRPG.UI.DialogueView>(), "the epilogue ended and its view closed");
            Assert.IsFalse(new UIToolkitPointerCapture().IsPointerOverUI(new Vector2(2f, 2f)), "no modal capture is left behind");
        }

        private static class InteractionExecutionResultHolder
        {
            public static CRPG.Gameplay.Interactions.InteractionExecutionResult? Last;
            public static void Reset() => Last = null;
        }
    }
}
