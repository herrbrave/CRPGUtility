using System;
using System.Collections;
using System.IO;
using BarrowsEnd.Gate;
using BarrowsEnd.Shell;
using CRPG.Core;
using CRPG.Gameplay;
using CRPG.Gameplay.Interactions;
using CRPG.Navigation.Grid;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace BarrowsEnd.Tests
{
    /// <summary>Task 6.3: the portcullis and lever -- the first terrain that changes from a game event. The review's B1/B2: a
    /// gate must be closed by default, and re-derive open OR closed from the persisted key every time a session is wired
    /// (a same-scene load of an older save keeps the runtime grid, so open must be able to go back to closed).</summary>
    public class PortcullisGateTests
    {
        private const string CorePath = "Assets/Scenes/Core.unity";
        private const string UpperPath = "Assets/Scenes/Locations/Cavern_Upper.unity";
        private const string Key = "portcullis-test-lever";

        private ShellBootstrap shell;
        private string savePath;
        private GameObject root;

        [UnitySetUp]
        public IEnumerator Boot()
        {
            savePath = Path.Combine(Path.GetTempPath(), $"barrowsend-gate-{Guid.NewGuid():N}.json");
            ShellBootstrap.StartModeOverride = ShellStartMode.AuthoredParty;
            ShellBootstrap.StartingSceneOverride = (UpperPath, "from-town");
            yield return SceneManager.LoadSceneAsync(CorePath, LoadSceneMode.Single);
            shell = UnityEngine.Object.FindFirstObjectByType<ShellBootstrap>();
            shell.SavePath = savePath;
            float waited = 0f;
            while (!shell.IsReady && waited < 20f) { waited += Mathf.Min(Time.deltaTime, 0.1f); yield return null; }
            Assert.IsTrue(shell.IsReady);
        }

        [TearDown]
        public void Cleanup()
        {
            ShellBootstrap.StartModeOverride = null;
            ShellBootstrap.StartingSceneOverride = null;
            if (root != null) UnityEngine.Object.Destroy(root);
            if (File.Exists(savePath)) File.Delete(savePath);
        }

        private PortcullisGate BuildGate(params Vector2Int[] cells)
        {
            root = new GameObject("TestGate");
            foreach (var cell in cells)
            {
                var child = new GameObject($"Cell_{cell.x}_{cell.y}");
                child.transform.SetParent(root.transform, false);
                child.transform.position = new Vector3(cell.x + 0.5f, 0f, cell.y + 0.5f);
            }

            var gate = root.AddComponent<PortcullisGate>();
            typeof(PortcullisGate).GetField("openKey", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance).SetValue(gate, Key);
            return gate;
        }

        private WalkabilityGrid Grid() => WalkabilityGridProvider.FindInScene(SceneManager.GetActiveScene()).Grid;
        private static GridCoordinate C(int x, int y) => new(x, y);

        [UnityTest]
        public IEnumerator AGateIsClosedByDefaultOpensFromTheKeyAndClosesAgainFromAnOlderSession()
        {
            var session = shell.Services.Session;
            var gate = BuildGate(new Vector2Int(10, 6), new Vector2Int(11, 6), new Vector2Int(12, 6));
            yield return null;

            var grid = Grid();
            Assert.IsFalse(gate.IsOpen);
            foreach (var x in new[] { 10, 11, 12 }) Assert.IsFalse(grid.IsWalkable(C(x, 6)), $"cell {x},6 is blocked while the gate is closed");
            Assert.IsTrue(grid.IsWalkable(C(9, 6)), "its neighbours are untouched");

            gate.Refresh(session);
            Assert.IsFalse(gate.IsOpen, "a session without the lever key leaves it closed");

            session.Timeline.Record(Key, TimelineValue.Flag);
            gate.Refresh(session);
            Assert.IsTrue(gate.IsOpen);
            foreach (var x in new[] { 10, 11, 12 }) Assert.IsTrue(grid.IsWalkable(C(x, 6)), $"cell {x},6 opens");
            foreach (Transform bars in root.transform) Assert.IsFalse(bars.gameObject.activeSelf, "the bars disappear");

            // A same-scene load of an OLDER save brings a session whose timeline lacks the key; the runtime grid survives that load,
            // so the gate has to close again, not stay open.
            var older = new GameSession(new RulesDatabase(), new DiceService(new SeededRandomSource(2)));
            try
            {
                gate.Refresh(older);
                Assert.IsFalse(gate.IsOpen, "an older save re-closes the gate");
                foreach (var x in new[] { 10, 11, 12 }) Assert.IsFalse(grid.IsWalkable(C(x, 6)));
                foreach (Transform bars in root.transform) Assert.IsTrue(bars.gameObject.activeSelf);
            }
            finally { older.Dispose(); }
        }

        [UnityTest]
        public IEnumerator AClosedGateMakesTheFarCellUnreachableOnARealPathQueryAndTheLeverOpensIt()
        {
            var session = shell.Services.Session;
            var heroId = session.Party.Members[0];
            var hero = GateSessionServices.FindAgent(heroId);
            var from = hero.transform.position;

            // A gate that seals the goal cell itself: a path query to it fails closed, succeeds open.
            var goal = new Vector2Int((int)from.x + 3, (int)from.z + 2);
            var gate = BuildGate(goal);
            var pathfinding = shell.Services.Spawner.GridNavigation;
            var goalWorld = new Vector3(goal.x + 0.5f, 0f, goal.y + 0.5f);
            yield return null;

            Assert.IsFalse(Grid().IsWalkable(C(goal.x, goal.y)));

            // The lever
            var leverGo = new GameObject("TestLever");
            leverGo.transform.SetParent(root.transform, false);
            leverGo.SetActive(false);
            leverGo.AddComponent<BoxCollider>();
            var lever = leverGo.AddComponent<LeverInteractionAdapter>();
            typeof(LeverInteractionAdapter).GetField("leverKey", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance).SetValue(lever, Key);
            var anchor = new GameObject("Anchor").transform;
            anchor.SetParent(leverGo.transform, false);
            leverGo.GetComponent<WorldInteractable>().Configure(anchor, 1.5f);
            leverGo.SetActive(true);
            lever.Session = session;
            var questRefreshes = 0;
            string message = null;
            lever.RequestQuestRefresh = () => questRefreshes++;
            lever.Notify = m => message = m;
            lever.OnPulled = () => gate.Refresh(session);

            Assert.IsTrue(lever.IsAvailable(heroId));
            InteractionExecutionResult? result = null;
            lever.BeginExecute(heroId, r => result = r);

            Assert.AreEqual(InteractionExecutionResult.Succeeded, result);
            Assert.IsTrue(session.Timeline.HasOccurred(Key), "pulling records the persisted key");
            Assert.IsTrue(gate.IsOpen, "and the gate re-derives itself from it");
            Assert.IsTrue(Grid().IsWalkable(C(goal.x, goal.y)));
            Assert.IsTrue(pathfinding.TryCalculatePath(from, goalWorld, out _), "a real path query to the formerly sealed cell now succeeds");
            Assert.AreEqual(1, questRefreshes, "quests are re-checked after a pull");
            StringAssert.Contains("lever", message);

            Assert.IsFalse(lever.IsAvailable(heroId), "a pulled lever is not offered again");
            InteractionExecutionResult? second = null;
            lever.BeginExecute(heroId, r => second = r);
            Assert.AreEqual(InteractionExecutionResult.Failed, second);
            Assert.AreEqual(1, questRefreshes, "and a repeat pull does nothing");
        }
    }
}
