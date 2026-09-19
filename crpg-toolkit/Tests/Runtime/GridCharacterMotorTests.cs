using System.Collections;
using System.Collections.Generic;
using System.Linq;
using CRPG.Actors;
using CRPG.Core;
using CRPG.Exploration;
using CRPG.Party;
using CRPG.Gameplay;
using CRPG.Gameplay.Actions;
using CRPG.Gameplay.Interactions;
using CRPG.Gameplay.Interactions.Adapters;
using CRPG.Navigation;
using CRPG.Navigation.Grid;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace CRPG.Tests.PlayMode
{
    /// <summary>
    /// 2D campaign Task 1.3: the real <see cref="GridCharacterMotor"/> over the real <see cref="GridPathfindingService"/>.
    /// Inherits the whole Task 1.1 movement contract (every test in <see cref="GridMotorContractTests"/> runs here
    /// against production code) and adds what only the real motor has: occupancy, scene-scoped grid swaps,
    /// the door seam, and composition-root wiring.
    /// </summary>
    public class GridCharacterMotorContractTests : GridMotorContractTests
    {
        private WalkabilityGrid grid;
        private GridPathfindingService service;

        protected override Arena CreateArena(int width, int depth, IEnumerable<(int x, int z)> blocked)
        {
            grid = new WalkabilityGrid(ArenaOrigin, 1f, width, depth, blocked.Select(b => new GridCoordinate(b.x, b.z)));
            service = new GridPathfindingService();
            service.SetActiveGrid(grid);
            var captured = grid;
            return new Arena
            {
                Nav = service,
                IsWalkable = captured.IsWalkableWorld,
                Center = (x, z) => captured.ToWorld(new GridCoordinate(x, z)),
            };
        }

        protected override GameObject CreateMover(Arena arena, Vector3 start, float speed)
        {
            var go = new GameObject("GridMover");
            go.transform.position = start;
            var motor = go.AddComponent<GridCharacterMotor>();
            motor.Pathfinding = service;
            motor.Speed = speed;
            return go;
        }

        private static GridCharacterMotor MotorOf(Mover mover) => mover.Transform.GetComponent<GridCharacterMotor>();

        // ------------------------------------------------------------------------------------------------
        // Occupancy
        // ------------------------------------------------------------------------------------------------

        [UnityTest]
        public IEnumerator AMoverCannotEnterAnOccupiedCellItStopsShortReportsBlockedAndCanRetryOnceItIsFree()
        {
            var arena = CreateArena(10, 3, Enumerable.Empty<(int, int)>());
            var blocker = RegisterMover(arena, arena.Center(2, 1), movementMax: 100);
            var walker = RegisterMover(arena, arena.Center(0, 1), movementMax: 100);
            yield return null; // let both claim the cell they stand in

            var request = walker.Motor.SetDestination(arena.Center(5, 1));
            MoveStatus? terminal = null;
            yield return WaitForTerminalStatus(walker.Motor, s => terminal = s);

            Assert.AreEqual(MoveStatus.Blocked, terminal);
            Assert.AreEqual(request.RequestId, walker.Motor.Snapshot.RequestId);
            Assert.Less(Vector3.Distance(arena.Center(1, 1), walker.Transform.position), 1e-4f, "It must stop in its own last cell, not part-way into the occupied one.");
            Assert.IsTrue(grid.IsOccupied(new GridCoordinate(2, 1), out var holder));
            Assert.AreEqual(MotorOf(blocker).OccupantId, holder);
            Assert.IsTrue(grid.IsOccupied(new GridCoordinate(1, 1), out var walkerCell) && walkerCell == MotorOf(walker).OccupantId);
            Assert.IsFalse(grid.IsOccupied(new GridCoordinate(0, 1), out _), "The cell it left must have been released.");

            // The blocker steps aside; the retry now succeeds.
            blocker.Motor.SetDestination(arena.Center(2, 2));
            yield return WaitForTerminalStatus(blocker.Motor, s => { });
            walker.Motor.SetDestination(arena.Center(5, 1));
            MoveStatus? retry = null;
            yield return WaitForTerminalStatus(walker.Motor, s => retry = s);
            Assert.AreEqual(MoveStatus.Arrived, retry);
        }

        [UnityTest]
        public IEnumerator ABlockedLegEndsMoveActionExecutionInterruptedWithTheRealDistanceSettled()
        {
            var arena = CreateArena(10, 3, Enumerable.Empty<(int, int)>());
            var mover = RegisterMover(arena, arena.Center(0, 1), movementMax: 10);
            RegisterMover(arena, arena.Center(3, 1), movementMax: 10); // stands in the way
            yield return null;

            var action = StartMove(arena, mover, arena.Center(6, 1));
            yield return WaitForActionToEnd(action);

            Assert.AreEqual(ActionOutcome.Interrupted, action.Outcome, "Blocked mid-traversal is an interruption, exactly like a NavMesh Blocked result.");
            Assert.IsFalse(action.ArrivedAtDestination);
            Assert.Less(Vector3.Distance(arena.Center(2, 1), mover.Transform.position), 1e-4f);
            Assert.AreEqual(2, MovementSpent(mover, 10), "The two cells actually walked are settled; nothing more.");
        }

        [UnityTest]
        public IEnumerator DisablingOrDestroyingAMoverReleasesItsCellAndReenablingClaimsItAgain()
        {
            var arena = CreateArena(6, 3, Enumerable.Empty<(int, int)>());
            var mover = RegisterMover(arena, arena.Center(2, 1), movementMax: 100);
            var motor = MotorOf(mover);
            var cell = new GridCoordinate(2, 1);
            yield return null;
            Assert.IsTrue(grid.IsOccupied(cell, out _));

            motor.enabled = false;
            Assert.IsFalse(grid.IsOccupied(cell, out _), "A disabled mover must not keep blocking its cell.");

            motor.enabled = true;
            yield return null;
            Assert.IsTrue(grid.IsOccupied(cell, out var holder));
            Assert.AreEqual(motor.OccupantId, holder);

            Object.DestroyImmediate(motor.gameObject);
            Assert.IsFalse(grid.IsOccupied(cell, out _), "A destroyed/despawned mover must free its cell.");
        }

        [UnityTest]
        public IEnumerator WarpIsRefusedIntoAnOccupiedCellAndTransfersTheClaimOnSuccess()
        {
            var arena = CreateArena(8, 4, Enumerable.Empty<(int, int)>());
            var a = RegisterMover(arena, arena.Center(1, 1), movementMax: 100);
            var b = RegisterMover(arena, arena.Center(4, 1), movementMax: 100);
            yield return null;
            var before = a.Transform.position;

            Assert.IsFalse(a.Motor.Warp(arena.Center(4, 1)), "b is standing there.");
            Assert.AreEqual(before, a.Transform.position);
            Assert.IsTrue(grid.IsOccupied(new GridCoordinate(1, 1), out var stillA) && stillA == MotorOf(a).OccupantId);

            Assert.IsTrue(a.Motor.Warp(arena.Center(6, 2)));
            Assert.IsFalse(grid.IsOccupied(new GridCoordinate(1, 1), out _));
            Assert.IsTrue(grid.IsOccupied(new GridCoordinate(6, 2), out var nowA) && nowA == MotorOf(a).OccupantId);
            Assert.IsTrue(grid.IsOccupied(new GridCoordinate(4, 1), out var stillB) && stillB == MotorOf(b).OccupantId);
        }

        [UnityTest]
        public IEnumerator SwappingTheActiveGridReleasesTheOldClaimAndReclaimsInTheNewOne()
        {
            var arena = CreateArena(8, 3, Enumerable.Empty<(int, int)>());
            var mover = RegisterMover(arena, arena.Center(2, 1), movementMax: 100);
            var cell = new GridCoordinate(2, 1);
            var original = grid;
            yield return null;
            Assert.IsTrue(original.IsOccupied(cell, out _));

            var other = new WalkabilityGrid(ArenaOrigin, 1f, 8, 3);
            service.SetActiveGrid(other); // what scene travel does
            yield return null;
            Assert.IsFalse(original.IsOccupied(cell, out _), "The old location's grid must be left clean.");
            Assert.IsTrue(other.IsOccupied(cell, out var holder));
            Assert.AreEqual(MotorOf(mover).OccupantId, holder);

            var tooSmall = new WalkabilityGrid(ArenaOrigin, 1f, 2, 2); // the mover's position is outside this one
            service.SetActiveGrid(tooSmall);
            yield return null;
            Assert.IsFalse(other.IsOccupied(cell, out _));

            service.SetActiveGrid(original);
            yield return null;
            Assert.IsTrue(original.IsOccupied(cell, out _), "Coming back to a grid it fits in re-claims its cell.");
        }

        // ------------------------------------------------------------------------------------------------
        // Motor behaviour the shared contract doesn't spell out
        // ------------------------------------------------------------------------------------------------

        [UnityTest]
        public IEnumerator SetDestinationIsInvalidWithNoActiveGridAndWorksAgainOnceOneIsSet()
        {
            var arena = CreateArena(6, 3, Enumerable.Empty<(int, int)>());
            var mover = RegisterMover(arena, arena.Center(0, 1), movementMax: 100);
            var active = grid;

            service.SetActiveGrid(null);
            Assert.AreEqual(MoveStatus.Invalid, mover.Motor.SetDestination(arena.Center(3, 1)).Status);

            service.SetActiveGrid(active);
            Assert.AreNotEqual(MoveStatus.Invalid, mover.Motor.SetDestination(arena.Center(3, 1)).Status);
            yield break;
        }

        [UnityTest]
        public IEnumerator TheSnapshotReportsRealVelocityAndRemainingDistanceWhileMovingAndZeroAtRest()
        {
            // FacingController's motion facing and Phase 2's rocking-motion driver both read Velocity, so a motor
            // that leaves it at zero would silently kill both for every grid character.
            var arena = CreateArena(30, 3, Enumerable.Empty<(int, int)>());
            var mover = RegisterMover(arena, arena.Center(0, 1), movementMax: 100, speed: 5f);

            mover.Motor.SetDestination(arena.Center(25, 1));
            yield return null;
            yield return null;
            var moving = mover.Motor.Snapshot;
            Assert.AreEqual(MoveStatus.Moving, moving.Status);
            Assert.AreEqual(5f, moving.Velocity.magnitude, 0.01f);
            Assert.Greater(moving.Velocity.x, 0f);
            Assert.AreEqual(moving.Velocity, moving.DesiredVelocity);
            Assert.IsTrue(moving.IsOnNavMesh, "IsOnNavMesh means 'standing on a walkable cell' for a grid mover.");
            yield return null;
            yield return null;
            Assert.Less(mover.Motor.Snapshot.RemainingDistance, moving.RemainingDistance);

            mover.Motor.Stop();
            Assert.AreEqual(Vector3.zero, mover.Motor.Snapshot.Velocity);
        }

        [UnityTest]
        public IEnumerator ADoorAdapterDrivingAGridBlockerOpensAndClosesTheRealPath()
        {
            var arena = CreateArena(9, 3, Enumerable.Empty<(int, int)>());
            var providerGo = new GameObject("GridProvider");
            var provider = providerGo.AddComponent<WalkabilityGridProvider>();
            provider.Configure(ArenaOrigin, 1f, 9, 3);
            service.SetActiveGrid(provider.Grid);

            // A door across the corridor at x = 4, closed at start.
            var doorGo = new GameObject("Door");
            doorGo.SetActive(false); // defer Awake so WorldInteractable can be configured first
            var blocker = doorGo.AddComponent<GridWalkabilityBlocker>();
            blocker.Configure(provider, new[] { new GridCoordinate(4, 0), new GridCoordinate(4, 1), new GridCoordinate(4, 2) }, startBlocking: true);
            var adapter = doorGo.AddComponent<UseDoorInteractionAdapter>();
            var anchor = new GameObject("DoorAnchor");
            doorGo.GetComponent<WorldInteractable>().Configure(anchor.transform, 1.5f);
            doorGo.SetActive(true);

            try
            {
                var mover = RegisterMover(arena, arena.Center(1, 1), movementMax: 100);
                var beyondTheDoor = arena.Center(7, 1);

                Assert.AreEqual(MoveStatus.Invalid, mover.Motor.SetDestination(beyondTheDoor).Status, "A closed door blocks the only way through.");

                adapter.BeginExecute(RuntimeEntityId.NewId(), _ => { });
                Assert.IsTrue(adapter.IsOpen);
                Assert.IsFalse(blocker.IsBlocking);
                Assert.AreNotEqual(MoveStatus.Invalid, mover.Motor.SetDestination(beyondTheDoor).Status);
                MoveStatus? terminal = null;
                yield return WaitForTerminalStatus(mover.Motor, s => terminal = s);
                Assert.AreEqual(MoveStatus.Arrived, terminal);

                adapter.BeginExecute(RuntimeEntityId.NewId(), _ => { });
                Assert.IsFalse(adapter.IsOpen);
                Assert.IsTrue(blocker.IsBlocking);
                Assert.AreEqual(MoveStatus.Invalid, mover.Motor.SetDestination(arena.Center(1, 1)).Status, "Closed again: the mover is now on the far side and cannot come back.");
            }
            finally
            {
                Object.DestroyImmediate(doorGo);
                Object.DestroyImmediate(anchor);
                Object.DestroyImmediate(providerGo);
            }
        }

        private sealed class FakeCommandSource : IPlayerCommandSource
        {
            public event System.Action<SelectCommand> Select;
            public event System.Action<MoveCommand> Move;
            public event System.Action<InteractCommand> Interact;
            public event System.Action Cancel;

            public void RaiseMove(MoveCommand command) => Move?.Invoke(command);
        }

        private sealed class TransformSelectableSource : ISelectableSource
        {
            private readonly List<(RuntimeEntityId id, Transform transform)> entries = new();
            public void Add(RuntimeEntityId id, Transform transform) => entries.Add((id, transform));

            public IEnumerable<SelectableEntity> GetSelectables() =>
                entries.Select(e => new SelectableEntity(e.id, e.transform.position));
        }

        /// <summary>Forwards to a real motor and remembers where each member was told to go.</summary>
        private sealed class RecordingMotor : IMovable
        {
            private readonly IMovable inner;
            public Vector3? LastDestination { get; private set; }
            public RecordingMotor(IMovable inner) => this.inner = inner;

            public MoveResult SetDestination(Vector3 destination)
            {
                LastDestination = destination;
                return inner.SetDestination(destination);
            }

            public void Stop() => inner.Stop();
            public bool Warp(Vector3 position) => inner.Warp(position);
            public MotionSnapshot Snapshot => inner.Snapshot;
        }

        [UnityTest]
        public IEnumerator ARealPartyMoveOrderGivesEveryGridMotorItsOwnDistinctCellEvenWhenTheFormationSlotsCollapse()
        {
            // End to end through the real PartyExplorationController and PartyMovementService, with a formation
            // spacing (0.1) far smaller than a cell so every slot lands in one cell. Asserts distinct, accepted
            // destinations -- not simultaneous arrival: pathfinding deliberately ignores occupancy, so whether a
            // teammate is briefly in the way on the route (a Blocked mid-walk) is the accepted, documented
            // limitation, not what this test is about.
            var arena = CreateArena(20, 20, Enumerable.Empty<(int, int)>());
            var selectables = new TransformSelectableSource();
            var movers = new List<Mover>();
            for (var i = 0; i < 4; i++)
            {
                var mover = RegisterMover(arena, arena.Center(1 + i, 1), movementMax: 100);
                movers.Add(mover);
                selectables.Add(mover.Id, mover.Transform);
            }

            yield return null; // every mover claims the cell it starts in

            var party = new PartyState(movers.Select(m => m.Id));
            var source = new FakeCommandSource();
            var controller = new PartyExplorationController(
                party, new PartyMovementService(service, selectables), new LineFormationSolver(), source, formationSpacing: 0.1f);
            var recorders = new Dictionary<RuntimeEntityId, RecordingMotor>();
            foreach (var mover in movers)
            {
                recorders[mover.Id] = new RecordingMotor(mover.Motor);
                controller.RegisterMotor(mover.Id, recorders[mover.Id]);
            }

            var outcomes = new Dictionary<RuntimeEntityId, MoveResult>();
            controller.MemberMoveOutcome += (id, result) => outcomes[id] = result;

            source.RaiseMove(new MoveCommand(arena.Center(12, 10), Vector3.forward));

            Assert.AreEqual(4, outcomes.Count);
            foreach (var mover in movers)
                Assert.AreNotEqual(MoveStatus.Invalid, outcomes[mover.Id].Status, "Every member must be given a real, reachable, free destination.");

            var destinationCells = movers.Select(m => grid.ToGrid(recorders[m.Id].LastDestination.Value)).ToList();
            Assert.AreEqual(4, destinationCells.Distinct().Count(), $"Each member needs its own cell, got: {string.Join(", ", destinationCells)}");

            controller.Dispose();
        }

        [Test]
        public void WorldSpawnServiceHandsEverySpawnedGridMotorItsPathfinderAndEntityId()
        {
            var pathfinding = new GridPathfindingService();
            var session = new GameSession(new RulesDatabase(), new DiceService(new SeededRandomSource(1)));
            var spawnService = new WorldSpawnService(session) { GridNavigation = pathfinding };
            var characterId = RuntimeEntityId.NewId();
            session.Characters.Register(new CharacterState(characterId));

            var template = new GameObject("Template");
            template.AddComponent<GridCharacterMotor>();
            var child = new GameObject("Child");
            child.transform.SetParent(template.transform);
            child.AddComponent<GridCharacterMotor>(); // also found, like CharacterAgent's own IMovable lookup

            var instance = spawnService.BindRestoredView("spawn-1", characterId, default, template, Vector3.zero, Quaternion.identity, default, startInactive: false);
            Object.DestroyImmediate(template);

            try
            {
                var motors = instance.GetComponentsInChildren<GridCharacterMotor>(true);
                Assert.AreEqual(2, motors.Length);
                foreach (var motor in motors)
                {
                    Assert.AreSame(pathfinding, motor.Pathfinding);
                    Assert.AreEqual(characterId, motor.OccupantId);
                }
            }
            finally
            {
                Object.DestroyImmediate(instance);
            }
        }
    }

    public class WalkabilityGridProviderTests
    {
        private readonly List<Scene> scenes = new();
        private readonly List<GameObject> spawned = new();

        [TearDown]
        public void TearDown()
        {
            foreach (var go in spawned) if (go != null) Object.DestroyImmediate(go);
            spawned.Clear();
            foreach (var scene in scenes) if (scene.isLoaded) SceneManager.UnloadSceneAsync(scene);
            scenes.Clear();
        }

        private WalkabilityGridProvider CreateProviderInScene(Scene scene, int width)
        {
            var go = new GameObject($"Provider{width}");
            SceneManager.MoveGameObjectToScene(go, scene);
            spawned.Add(go);
            var provider = go.AddComponent<WalkabilityGridProvider>();
            provider.Configure(new Vector3(10f, 1f, 20f), 2f, width, 3, new[] { new GridCoordinate(1, 1) });
            return provider;
        }

        [Test]
        public void TheGridIsBuiltFromTheConfiguredDataOnceAndLazily()
        {
            var go = new GameObject("Provider");
            spawned.Add(go);
            var provider = go.AddComponent<WalkabilityGridProvider>();
            provider.Configure(new Vector3(10f, 1f, 20f), 2f, 6, 4, new[] { new GridCoordinate(2, 3) });

            var grid = provider.Grid;

            Assert.AreEqual(6, grid.Width);
            Assert.AreEqual(4, grid.Depth);
            Assert.AreEqual(2f, grid.CellSize);
            Assert.AreEqual(1f, grid.GroundY);
            Assert.IsFalse(grid.IsWalkable(new GridCoordinate(2, 3)));
            Assert.IsTrue(grid.IsWalkable(new GridCoordinate(0, 0)));
            Assert.AreSame(grid, provider.Grid, "Repeated access must return the same grid instance the pathfinder was pointed at.");
        }

        [Test]
        public void InvalidSerializedDataFailsLoudlyRatherThanBuildingAWrongMap()
        {
            var go = new GameObject("Provider");
            spawned.Add(go);
            var provider = go.AddComponent<WalkabilityGridProvider>();
            provider.Configure(Vector3.zero, 1f, 0, 4);

            Assert.Throws<System.ArgumentOutOfRangeException>(() => { var _ = provider.Grid; });
        }

        [Test]
        public void FindInSceneReturnsOnlyTheProviderBelongingToThatScene()
        {
            // Two location scenes are briefly loaded together during travel; each must resolve only its own grid.
            var sceneA = SceneManager.CreateScene("GridProviderTestA");
            var sceneB = SceneManager.CreateScene("GridProviderTestB");
            var sceneC = SceneManager.CreateScene("GridProviderTestC");
            scenes.AddRange(new[] { sceneA, sceneB, sceneC });
            CreateProviderInScene(sceneA, 5);
            CreateProviderInScene(sceneB, 7);

            Assert.AreEqual(5, WalkabilityGridProvider.FindInScene(sceneA).Grid.Width);
            Assert.AreEqual(7, WalkabilityGridProvider.FindInScene(sceneB).Grid.Width);
            Assert.IsNull(WalkabilityGridProvider.FindInScene(sceneC));
        }
    }
}
