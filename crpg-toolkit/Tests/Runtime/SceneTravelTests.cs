using System.Collections;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using CRPG.Actors;
using CRPG.Core;
using CRPG.Gameplay;
using CRPG.Gameplay.Scenes;
using CRPG.Party;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace CRPG.Tests.PlayMode
{
    /// <summary>
    /// Task 3.6: SceneTravelCoordinator against two REAL, content-free scenes (SceneTravelTestFixtureAuthor)
    /// loaded via the actual SceneManager -- proves the scene-loading/placement-validation/warp/rollback
    /// machinery itself, not content preparation (LocationStatePersistenceTests, EditMode, covers that
    /// cheaply). Party agents are always built BEFORE SceneTravelTestA is loaded/made active, and never moved
    /// afterward -- they land in the test's own base scene by construction, standing in for the persistent Core
    /// scene, so unloading SceneTravelTestA/B (which every travel eventually does) can never destroy them. This
    /// was a real bug in an earlier draft of these tests, not in production code: building the party AFTER
    /// SceneManager.SetActiveScene(A) put its GameObjects in A too, and A being unloaded on a later travel
    /// destroyed the party right along with it.
    /// </summary>
    public class SceneTravelTests
    {
        private const string SceneAPath = "Assets/Scenes/Tests/SceneTravelTestA.unity";
        private const string SceneBPath = "Assets/Scenes/Tests/SceneTravelTestB.unity";

        private readonly List<GameObject> spawned = new();

        [TearDown]
        public void TearDown()
        {
            foreach (var go in spawned) if (go != null) Object.DestroyImmediate(go);
            spawned.Clear();

            for (var i = SceneManager.sceneCount - 1; i >= 0; i--)
            {
                var scene = SceneManager.GetSceneAt(i);
                if (scene.path == SceneAPath || scene.path == SceneBPath) SceneManager.UnloadSceneAsync(scene);
            }
        }

        private GameObject Track(GameObject go)
        {
            spawned.Add(go);
            return go;
        }

        private (GameSession session, WorldSpawnService worldSpawnService, PartyState party, RuntimeEntityId memberId, CharacterAgent agent)
            BuildOneMemberParty(bool withNavMeshAgent = true)
        {
            var session = new GameSession(new RulesDatabase(), new DiceService(new SeededRandomSource(1)));
            var worldSpawnService = new WorldSpawnService(session);
            var party = new PartyState();

            var characterId = RuntimeEntityId.NewId();
            session.Characters.Register(new CharacterState(characterId));
            party.AddMember(characterId);

            var go = Track(new GameObject("PartyMember"));
            if (withNavMeshAgent) go.AddComponent<NavMeshAgent>();
            var agent = go.AddComponent<CharacterAgent>();
            agent.Bind(characterId, session);

            return (session, worldSpawnService, party, characterId, agent);
        }

        private static IEnumerator LoadAndActivate(string scenePath)
        {
            yield return SceneManager.LoadSceneAsync(scenePath, LoadSceneMode.Additive);
            SceneManager.SetActiveScene(SceneManager.GetSceneByPath(scenePath));
        }

        private sealed class FixedPointFormationSolver : IFormationSolver
        {
            private readonly Vector3 point;
            public FixedPointFormationSolver(Vector3 point) => this.point = point;
            public IReadOnlyList<Vector3> Solve(Vector3 destination, Vector3 facing, int memberCount, float spacing)
            {
                var points = new Vector3[memberCount];
                for (var i = 0; i < memberCount; i++) points[i] = point;
                return points;
            }
        }

        [UnityTest]
        public IEnumerator HappyPathTravelsFromAToBAndBackReleasingTheOutgoingLocationEachTime()
        {
            var (session, worldSpawnService, party, memberId, agent) = BuildOneMemberParty();
            yield return LoadAndActivate(SceneAPath);

            var gate = new WorldTransitionGate();
            var coordinator = new SceneTravelCoordinator(session, worldSpawnService, party, gate);

            SceneTravelResult resultToB = null;
            yield return coordinator.TravelCoroutine(SceneBPath, "dest-b", r => resultToB = r);

            Assert.IsTrue(resultToB.Success, resultToB.Message);
            Assert.IsFalse(SceneManager.GetSceneByPath(SceneAPath).isLoaded); // A was unloaded
            Assert.IsTrue(SceneManager.GetSceneByPath(SceneBPath).isLoaded);
            Assert.IsFalse(gate.IsBusy);
            Assert.IsTrue(agent != null && agent.gameObject != null); // party agent never destroyed
            Assert.Less(Vector3.Distance(agent.transform.position, new Vector3(2000f, 0f, 0f)), 5f);

            SceneTravelResult resultToA = null;
            yield return coordinator.TravelCoroutine(SceneAPath, "dest-a", r => resultToA = r);

            Assert.IsTrue(resultToA.Success, resultToA.Message);
            Assert.IsFalse(SceneManager.GetSceneByPath(SceneBPath).isLoaded); // B was unloaded on the way back
            Assert.IsTrue(SceneManager.GetSceneByPath(SceneAPath).isLoaded);
            Assert.Less(Vector3.Distance(agent.transform.position, Vector3.zero), 5f);
        }

        [UnityTest]
        public IEnumerator UnknownDestinationIdFailsWithoutUnloadingTheOldScene()
        {
            var (session, worldSpawnService, party, _, _) = BuildOneMemberParty();
            yield return LoadAndActivate(SceneAPath);

            var gate = new WorldTransitionGate();
            var coordinator = new SceneTravelCoordinator(session, worldSpawnService, party, gate);

            SceneTravelResult result = null;
            yield return coordinator.TravelCoroutine(SceneBPath, "no-such-destination", r => result = r);

            Assert.AreEqual(SceneTravelOutcome.DestinationNotFound, result.Outcome);
            Assert.IsTrue(SceneManager.GetSceneByPath(SceneAPath).isLoaded); // old scene untouched
            Assert.IsFalse(SceneManager.GetSceneByPath(SceneBPath).isLoaded); // candidate scene cleaned up
            Assert.IsFalse(gate.IsBusy);
        }

        [UnityTest]
        public IEnumerator NonexistentScenePathFailsCleanly()
        {
            var (session, worldSpawnService, party, _, _) = BuildOneMemberParty();
            yield return LoadAndActivate(SceneAPath);

            var gate = new WorldTransitionGate();
            var coordinator = new SceneTravelCoordinator(session, worldSpawnService, party, gate);

            // Unity itself logs an error when LoadSceneAsync can't resolve the path -- this test deliberately
            // triggers that, so it must be declared expected or the Test Framework fails the test outright for
            // an "unhandled" error log.
            LogAssert.Expect(LogType.Error, new Regex("DoesNotExist"));

            SceneTravelResult result = null;
            yield return coordinator.TravelCoroutine("Assets/Scenes/Tests/DoesNotExist.unity", "dest-x", r => result = r);

            Assert.AreEqual(SceneTravelOutcome.SceneLoadFailed, result.Outcome);
            Assert.IsTrue(SceneManager.GetSceneByPath(SceneAPath).isLoaded);
            Assert.IsFalse(gate.IsBusy);
        }

        [UnityTest]
        public IEnumerator AnUnreachableDestinationFailsPlacementValidationBeforeMovingAnyone()
        {
            var (session, worldSpawnService, party, _, agent) = BuildOneMemberParty();
            var startPosition = agent.transform.position;
            yield return LoadAndActivate(SceneAPath);

            var gate = new WorldTransitionGate();
            // Far off B's baked plane (B's ground is a 40x40 plane centered on (2000,0,0)) -- guaranteed off-mesh.
            var offMeshSolver = new FixedPointFormationSolver(new Vector3(2000f, 500f, 0f));
            var coordinator = new SceneTravelCoordinator(session, worldSpawnService, party, gate, formationSolver: offMeshSolver);

            SceneTravelResult result = null;
            yield return coordinator.TravelCoroutine(SceneBPath, "dest-b", r => result = r);

            Assert.AreEqual(SceneTravelOutcome.PlacementValidationFailed, result.Outcome);
            Assert.IsTrue(SceneManager.GetSceneByPath(SceneAPath).isLoaded);
            Assert.IsFalse(SceneManager.GetSceneByPath(SceneBPath).isLoaded);
            // NavMeshAgent's own OnEnable auto-snap-to-surface can nudge Y by a hair even with no Warp
            // involved -- a tolerance, not exact equality, is the correct check for "never moved."
            Assert.Less(Vector3.Distance(startPosition, agent.transform.position), 0.5f);
            Assert.IsFalse(gate.IsBusy);
        }

        [UnityTest]
        public IEnumerator APartyMemberMissingAMoverFailsRepositioningAndRollsBackAnyoneAlreadyWarped()
        {
            var session = new GameSession(new RulesDatabase(), new DiceService(new SeededRandomSource(1)));
            var worldSpawnService = new WorldSpawnService(session);
            var party = new PartyState();

            var okId = RuntimeEntityId.NewId();
            session.Characters.Register(new CharacterState(okId));
            party.AddMember(okId);
            var okGo = Track(new GameObject("Member-OK"));
            okGo.AddComponent<NavMeshAgent>();
            var okAgent = okGo.AddComponent<CharacterAgent>();
            okAgent.Bind(okId, session);
            var okStart = okAgent.transform.position;

            var brokenId = RuntimeEntityId.NewId();
            session.Characters.Register(new CharacterState(brokenId));
            party.AddMember(brokenId); // no NavMeshAgent -- Warp must fail for this one
            var brokenGo = Track(new GameObject("Member-Broken"));
            var brokenAgent = brokenGo.AddComponent<CharacterAgent>();
            brokenAgent.Bind(brokenId, session);

            yield return LoadAndActivate(SceneAPath);

            var gate = new WorldTransitionGate();
            // Single shared point -- both members validate fine (on B's real navmesh), but only one can Warp.
            var solver = new FixedPointFormationSolver(new Vector3(2000f, 0f, 0f));
            var coordinator = new SceneTravelCoordinator(session, worldSpawnService, party, gate, formationSolver: solver);

            SceneTravelResult result = null;
            yield return coordinator.TravelCoroutine(SceneBPath, "dest-b", r => result = r);

            Assert.AreEqual(SceneTravelOutcome.PartyRepositionFailed, result.Outcome);
            Assert.IsTrue(SceneManager.GetSceneByPath(SceneAPath).isLoaded);
            Assert.IsFalse(SceneManager.GetSceneByPath(SceneBPath).isLoaded);
            // Same NavMeshAgent auto-snap tolerance as above.
            Assert.Less(Vector3.Distance(okStart, okAgent.transform.position), 0.5f);
            Assert.IsFalse(gate.IsBusy);
        }

        [UnityTest]
        public IEnumerator TravelRefusesToStartWhileTheSharedGateIsAlreadyBusy()
        {
            var (session, worldSpawnService, party, _, _) = BuildOneMemberParty();
            yield return LoadAndActivate(SceneAPath);

            var gate = new WorldTransitionGate();
            gate.BeginBusy("session-load"); // simulates a load already in progress
            var coordinator = new SceneTravelCoordinator(session, worldSpawnService, party, gate);

            SceneTravelResult result = null;
            yield return coordinator.TravelCoroutine(SceneBPath, "dest-b", r => result = r);

            Assert.AreEqual(SceneTravelOutcome.BlockedByLoad, result.Outcome);
            Assert.IsTrue(SceneManager.GetSceneByPath(SceneAPath).isLoaded);
            Assert.IsFalse(SceneManager.GetSceneByPath(SceneBPath).isLoaded); // never even attempted
        }
    }
}
