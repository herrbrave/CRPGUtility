using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using CRPG.Actors;
using CRPG.Combat;
using CRPG.Core;
using CRPG.Gameplay.Actions;
using CRPG.Navigation;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.TestTools;

namespace CRPG.Tests.PlayMode
{
    /// <summary>
    /// 2D campaign Task 1.1: the movement contract any grid backend must meet before it can replace NavMesh
    /// movement, proven against the REAL <see cref="MoveActionExecution"/> (unchanged) and the raw
    /// <see cref="IMovable"/> calls production code makes directly (PartyExplorationController's click-to-move
    /// and InteractionController's walk-to-interact both call SetDestination with an arbitrary, unplanned
    /// destination and only poll Snapshot -- they never go through MoveActionExecution at all).
    /// <para>Abstract on purpose: the concrete subclass supplies the arena and motor. This task's subclass
    /// uses a deliberately minimal TEST-ONLY grid (below) so the contract can be settled before any production
    /// grid code exists; the real GridPathfindingService/GridCharacterMotor (Tasks 1.2/1.3) get a second
    /// subclass and must pass the identical tests.</para>
    /// </summary>
    public abstract class GridMotorContractTests
    {
        protected static readonly Vector3 ArenaOrigin = new(3000f, 0f, 3000f);
        protected static readonly DefinitionId MovementResourceId = new("resource-movement-gridcontract");

        /// <summary>What the base tests need to know about a grid, independent of which backend built it.</summary>
        protected sealed class Arena
        {
            public INavMeshService Nav;
            public Func<Vector3, bool> IsWalkable;
            public Func<int, int, Vector3> Center;
        }

        protected abstract Arena CreateArena(int width, int depth, IEnumerable<(int x, int z)> blocked);

        /// <summary>Returns a GameObject carrying the backend's <see cref="IMovable"/>, placed at start.</summary>
        protected abstract GameObject CreateMover(Arena arena, Vector3 start, float speed);

        private readonly List<GameObject> spawned = new();
        private CharacterRegistry characters;
        private TestPositionProvider positions;
        private MovementResolutionService movementResolution;

        [SetUp]
        public void SetUp()
        {
            characters = new CharacterRegistry();
            positions = new TestPositionProvider();
            movementResolution = new MovementResolutionService(characters, positions, new NoOpMovementController());
        }

        [TearDown]
        public void TearDown()
        {
            Time.timeScale = 1f;
            foreach (var go in spawned) if (go != null) UnityEngine.Object.DestroyImmediate(go);
            spawned.Clear();
        }

        protected readonly struct Mover
        {
            public Mover(RuntimeEntityId id, IMovable motor, Transform transform)
            {
                Id = id;
                Motor = motor;
                Transform = transform;
            }

            public RuntimeEntityId Id { get; }
            public IMovable Motor { get; }
            public Transform Transform { get; }
        }

        protected Mover RegisterMover(Arena arena, Vector3 start, int movementMax, float speed = 20f)
        {
            var state = new CharacterState(RuntimeEntityId.NewId());
            state.AddResource(new ResourceState(MovementResourceId, max: movementMax));
            characters.Register(state);

            var go = CreateMover(arena, start, speed);
            spawned.Add(go);
            positions.Track(state.Id, go.transform);
            return new Mover(state.Id, go.GetComponent<IMovable>(), go.transform);
        }

        protected MoveActionExecution StartMove(Arena arena, Mover mover, Vector3 destination) =>
            new(mover.Id, mover.Motor, movementResolution, arena.Nav, characters, positions, MovementResourceId, destination);

        protected int MovementSpent(Mover mover, int movementMax)
        {
            characters.TryGetCharacter(mover.Id, out var state);
            state.TryGetResource(MovementResourceId, out var movement);
            return movementMax - movement.Current;
        }

        // Budgeted by accumulated (scaled) Time.deltaTime, not a raw frame count -- headless -batchmode ticks
        // frames far faster than realtime (see CharacterMotionTests.WaitForTerminalStatus).
        protected static IEnumerator WaitForActionToEnd(ActionExecution action, float maxSimulatedSeconds = 30f)
        {
            var elapsed = 0f;
            while (elapsed < maxSimulatedSeconds)
            {
                if (action.Outcome.HasValue) yield break;
                action.Tick(Time.deltaTime);
                elapsed += Time.deltaTime;
                yield return null;
            }

            Assert.Fail($"Action never completed within {maxSimulatedSeconds} simulated seconds (phase: {action.Phase}).");
        }

        protected static IEnumerator WaitForTerminalStatus(IMovable motor, Action<MoveStatus> onTerminal, float maxSimulatedSeconds = 30f)
        {
            var elapsed = 0f;
            while (elapsed < maxSimulatedSeconds)
            {
                var status = motor.Snapshot.Status;
                if (status is MoveStatus.Arrived or MoveStatus.Blocked or MoveStatus.Invalid or MoveStatus.Cancelled)
                {
                    onTerminal(status);
                    yield break;
                }

                elapsed += Time.deltaTime;
                yield return null;
            }

            Assert.Fail($"Move request never reached a terminal status within {maxSimulatedSeconds} simulated seconds (last: {motor.Snapshot.Status}).");
        }

        // ------------------------------------------------------------------------------------------------
        // MoveActionExecution: budget accounting on a grid (the review's integer-rounding-boundary risk)
        // ------------------------------------------------------------------------------------------------

        [UnityTest]
        public IEnumerator AStraightNCellMoveSpendsExactlyNMovement()
        {
            foreach (var n in new[] { 1, 2, 3, 5, 7 })
            {
                var arena = CreateArena(12, 4, Enumerable.Empty<(int, int)>());
                var mover = RegisterMover(arena, arena.Center(0, 1), movementMax: 10);
                var action = StartMove(arena, mover, arena.Center(n, 1));

                yield return WaitForActionToEnd(action);

                Assert.AreEqual(ActionOutcome.Succeeded, action.Outcome, $"n={n}");
                Assert.IsTrue(action.ArrivedAtDestination, $"n={n}");
                Assert.AreEqual(n, MovementSpent(mover, 10), $"A straight {n}-cell move must spend exactly {n}, not {n + 1} from float drift landing on Mathf.CeilToInt's boundary.");
            }
        }

        [UnityTest]
        public IEnumerator DiagonalMovesSpendTheCeilingOfTheRealEuclideanDistance()
        {
            var arena = CreateArena(8, 8, Enumerable.Empty<(int, int)>());
            var mover = RegisterMover(arena, arena.Center(0, 0), movementMax: 10);
            var action = StartMove(arena, mover, arena.Center(3, 3)); // 3 diagonal steps = 4.2426 units

            yield return WaitForActionToEnd(action);

            Assert.AreEqual(ActionOutcome.Succeeded, action.Outcome);
            Assert.IsTrue(action.ArrivedAtDestination);
            Assert.AreEqual(5, MovementSpent(mover, 10), "Ceil(3 * sqrt(2)) = 5, settled cumulatively rather than per leg.");
        }

        [UnityTest]
        public IEnumerator AnExhaustedBudgetIssuesAZeroLengthLegThatIsASuccessNotAnInterrupt()
        {
            var arena = CreateArena(8, 4, Enumerable.Empty<(int, int)>());
            var start = arena.Center(0, 1);
            var mover = RegisterMover(arena, start, movementMax: 0);
            var action = StartMove(arena, mover, arena.Center(5, 1));

            yield return WaitForActionToEnd(action);

            // MoveActionExecution clips the leg to the (zero) affordable budget, so it calls SetDestination with
            // the mover's own current position. That must resolve as an arrival, never Invalid -- Invalid would
            // end the whole action Interrupted.
            Assert.AreEqual(ActionOutcome.Succeeded, action.Outcome);
            Assert.IsFalse(action.ArrivedAtDestination);
            Assert.Less(Vector3.Distance(start, mover.Transform.position), 1e-4f);
            Assert.AreEqual(0, MovementSpent(mover, 0));
        }

        [UnityTest]
        public IEnumerator ABudgetClipLandingMidCellIsReachedExactly()
        {
            var arena = CreateArena(8, 8, Enumerable.Empty<(int, int)>());
            var mover = RegisterMover(arena, arena.Center(0, 0), movementMax: 3);
            var action = StartMove(arena, mover, arena.Center(4, 4));

            yield return WaitForActionToEnd(action);

            // Leg 1 (diagonal, 1.4142) settles as 2, leaving 1 -- the next 1.4142 leg is clipped to exactly 1.0,
            // which lands mid-cell: a non-cell-aligned SetDestination the motor must honour precisely.
            var expected = arena.Center(1, 1) + new Vector3(1f, 0f, 1f).normalized * 1f;
            Assert.AreEqual(ActionOutcome.Succeeded, action.Outcome);
            Assert.IsFalse(action.ArrivedAtDestination);
            Assert.Less(Vector3.Distance(expected, mover.Transform.position), 1e-3f);
            Assert.AreEqual(3, MovementSpent(mover, 3), "The full affordable budget must be spent.");
        }

        // ------------------------------------------------------------------------------------------------
        // Raw IMovable calls, exactly as PartyExplorationController / InteractionController make them
        // ------------------------------------------------------------------------------------------------

        [UnityTest]
        public IEnumerator AnUnplannedLongDestinationPathsAroundAWallAndNeverEntersIt()
        {
            // A wall down x = 5 with a gap only at z = 10..11: a straight line from (1,1) to (9,1) is blocked.
            var blocked = Enumerable.Range(0, 10).Select(z => (5, z)).ToList();
            var arena = CreateArena(12, 12, blocked);
            var mover = RegisterMover(arena, arena.Center(1, 1), movementMax: 100);
            var destination = arena.Center(9, 1);

            var request = mover.Motor.SetDestination(destination);
            Assert.IsTrue(request.RequestId.IsValid);
            Assert.AreNotEqual(MoveStatus.Invalid, request.Status);

            var travelled = 0f;
            var last = mover.Transform.position;
            MoveStatus? terminal = null;
            var elapsed = 0f;
            while (elapsed < 30f && terminal == null)
            {
                yield return null;
                elapsed += Time.deltaTime;
                Assert.IsTrue(arena.IsWalkable(mover.Transform.position), $"The mover entered a blocked cell at {mover.Transform.position}.");
                travelled += Vector3.Distance(last, mover.Transform.position);
                last = mover.Transform.position;

                var snapshot = mover.Motor.Snapshot;
                Assert.AreEqual(request.RequestId, snapshot.RequestId);
                if (snapshot.Status is MoveStatus.Arrived or MoveStatus.Blocked or MoveStatus.Invalid or MoveStatus.Cancelled) terminal = snapshot.Status;
            }

            Assert.AreEqual(MoveStatus.Arrived, terminal);
            Assert.Less(Vector3.Distance(destination, mover.Transform.position), 1e-3f);
            Assert.Greater(travelled, 16f, "The straight line is 8 units; going around the wall through the gap is far longer.");
        }

        [UnityTest]
        public IEnumerator AnArbitraryNonCellAlignedDestinationIsReachedExactly()
        {
            var arena = CreateArena(8, 8, Enumerable.Empty<(int, int)>());
            var mover = RegisterMover(arena, arena.Center(1, 1), movementMax: 100);
            var destination = arena.Center(5, 4) + new Vector3(0.37f, 0f, -0.29f);

            var request = mover.Motor.SetDestination(destination);
            Assert.IsTrue(request.RequestId.IsValid);

            MoveStatus? terminal = null;
            yield return WaitForTerminalStatus(mover.Motor, s => terminal = s);

            Assert.AreEqual(MoveStatus.Arrived, terminal);
            Assert.Less(Vector3.Distance(destination, mover.Transform.position), 1e-4f);
        }

        [UnityTest]
        public IEnumerator AZeroLengthDestinationIsAcceptedAndArrivesNeverInvalid()
        {
            var arena = CreateArena(6, 6, Enumerable.Empty<(int, int)>());
            var mover = RegisterMover(arena, arena.Center(2, 2), movementMax: 100);

            var request = mover.Motor.SetDestination(mover.Transform.position);

            Assert.AreNotEqual(MoveStatus.Invalid, request.Status);
            Assert.IsTrue(request.RequestId.IsValid);
            MoveStatus? terminal = null;
            yield return WaitForTerminalStatus(mover.Motor, s => terminal = s);
            Assert.AreEqual(MoveStatus.Arrived, terminal);
            Assert.AreEqual(request.RequestId, mover.Motor.Snapshot.RequestId);
        }

        [UnityTest]
        public IEnumerator AnUnreachableDestinationIsInvalidAndLeavesTheRequestInFlightUntouched()
        {
            // (6,6) is walkable but fully walled in.
            var ring = new List<(int, int)>();
            for (var x = 5; x <= 7; x++)
                for (var z = 5; z <= 7; z++)
                    if (!(x == 6 && z == 6)) ring.Add((x, z));
            var arena = CreateArena(10, 10, ring);
            var mover = RegisterMover(arena, arena.Center(0, 0), movementMax: 100, speed: 5f);

            var first = mover.Motor.SetDestination(arena.Center(9, 0));
            Assert.IsTrue(first.RequestId.IsValid);
            yield return null;

            var rejected = mover.Motor.SetDestination(arena.Center(6, 6));

            Assert.AreEqual(MoveStatus.Invalid, rejected.Status);
            Assert.AreEqual(MoveRequestId.None, rejected.RequestId);
            Assert.AreEqual(first.RequestId, mover.Motor.Snapshot.RequestId, "A rejected request must not disturb the one in flight.");
            Assert.AreEqual(MoveStatus.Moving, mover.Motor.Snapshot.Status);
        }

        [UnityTest]
        public IEnumerator ANewDestinationSupersedesTheRequestInFlight()
        {
            var arena = CreateArena(20, 6, Enumerable.Empty<(int, int)>());
            var mover = RegisterMover(arena, arena.Center(0, 2), movementMax: 100, speed: 5f);

            var first = mover.Motor.SetDestination(arena.Center(18, 2));
            yield return null;
            yield return null;
            var secondDestination = arena.Center(2, 5);
            var second = mover.Motor.SetDestination(secondDestination);

            Assert.AreNotEqual(first.RequestId, second.RequestId);
            Assert.AreEqual(second.RequestId, mover.Motor.Snapshot.RequestId);

            MoveStatus? terminal = null;
            yield return WaitForTerminalStatus(mover.Motor, s => terminal = s);
            Assert.AreEqual(MoveStatus.Arrived, terminal);
            Assert.AreEqual(second.RequestId, mover.Motor.Snapshot.RequestId);
            Assert.Less(Vector3.Distance(secondDestination, mover.Transform.position), 1e-3f);
        }

        [UnityTest]
        public IEnumerator StopCancelsRetainsTheRequestIdAndHaltsMovement()
        {
            var arena = CreateArena(20, 4, Enumerable.Empty<(int, int)>());
            var mover = RegisterMover(arena, arena.Center(0, 1), movementMax: 100, speed: 5f);

            var request = mover.Motor.SetDestination(arena.Center(18, 1));
            yield return null;
            yield return null;
            mover.Motor.Stop();

            Assert.AreEqual(MoveStatus.Cancelled, mover.Motor.Snapshot.Status);
            Assert.AreEqual(request.RequestId, mover.Motor.Snapshot.RequestId);
            var stoppedAt = mover.Transform.position;
            yield return null;
            yield return null;
            Assert.AreEqual(stoppedAt, mover.Transform.position);
        }

        [UnityTest]
        public IEnumerator WarpRefusesAnUnwalkableCellWithoutMovingAndAcceptsAWalkableOne()
        {
            var arena = CreateArena(10, 10, new[] { (3, 3) });
            var mover = RegisterMover(arena, arena.Center(0, 0), movementMax: 100, speed: 5f);
            var request = mover.Motor.SetDestination(arena.Center(9, 0));
            yield return null;
            var before = mover.Transform.position;

            Assert.IsFalse(mover.Motor.Warp(arena.Center(3, 3)));
            Assert.AreEqual(before, mover.Transform.position, "A refused Warp must leave the mover exactly where it was.");
            Assert.AreEqual(MoveStatus.Moving, mover.Motor.Snapshot.Status, "A refused Warp must not cancel the request in flight.");

            Assert.IsTrue(mover.Motor.Warp(arena.Center(6, 6)));
            Assert.Less(Vector3.Distance(arena.Center(6, 6), mover.Transform.position), 1e-4f);
            Assert.AreEqual(MoveStatus.Cancelled, mover.Motor.Snapshot.Status);
            Assert.AreEqual(request.RequestId, mover.Motor.Snapshot.RequestId);
        }

        [UnityTest]
        public IEnumerator PausingViaTimeScaleZeroIsATrueNoOpAndResumesCleanly()
        {
            var arena = CreateArena(40, 4, Enumerable.Empty<(int, int)>());
            var mover = RegisterMover(arena, arena.Center(0, 1), movementMax: 100, speed: 5f);
            var action = StartMove(arena, mover, arena.Center(30, 1));

            for (var i = 0; i < 3; i++)
            {
                action.Tick(Time.deltaTime);
                yield return null;
            }

            Assert.IsFalse(action.Outcome.HasValue, "The move should still be under way before pausing.");
            Time.timeScale = 0f;
            yield return null; // let the paused timescale take effect before sampling
            var pausedAt = mover.Transform.position;
            var pausedSpent = MovementSpent(mover, 100);

            for (var i = 0; i < 5; i++)
            {
                action.Tick(Time.deltaTime); // exactly what ActionScheduler passes: 0 while paused
                yield return null;
            }

            Assert.AreEqual(pausedAt, mover.Transform.position, "Nothing may move while Time.timeScale is 0.");
            Assert.AreEqual(pausedSpent, MovementSpent(mover, 100));
            Assert.IsFalse(action.Outcome.HasValue);

            Time.timeScale = 1f;
            yield return WaitForActionToEnd(action);
            Assert.AreEqual(ActionOutcome.Succeeded, action.Outcome);
            Assert.IsTrue(action.ArrivedAtDestination);
            Assert.AreEqual(30, MovementSpent(mover, 100));
        }
    }

    // ----------------------------------------------------------------------------------------------------
    // Test-only minimal grid backend (Task 1.1). NOT production code: deliberately small, exists only so the
    // contract above can be settled before GridPathfindingService/GridCharacterMotor (Tasks 1.2/1.3) exist.
    // ----------------------------------------------------------------------------------------------------

    internal sealed class FakeGrid
    {
        public Vector3 Origin { get; }
        private readonly int width;
        private readonly int depth;
        private readonly bool[,] blocked;

        public FakeGrid(Vector3 origin, int width, int depth, IEnumerable<(int x, int z)> blockedCells)
        {
            Origin = origin;
            this.width = width;
            this.depth = depth;
            blocked = new bool[width, depth];
            foreach (var (x, z) in blockedCells) blocked[x, z] = true;
        }

        public (int x, int z) ToCell(Vector3 world) =>
            (Mathf.FloorToInt(world.x - Origin.x), Mathf.FloorToInt(world.z - Origin.z));

        public Vector3 Center((int x, int z) cell) => new(Origin.x + cell.x + 0.5f, Origin.y, Origin.z + cell.z + 0.5f);

        public bool IsWalkable((int x, int z) cell) =>
            cell.x >= 0 && cell.x < width && cell.z >= 0 && cell.z < depth && !blocked[cell.x, cell.z];

        public bool IsWalkableWorld(Vector3 world) => IsWalkable(ToCell(world));

        /// <summary>8-connected, true Euclidean step cost, no corner cutting (a diagonal is only legal when both
        /// orthogonal neighbours are walkable). Returns null if unreachable.</summary>
        public List<(int x, int z)> FindPath((int x, int z) start, (int x, int z) goal)
        {
            if (!IsWalkable(start) || !IsWalkable(goal)) return null;

            var dist = new Dictionary<(int x, int z), float> { [start] = 0f };
            var prev = new Dictionary<(int x, int z), (int x, int z)>();
            var open = new List<(int x, int z)> { start };
            var closed = new HashSet<(int x, int z)>();

            while (open.Count > 0)
            {
                var best = 0;
                for (var i = 1; i < open.Count; i++) if (dist[open[i]] < dist[open[best]]) best = i;
                var cur = open[best];
                open.RemoveAt(best);
                if (!closed.Add(cur)) continue;
                if (cur == goal) break;

                for (var dx = -1; dx <= 1; dx++)
                {
                    for (var dz = -1; dz <= 1; dz++)
                    {
                        if (dx == 0 && dz == 0) continue;
                        var next = (x: cur.x + dx, z: cur.z + dz);
                        if (!IsWalkable(next) || closed.Contains(next)) continue;
                        var diagonal = dx != 0 && dz != 0;
                        if (diagonal && (!IsWalkable((cur.x + dx, cur.z)) || !IsWalkable((cur.x, cur.z + dz)))) continue;

                        var cost = dist[cur] + (diagonal ? 1.41421356f : 1f);
                        if (dist.TryGetValue(next, out var known) && cost >= known) continue;
                        dist[next] = cost;
                        prev[next] = cur;
                        open.Add(next);
                    }
                }
            }

            if (!dist.ContainsKey(goal)) return null;

            var path = new List<(int x, int z)> { goal };
            while (path[path.Count - 1] != start) path.Add(prev[path[path.Count - 1]]);
            path.Reverse();
            return path;
        }
    }

    internal sealed class FakeGridNavService : INavMeshService
    {
        private readonly FakeGrid grid;
        public FakeGridNavService(FakeGrid grid) => this.grid = grid;

        public bool TrySamplePosition(Vector3 point, out Vector3 sampledPosition, float maxDistance = 5f, int areaMask = NavMesh.AllAreas)
        {
            var cell = grid.ToCell(point);
            if (grid.IsWalkable(cell))
            {
                sampledPosition = grid.Center(cell);
                return true;
            }

            var found = false;
            var bestDistance = float.MaxValue;
            sampledPosition = point;
            var reach = Mathf.CeilToInt(maxDistance);
            for (var dx = -reach; dx <= reach; dx++)
            {
                for (var dz = -reach; dz <= reach; dz++)
                {
                    var candidate = (x: cell.x + dx, z: cell.z + dz);
                    if (!grid.IsWalkable(candidate)) continue;
                    var distance = Vector3.Distance(point, grid.Center(candidate));
                    if (distance > maxDistance || distance >= bestDistance) continue;
                    bestDistance = distance;
                    sampledPosition = grid.Center(candidate);
                    found = true;
                }
            }

            return found;
        }

        /// <summary>corners[0] is the caller's exact from; corners[1..] are cell centres, ending at the
        /// destination cell's centre.</summary>
        public bool TryCalculatePath(Vector3 from, Vector3 to, out Vector3[] corners, int areaMask = NavMesh.AllAreas)
        {
            corners = null;
            var start = grid.ToCell(from);
            var goal = grid.ToCell(to);
            var path = grid.FindPath(start, goal);
            if (path == null) return false;

            var flatFrom = new Vector3(from.x, grid.Origin.y, from.z);
            var list = new List<Vector3> { flatFrom };
            if (path.Count == 1)
            {
                var center = grid.Center(goal);
                if (Vector3.Distance(flatFrom, center) > 1e-4f) list.Add(center);
            }
            else
            {
                for (var i = 1; i < path.Count; i++) list.Add(grid.Center(path[i]));
            }

            corners = list.ToArray();
            return true;
        }

        public bool TryCalculatePathLength(Vector3 from, Vector3 to, out float length, int areaMask = NavMesh.AllAreas)
        {
            length = 0f;
            if (!TryCalculatePath(from, to, out var corners, areaMask)) return false;
            for (var i = 1; i < corners.Length; i++) length += Vector3.Distance(corners[i - 1], corners[i]);
            return true;
        }
    }

    /// <summary>Minimal grid mover. <see cref="ArrivalOvershoot"/> = 0 is the contract (snap EXACTLY onto each
    /// corner); a positive value simulates a sloppy "stop close enough" motor for the negative control.</summary>
    internal sealed class FakeGridMotor : MonoBehaviour, IMovable
    {
        public INavMeshService Nav;
        public Func<Vector3, bool> IsWalkable;
        public float GroundY;
        public float Speed = 20f;
        public float ArrivalOvershoot;

        private readonly Queue<Vector3> corners = new();
        private MoveRequestId currentId = MoveRequestId.None;
        private bool cancelled;
        private Vector3 velocity;

        public MoveResult SetDestination(Vector3 destination)
        {
            if (!Nav.TryCalculatePath(transform.position, destination, out var path)) return MoveResult.Invalid();

            // Drop the leading "from", and make the final corner the exact requested point (not its cell centre).
            var planned = path.Skip(1).ToList();
            var exact = new Vector3(destination.x, GroundY, destination.z);
            if (planned.Count > 0) planned[planned.Count - 1] = exact;
            else planned.Add(exact);

            corners.Clear();
            foreach (var corner in planned) corners.Enqueue(corner);
            currentId = MoveRequestId.NewId();
            cancelled = false;
            return new MoveResult(MoveStatus.Pending, currentId);
        }

        public void Stop()
        {
            corners.Clear();
            velocity = Vector3.zero;
            if (currentId.IsValid) cancelled = true;
        }

        public bool Warp(Vector3 position)
        {
            if (!IsWalkable(position)) return false;

            transform.position = new Vector3(position.x, GroundY, position.z);
            corners.Clear();
            velocity = Vector3.zero;
            if (currentId.IsValid) cancelled = true;
            return true;
        }

        public MotionSnapshot Snapshot
        {
            get
            {
                if (!currentId.IsValid) return MotionSnapshot.None;
                var onGrid = IsWalkable(transform.position);
                if (cancelled) return new MotionSnapshot(Vector3.zero, Vector3.zero, onGrid, 0f, MoveStatus.Cancelled, currentId);
                if (corners.Count == 0) return new MotionSnapshot(Vector3.zero, Vector3.zero, onGrid, 0f, MoveStatus.Arrived, currentId);
                return new MotionSnapshot(velocity, velocity, onGrid, RemainingDistance(), MoveStatus.Moving, currentId);
            }
        }

        private float RemainingDistance()
        {
            var total = 0f;
            var from = transform.position;
            foreach (var corner in corners)
            {
                total += Vector3.Distance(from, corner);
                from = corner;
            }

            return total;
        }

        private void Update()
        {
            velocity = Vector3.zero;
            if (Time.deltaTime <= 0f || cancelled || corners.Count == 0) return; // paused / idle: a true no-op

            var budget = Speed * Time.deltaTime; // scaled time only
            while (corners.Count > 0)
            {
                var target = corners.Peek();
                var toTarget = target - transform.position;
                toTarget.y = 0f;
                var distance = toTarget.magnitude;
                var direction = distance > 0f ? toTarget / distance : Vector3.zero;

                if (distance <= budget)
                {
                    transform.position = ArrivalOvershoot > 0f ? target + direction * ArrivalOvershoot : target;
                    budget -= distance;
                    corners.Dequeue();
                    velocity = direction * Speed;
                    continue;
                }

                transform.position += direction * budget;
                velocity = direction * Speed;
                break;
            }
        }
    }

    public class FakeGridMotorContractTests : GridMotorContractTests
    {
        protected override Arena CreateArena(int width, int depth, IEnumerable<(int x, int z)> blocked)
        {
            var grid = new FakeGrid(ArenaOrigin, width, depth, blocked);
            return new Arena
            {
                Nav = new FakeGridNavService(grid),
                IsWalkable = grid.IsWalkableWorld,
                Center = (x, z) => grid.Center((x, z)),
            };
        }

        protected override GameObject CreateMover(Arena arena, Vector3 start, float speed)
        {
            var go = new GameObject("FakeGridMover");
            go.transform.position = start;
            var motor = go.AddComponent<FakeGridMotor>();
            motor.Nav = arena.Nav;
            motor.IsWalkable = arena.IsWalkable;
            motor.GroundY = ArenaOrigin.y;
            motor.Speed = speed;
            return go;
        }

        [UnityTest]
        public IEnumerator NegativeControl_ASloppyOvershootingMotorMakesAStraightNCellMoveSpendNPlusOne()
        {
            // Proves the exact-arrival-snap requirement is load-bearing, not theoretical: a motor that merely
            // stops "close enough" (here 0.001 past each corner) pushes MoveActionExecution's cumulative
            // Mathf.CeilToInt over the integer boundary on every step. If this ever stops failing the way it
            // is asserted to, the exactness tests above have lost their teeth.
            var arena = CreateArena(12, 4, System.Linq.Enumerable.Empty<(int, int)>());
            var mover = RegisterMover(arena, arena.Center(0, 1), movementMax: 10);
            mover.Transform.GetComponent<FakeGridMotor>().ArrivalOvershoot = 0.001f;

            var action = StartMove(arena, mover, arena.Center(5, 1));
            yield return WaitForActionToEnd(action);

            Assert.AreEqual(ActionOutcome.Succeeded, action.Outcome);
            Assert.AreEqual(6, MovementSpent(mover, 10), "5 cells with a 0.001 overshoot per corner must spend 6 (the failure mode the exact-snap contract prevents).");
        }
    }

    /// <summary>Task 1.2: the identical contract, but the pathfinder is the REAL <c>GridPathfindingService</c> over a
    /// real <c>WalkabilityGrid</c>. The motor is still the test-only fake (the real <c>GridCharacterMotor</c> is
    /// Task 1.3, which adds a third subclass), so this proves the production pathfinder satisfies everything
    /// <c>MoveActionExecution</c> and the raw <c>IMovable</c> callers need from an <c>INavMeshService</c>.</summary>
    public class RealGridPathfinderContractTests : GridMotorContractTests
    {
        protected override Arena CreateArena(int width, int depth, IEnumerable<(int x, int z)> blocked)
        {
            var grid = new CRPG.Navigation.Grid.WalkabilityGrid(
                ArenaOrigin, 1f, width, depth, blocked.Select(b => new CRPG.Navigation.Grid.GridCoordinate(b.x, b.z)));
            var service = new CRPG.Navigation.Grid.GridPathfindingService();
            service.SetActiveGrid(grid);
            return new Arena
            {
                Nav = service,
                IsWalkable = grid.IsWalkableWorld,
                Center = (x, z) => grid.ToWorld(new CRPG.Navigation.Grid.GridCoordinate(x, z)),
            };
        }

        protected override GameObject CreateMover(Arena arena, Vector3 start, float speed)
        {
            var go = new GameObject("FakeMotorOverRealPathfinder");
            go.transform.position = start;
            var motor = go.AddComponent<FakeGridMotor>();
            motor.Nav = arena.Nav;
            motor.IsWalkable = arena.IsWalkable;
            motor.GroundY = ArenaOrigin.y;
            motor.Speed = speed;
            return go;
        }
    }
}
