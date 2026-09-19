using System.Collections;
using System.Collections.Generic;
using System.Linq;
using CRPG.Actors;
using CRPG.Core;
using CRPG.Gameplay;
using CRPG.Gameplay.Scenes;
using CRPG.Navigation;
using CRPG.Navigation.Grid;
using CRPG.Party;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace CRPG.Tests.PlayMode
{
    /// <summary>
    /// 2D campaign Task 1.5: SceneTravelCoordinator over two REAL scenes that each carry a WalkabilityGridProvider
    /// (authored by SceneTravelTestFixtureAuthor.AuthorGridFixtures). Scene A is fully open; scene B has a solid wall
    /// down x = 8. The persistent Core-scene GridPathfindingService must be re-pointed at each scene's own grid on
    /// travel, with reachability and occupancy following it and nothing stale surviving the trip.
    /// </summary>
    public class GridSceneTravelTests
    {
        private const string SceneAPath = "Assets/Scenes/Tests/GridTravelTestA.unity";
        private const string SceneBPath = "Assets/Scenes/Tests/GridTravelTestB.unity";

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

        private sealed class FixedPointFormationSolver : IFormationSolver
        {
            private readonly Vector3 point;
            public FixedPointFormationSolver(Vector3 point) => this.point = point;

            public IReadOnlyList<Vector3> Solve(Vector3 destination, Vector3 facing, int memberCount, float spacing) =>
                Enumerable.Repeat(point, memberCount).ToArray();
        }

        private readonly struct Member
        {
            public Member(RuntimeEntityId id, CharacterAgent agent, GridCharacterMotor motor)
            {
                Id = id;
                Agent = agent;
                Motor = motor;
            }

            public RuntimeEntityId Id { get; }
            public CharacterAgent Agent { get; }
            public GridCharacterMotor Motor { get; }
        }

        private Member AddMember(GameSession session, PartyState party, GridPathfindingService pathfinding, Vector3 position, bool withMotor = true)
        {
            var id = RuntimeEntityId.NewId();
            session.Characters.Register(new CharacterState(id));
            party.AddMember(id);

            var go = new GameObject("PartyMember");
            spawned.Add(go);
            go.transform.position = position;
            GridCharacterMotor motor = null;
            if (withMotor)
            {
                motor = go.AddComponent<GridCharacterMotor>();
                motor.Pathfinding = pathfinding;
                motor.OccupantId = id;
                motor.Speed = 20f;
            }

            var agent = go.AddComponent<CharacterAgent>(); // after the motor, so Bind resolves it
            agent.Bind(id, session);
            return new Member(id, agent, motor);
        }

        private static readonly Vector3 AOrigin = new(0f, 0f, 5000f);
        private static Vector3 ACell(int x, int z) => AOrigin + new Vector3(x + 0.5f, 0f, z + 0.5f);

        private static IEnumerator LoadActivateAndBind(string scenePath, GridPathfindingService pathfinding)
        {
            yield return SceneManager.LoadSceneAsync(scenePath, LoadSceneMode.Additive);
            var scene = SceneManager.GetSceneByPath(scenePath);
            SceneManager.SetActiveScene(scene);

            // The composition root's job for the INITIAL location: point the pathfinder at its grid.
            pathfinding.SetActiveGrid(WalkabilityGridProvider.FindInScene(scene).Grid);
            yield return null; // let each mover claim the cell it stands in
        }

        private static WalkabilityGrid GridOf(string scenePath) =>
            WalkabilityGridProvider.FindInScene(SceneManager.GetSceneByPath(scenePath)).Grid;

        private static bool CanCross(GridPathfindingService pathfinding, WalkabilityGrid grid) =>
            pathfinding.TryCalculatePath(grid.ToWorld(new GridCoordinate(1, 2)), grid.ToWorld(new GridCoordinate(12, 2)), out _);

        [UnityTest]
        public IEnumerator ARoundTripSwapsTheActiveGridEachWayWithNoStaleReachabilityOrClaims()
        {
            var pathfinding = new GridPathfindingService();
            var session = new GameSession(new RulesDatabase(), new DiceService(new SeededRandomSource(1)));
            var spawnService = new WorldSpawnService(session) { GridNavigation = pathfinding };
            var party = new PartyState();
            var first = AddMember(session, party, pathfinding, ACell(1, 1));
            var second = AddMember(session, party, pathfinding, ACell(2, 1));

            yield return LoadActivateAndBind(SceneAPath, pathfinding);
            var firstGridA = pathfinding.ActiveGrid;
            Assert.IsTrue(CanCross(pathfinding, firstGridA), "Scene A is open: the left and right halves are connected.");
            Assert.IsTrue(firstGridA.IsOccupied(new GridCoordinate(1, 1), out var holderInA));
            Assert.AreEqual(first.Id, holderInA);

            var gate = new WorldTransitionGate();
            var coordinator = new SceneTravelCoordinator(session, spawnService, party, gate, gridPathfinding: pathfinding);

            // --- A -> B ---
            SceneTravelResult toB = null;
            yield return coordinator.TravelCoroutine(SceneBPath, "grid-dest-b", r => toB = r);
            Assert.IsTrue(toB.Success, toB.Message);
            yield return null;

            var gridB = GridOf(SceneBPath);
            Assert.AreSame(gridB, pathfinding.ActiveGrid, "The pathfinder must now be pointed at B's own grid.");
            Assert.IsFalse(CanCross(pathfinding, gridB), "B's solid wall splits it: nothing of A's open layout may leak in.");
            foreach (var member in new[] { first, second })
            {
                var cell = gridB.ToGrid(member.Agent.transform.position);
                Assert.IsTrue(gridB.IsWalkable(cell));
                Assert.IsTrue(gridB.IsOccupied(cell, out var holder) && holder == member.Id, "Each member holds the cell it landed in on B.");
            }

            Assert.IsFalse(firstGridA.IsOccupied(new GridCoordinate(1, 1), out _), "A's grid must be left clean of the party's claims.");
            Assert.IsFalse(firstGridA.IsOccupied(new GridCoordinate(2, 1), out _));

            // Real movement works on B's grid (and stays left of the wall).
            var target = gridB.ToWorld(new GridCoordinate(5, 5));
            Assert.AreNotEqual(MoveStatus.Invalid, first.Motor.SetDestination(target).Status);
            MoveStatus? terminal = null;
            var elapsed = 0f;
            while (elapsed < 10f && terminal == null)
            {
                yield return null;
                elapsed += Time.deltaTime;
                var status = first.Motor.Snapshot.Status;
                if (status is MoveStatus.Arrived or MoveStatus.Blocked) terminal = status;
            }

            Assert.AreEqual(MoveStatus.Arrived, terminal);

            // --- B -> A ---
            SceneTravelResult toA = null;
            yield return coordinator.TravelCoroutine(SceneAPath, "grid-dest-a", r => toA = r);
            Assert.IsTrue(toA.Success, toA.Message);
            yield return null;

            var secondGridA = GridOf(SceneAPath);
            Assert.AreSame(secondGridA, pathfinding.ActiveGrid);
            Assert.AreNotSame(firstGridA, secondGridA, "A was unloaded and reloaded: it is a brand new grid, nothing stale from the first visit.");
            Assert.IsTrue(CanCross(pathfinding, secondGridA), "Back in the open scene, the halves are connected again.");
            foreach (var member in new[] { first, second })
            {
                var cell = secondGridA.ToGrid(member.Agent.transform.position);
                Assert.IsTrue(secondGridA.IsOccupied(cell, out var holder) && holder == member.Id);
            }

            Assert.IsFalse(gridB.IsOccupied(gridB.ToGrid(target), out _), "B's grid must be left clean too.");
        }

        [UnityTest]
        public IEnumerator AFailedRepositionRestoresTheOldGridAndPutsTheWarpedMemberBackWithItsClaim()
        {
            var pathfinding = new GridPathfindingService();
            var session = new GameSession(new RulesDatabase(), new DiceService(new SeededRandomSource(1)));
            var spawnService = new WorldSpawnService(session) { GridNavigation = pathfinding };
            var party = new PartyState();
            var start = ACell(1, 1);
            var good = AddMember(session, party, pathfinding, start);
            AddMember(session, party, pathfinding, ACell(2, 1), withMotor: false); // no motor: its Warp must fail

            yield return LoadActivateAndBind(SceneAPath, pathfinding);
            var gridA = pathfinding.ActiveGrid;

            var gate = new WorldTransitionGate();
            var coordinator = new SceneTravelCoordinator(session, spawnService, party, gate, gridPathfinding: pathfinding);
            SceneTravelResult result = null;
            yield return coordinator.TravelCoroutine(SceneBPath, "grid-dest-b", r => result = r);
            yield return null;

            Assert.AreEqual(SceneTravelOutcome.PartyRepositionFailed, result.Outcome);
            Assert.AreSame(gridA, pathfinding.ActiveGrid, "A failed travel must put the previous location's grid back.");
            Assert.Less(Vector3.Distance(start, good.Agent.transform.position), 1e-3f, "The member who warped OK is rolled back to where it was.");
            Assert.IsTrue(gridA.IsOccupied(new GridCoordinate(1, 1), out var holder) && holder == good.Id, "...and holds its original cell again.");
            Assert.IsTrue(SceneManager.GetSceneByPath(SceneAPath).isLoaded);
            Assert.IsFalse(SceneManager.GetSceneByPath(SceneBPath).isLoaded);
            Assert.IsFalse(gate.IsBusy);
        }

        [UnityTest]
        public IEnumerator PartyMembersWhoseFormationSlotsCollapseIntoOneCellAreStillPlacedInDistinctCells()
        {
            var pathfinding = new GridPathfindingService();
            var session = new GameSession(new RulesDatabase(), new DiceService(new SeededRandomSource(1)));
            var spawnService = new WorldSpawnService(session) { GridNavigation = pathfinding };
            var party = new PartyState();
            var members = new[]
            {
                AddMember(session, party, pathfinding, ACell(1, 1)),
                AddMember(session, party, pathfinding, ACell(2, 1)),
                AddMember(session, party, pathfinding, ACell(3, 1)),
            };

            yield return LoadActivateAndBind(SceneAPath, pathfinding);

            // Every slot is the same point: without slot avoidance the second Warp into that cell is refused and the
            // whole travel rolls back.
            var sameSpot = new Vector3(2000f, 0f, 5000f) + new Vector3(3.5f, 0f, 3.5f);
            var gate = new WorldTransitionGate();
            var coordinator = new SceneTravelCoordinator(
                session, spawnService, party, gate, formationSolver: new FixedPointFormationSolver(sameSpot), gridPathfinding: pathfinding);

            SceneTravelResult result = null;
            yield return coordinator.TravelCoroutine(SceneBPath, "grid-dest-b", r => result = r);
            yield return null;

            Assert.IsTrue(result.Success, result.Message);
            var gridB = pathfinding.ActiveGrid;
            var cells = members.Select(m => gridB.ToGrid(m.Agent.transform.position)).ToList();
            Assert.AreEqual(3, cells.Distinct().Count(), $"Each member needs its own cell, got: {string.Join(", ", cells)}");
        }
    }
}
