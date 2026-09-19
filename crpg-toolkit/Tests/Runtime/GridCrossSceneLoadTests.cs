using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using CRPG.Actors;
using CRPG.Core;
using CRPG.Gameplay;
using CRPG.Gameplay.Scenes;
using CRPG.Navigation;
using CRPG.Navigation.Grid;
using CRPG.Saving;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace CRPG.Tests.PlayMode
{
    /// <summary>
    /// 2D campaign Task 1.5: SessionLoadCoordinator's cross-scene load over the real grid scenes. The loaded
    /// location's grid must be activated before the party is restored onto it (and each restored grid motor handed the
    /// pathfinder), and a failed load must leave the previous location's grid in place.
    /// </summary>
    public class GridCrossSceneLoadTests
    {
        private const string GridSceneAPath = "Assets/Scenes/Tests/GridTravelTestA.unity";
        private const string GridSceneBPath = "Assets/Scenes/Tests/GridTravelTestB.unity";
        private const string NavMeshSceneAPath = "Assets/Scenes/Tests/SceneTravelTestA.unity"; // carries no WalkabilityGridProvider

        private static readonly Vector3 AOrigin = new(0f, 0f, 5000f);

        private string savePath;
        private readonly List<GameObject> spawned = new();

        [SetUp]
        public void SetUp() => savePath = Path.Combine(Path.GetTempPath(), "crpg-grid-load-" + Guid.NewGuid().ToString("N") + ".json");

        [TearDown]
        public void TearDown()
        {
            foreach (var go in spawned) if (go != null) UnityEngine.Object.DestroyImmediate(go);
            spawned.Clear();
            foreach (var stray in UnityEngine.Object.FindObjectsByType<CharacterAgent>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                UnityEngine.Object.DestroyImmediate(stray.gameObject);

            for (var i = SceneManager.sceneCount - 1; i >= 0; i--)
            {
                var scene = SceneManager.GetSceneAt(i);
                if (scene.path == GridSceneAPath || scene.path == GridSceneBPath || scene.path == NavMeshSceneAPath) SceneManager.UnloadSceneAsync(scene);
            }

            if (File.Exists(savePath)) File.Delete(savePath);
        }

        private static WalkabilityGrid GridOf(string scenePath) =>
            WalkabilityGridProvider.FindInScene(SceneManager.GetSceneByPath(scenePath)).Grid;

        /// <summary>Loads and activates a scene the way a composition root would for an initial location.</summary>
        private static IEnumerator EnterSceneByHand(string scenePath, GridPathfindingService pathfinding)
        {
            yield return SceneManager.LoadSceneAsync(scenePath, LoadSceneMode.Additive);
            var scene = SceneManager.GetSceneByPath(scenePath);
            SceneManager.SetActiveScene(scene);
            var provider = WalkabilityGridProvider.FindInScene(scene);
            if (provider != null) pathfinding.SetActiveGrid(provider.Grid);
        }

        private (GameSessionHost host, RulesDatabase rules, GameSession session, WorldSpawnService spawner, RuntimeEntityId memberId, GameObject template)
            BuildSession(GridPathfindingService pathfinding, Vector3 memberAt, bool withGridMotor)
        {
            var hostGo = new GameObject("Host");
            spawned.Add(hostGo);
            var host = hostGo.AddComponent<GameSessionHost>();
            var rules = new RulesDatabase();
            var session = host.StartSession(rules, new DiceService(new SeededRandomSource(1)));
            var spawner = new WorldSpawnService(session) { GridNavigation = pathfinding };

            var memberId = RuntimeEntityId.NewId();
            session.Characters.Register(new CharacterState(memberId));
            session.Party.AddMember(memberId);
            var template = new GameObject("PartyPrefabTemplate");
            spawned.Add(template);
            if (withGridMotor) template.AddComponent<GridCharacterMotor>();
            spawner.BindRestoredView(memberId.ToString(), memberId, default, template, memberAt, Quaternion.identity, default, startInactive: false);
            return (host, rules, session, spawner, memberId, template);
        }

        [UnityTest]
        public IEnumerator ACrossSceneLoadActivatesTheLoadedLocationsGridAndRestoresGridMotorsOntoIt()
        {
            var coreScene = SceneManager.GetActiveScene();
            var pathfinding = new GridPathfindingService();
            var (host, rules, session, spawner, memberId, template) = BuildSession(pathfinding, AOrigin + new Vector3(4.5f, 0f, 4.5f), withGridMotor: true); // cell (4,4) of scene A

            yield return EnterSceneByHand(GridSceneAPath, pathfinding);
            var saveResult = new SaveRequestService(session, spawner, new SafePointGate()).RequestSave(savePath);
            Assert.AreEqual(SaveRequestOutcome.Success, saveResult.Outcome, saveResult.Message);

            // "Travel" to B by hand, then load the save that was captured in A.
            yield return EnterSceneByHand(GridSceneBPath, pathfinding);
            yield return SceneManager.UnloadSceneAsync(SceneManager.GetSceneByPath(GridSceneAPath));
            var gridBeforeLoad = pathfinding.ActiveGrid;
            Assert.AreSame(GridOf(GridSceneBPath), gridBeforeLoad);

            var coordinator = new SessionLoadCoordinator(gate: new WorldTransitionGate(), gridPathfinding: pathfinding);
            LoadResult result = null;
            yield return coordinator.LoadCoroutine(
                savePath, rules, new DiceService(new SeededRandomSource(2)), host, spawner, coreScene,
                new Dictionary<RuntimeEntityId, GameObject> { [memberId] = template }, r => result = r);
            yield return null;

            Assert.AreEqual(LoadOutcome.Success, result.Outcome, result.Message);
            var gridA = GridOf(GridSceneAPath);
            Assert.AreSame(gridA, pathfinding.ActiveGrid, "The loaded location's grid must now be the active one.");
            Assert.AreNotSame(gridBeforeLoad, pathfinding.ActiveGrid);

            var agent = result.WorldSpawnService.OwnedAgents.Single(a => a.EntityId == memberId);
            spawned.Add(agent.gameObject);
            var motor = agent.GetComponent<GridCharacterMotor>();
            Assert.AreSame(pathfinding, motor.Pathfinding, "A restored grid motor must be handed the pathfinder, or movement is dead after every load.");
            Assert.AreEqual(memberId, motor.OccupantId);
            Assert.IsTrue(gridA.IsOccupied(gridA.ToGrid(agent.transform.position), out var holder) && holder == memberId, "It holds its cell on the loaded grid.");
            Assert.AreNotEqual(MoveStatus.Invalid, motor.SetDestination(gridA.ToWorld(new GridCoordinate(8, 4))).Status, "...and can move.");
        }

        [UnityTest]
        public IEnumerator ACrossSceneLoadIntoASceneWithNoGridProviderFailsAndLeavesTheCurrentGridUntouched()
        {
            var coreScene = SceneManager.GetActiveScene();
            var pathfinding = new GridPathfindingService();
            var (host, rules, session, spawner, memberId, template) = BuildSession(pathfinding, Vector3.zero, withGridMotor: false);

            // Saved while a scene WITHOUT a grid provider is active...
            yield return SceneManager.LoadSceneAsync(NavMeshSceneAPath, LoadSceneMode.Additive);
            SceneManager.SetActiveScene(SceneManager.GetSceneByPath(NavMeshSceneAPath));
            var saveResult = new SaveRequestService(session, spawner, new SafePointGate()).RequestSave(savePath);
            Assert.AreEqual(SaveRequestOutcome.Success, saveResult.Outcome, saveResult.Message);

            // ...then loaded from a grid scene.
            yield return EnterSceneByHand(GridSceneBPath, pathfinding);
            yield return SceneManager.UnloadSceneAsync(SceneManager.GetSceneByPath(NavMeshSceneAPath));
            var gridBeforeLoad = pathfinding.ActiveGrid;

            var gate = new WorldTransitionGate();
            var coordinator = new SessionLoadCoordinator(gate: gate, gridPathfinding: pathfinding);
            LoadResult result = null;
            yield return coordinator.LoadCoroutine(
                savePath, rules, new DiceService(new SeededRandomSource(2)), host, spawner, coreScene,
                new Dictionary<RuntimeEntityId, GameObject> { [memberId] = template }, r => result = r);
            yield return null;

            Assert.AreEqual(LoadOutcome.SceneLoadFailed, result.Outcome);
            StringAssert.Contains("WalkabilityGridProvider", result.Message);
            Assert.AreSame(gridBeforeLoad, pathfinding.ActiveGrid, "A failed load must not disturb the current location's grid.");
            Assert.IsTrue(SceneManager.GetSceneByPath(GridSceneBPath).isLoaded, "The current location stays loaded.");
            Assert.IsFalse(gate.IsBusy);
        }
    }
}
