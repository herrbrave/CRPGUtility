using System.Collections;
using System.Collections.Generic;
using CRPG.Core;
using CRPG.Gameplay;
using CRPG.Navigation;
using NUnit.Framework;
using Unity.AI.Navigation;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.TestTools;

namespace CRPG.Tests.PlayMode
{
    /// <summary>
    /// Task 2.1 (Production Foundation): structured movement outcomes (Docs/Design/Production-Foundation.md
    /// sec.4.3). Deliberately exercised against a REAL baked NavMesh with real obstacles/disconnected
    /// geometry, not a mocked straight-line INavMeshService fake -- the plan explicitly calls this out
    /// ("Verify against a real baked path with an obstacle, not only a mocked straight line"), and a mock
    /// can't distinguish a genuine partial path from a fabricated one.
    /// </summary>
    public class CharacterMotionTests
    {
        private readonly List<GameObject> spawned = new();

        [TearDown]
        public void TearDown()
        {
            foreach (var go in spawned)
            {
                if (go != null) UnityEngine.Object.DestroyImmediate(go);
            }

            spawned.Clear();

            // NavMesh state is process-global (not scoped to a scene reload between tests here), so each
            // test's bake must not bleed into the next.
            NavMesh.RemoveAllNavMeshData();
        }

        private T Track<T>(T component) where T : Component
        {
            spawned.Add(component.gameObject);
            return component;
        }

        private GameObject Track(GameObject go)
        {
            spawned.Add(go);
            return go;
        }

        private static GameObject CreateGroundPlane(Vector3 center, float halfSize)
        {
            var plane = GameObject.CreatePrimitive(PrimitiveType.Plane);
            plane.transform.position = center;
            plane.transform.localScale = new Vector3(halfSize / 5f, 1f, halfSize / 5f); // a default Plane is 10x10 world units
            plane.isStatic = true;
            return plane;
        }

        private static GameObject CreateWallObstacle(Vector3 center, Vector3 size)
        {
            var wall = GameObject.CreatePrimitive(PrimitiveType.Cube);
            wall.transform.position = center;
            wall.transform.localScale = size;
            wall.isStatic = true;
            return wall;
        }

        // Baking via a NavMeshSurface component's own BuildNavMesh(), not the classic static
        // UnityEditor.AI.NavMeshBuilder API -- confirmed in this project that the classic API silently no-ops
        // once com.unity.ai.navigation is installed (see project verification notes).
        private void BakeNavMesh()
        {
            var surface = Track(new GameObject("NavMeshSurface")).AddComponent<NavMeshSurface>();
            surface.BuildNavMesh();
        }

        private NavMeshCharacterMotor CreateMotorAt(Vector3 position, float speed = 8f)
        {
            var go = Track(new GameObject("TestMover"));
            go.transform.position = position;
            var agent = go.AddComponent<NavMeshAgent>();
            agent.speed = speed;
            agent.acceleration = 40f; // fast ramp-up so tests don't spend real wall-clock time accelerating
            var motor = go.AddComponent<NavMeshCharacterMotor>();
            agent.Warp(position); // explicit placement onto the baked mesh, not just an on-Awake auto-snap
            return motor;
        }

        // Bounded polling, not an unconditional yield count: real Time.deltaTime progresses during PlayMode
        // tests, so this waits only as long as actually needed (typically well under a second of simulated
        // time for these small distances) while still failing loudly instead of hanging if something never
        // resolves. Budgeted by accumulated Time.deltaTime, not a raw frame count: headless -batchmode ticks
        // frames far faster than realtime (confirmed empirically -- an entire 48-test PlayMode run completed
        // in well under a second of wall-clock time), so a frame-count budget under-estimates how much
        // simulated time actually elapses and can time out even while movement is proceeding normally.
        private static IEnumerator WaitForTerminalStatus(NavMeshCharacterMotor motor, System.Action<MoveStatus> onTerminal, float maxSimulatedSeconds = 15f)
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

            Assert.Fail($"Move request never reached a terminal status within {maxSimulatedSeconds} simulated seconds (last status: {motor.Snapshot.Status}).");
        }

        [UnityTest]
        public IEnumerator SetDestinationWithNoBakedNavMeshReturnsInvalid()
        {
            // No BakeNavMesh() call at all -- the mover has nothing to place itself on.
            var go = Track(new GameObject("OffMeshMover"));
            var agent = go.AddComponent<NavMeshAgent>();
            var motor = go.AddComponent<NavMeshCharacterMotor>();

            var result = motor.SetDestination(new Vector3(5f, 0f, 0f));

            Assert.AreEqual(MoveStatus.Invalid, result.Status);
            Assert.AreEqual(MoveRequestId.None, result.RequestId);
            Assert.AreEqual(MoveStatus.Invalid, motor.Snapshot.Status);
            yield break;
        }

        // The Warp tests below use a unique far-away origin with TRACKED geometry: this class's older plane/
        // wall helpers never track what they spawn, so geometry from earlier tests leaks into later bakes
        // (a leaked wall once made a probe point unwalkable and would let an "off-mesh" assertion pass by
        // accident). The mesh edge is located by raycast rather than assumed from the plane's size.
        private static readonly Vector3 WarpOrigin = new(6000f, 0f, 6000f);

        private void BuildWarpArena()
        {
            Track(CreateGroundPlane(WarpOrigin, 5f));
            BakeNavMesh();
        }

        private static float MeshEdgeXAlongPositiveXFrom(Vector3 from)
        {
            Assert.IsTrue(NavMesh.Raycast(from, from + new Vector3(20f, 0f, 0f), out var hit, NavMesh.AllAreas), "Expected the ray to hit the mesh edge.");
            return hit.position.x;
        }

        [UnityTest]
        public IEnumerator WarpToAnOnMeshPointRepositionsAndCancelsTheInFlightRequest()
        {
            BuildWarpArena();

            var motor = CreateMotorAt(WarpOrigin + new Vector3(-2f, 0f, -2f));
            var inFlight = motor.SetDestination(WarpOrigin + new Vector3(2f, 0f, 2f));
            Assert.IsTrue(inFlight.RequestId.IsValid);

            var target = WarpOrigin + new Vector3(0f, 0f, 1f);
            Assert.IsTrue(motor.Warp(target));

            Assert.Less(Vector3.Distance(motor.transform.position, target), 0.5f);
            Assert.AreEqual(MoveStatus.Cancelled, motor.Snapshot.Status);
            Assert.AreEqual(inFlight.RequestId, motor.Snapshot.RequestId); // RequestId retained, matching Stop()'s contract
            yield break;
        }

        [UnityTest]
        public IEnumerator WarpToAnOffMeshPointIsRefusedWithoutMovingTheAgentOrLosingItsRequest()
        {
            BuildWarpArena();

            var motor = CreateMotorAt(WarpOrigin);
            var agent = motor.GetComponent<NavMeshAgent>();
            var inFlight = motor.SetDestination(WarpOrigin + new Vector3(2f, 0f, 2f));
            var positionBefore = motor.transform.position;

            // Raw NavMeshAgent.Warp would move the transform here, strand the agent off-mesh, and destroy its
            // path -- Warp must refuse before ever touching the agent.
            Assert.IsFalse(motor.Warp(WarpOrigin + new Vector3(500f, 0f, 500f)));

            Assert.AreEqual(positionBefore, motor.transform.position);
            Assert.IsTrue(agent.isOnNavMesh);
            Assert.AreNotEqual(MoveStatus.Cancelled, motor.Snapshot.Status, "A refused Warp must not cancel the request in flight.");
            Assert.AreEqual(inFlight.RequestId, motor.Snapshot.RequestId);

            // ...and the untouched in-flight move still completes.
            MoveStatus? terminal = null;
            yield return WaitForTerminalStatus(motor, status => terminal = status);
            Assert.AreEqual(MoveStatus.Arrived, terminal);
        }

        [UnityTest]
        public IEnumerator WarpToAPointNearTheMeshSnapsOntoTheMeshImmediatelyNotOnTheNextFrame()
        {
            BuildWarpArena();

            var motor = CreateMotorAt(WarpOrigin);
            var edgeX = MeshEdgeXAlongPositiveXFrom(WarpOrigin);
            var nearButOffMesh = new Vector3(edgeX + 0.5f, 0f, WarpOrigin.z); // past the baked edge, within warpSampleMaxDistance

            Assert.IsTrue(motor.Warp(nearButOffMesh), $"meshEdgeX={edgeX}");

            // Raw NavMeshAgent.Warp would leave the transform at the raw point for this frame and only snap it
            // to the mesh on the next Update, letting several nearby slots silently collapse onto one edge point.
            var immediately = motor.transform.position;
            Assert.Less(immediately.x, edgeX + 0.25f, "Warp must land on the sampled mesh point right away.");
            yield return null;
            yield return null;
            Assert.Less(Vector3.Distance(immediately, motor.transform.position), 0.05f, "The position must be stable, not corrected later.");
        }

        [UnityTest]
        public IEnumerator WarpBeyondTheSampleToleranceIsRefusedForASmallRadiusAgentInsteadOfStrandingIt()
        {
            BuildWarpArena();

            var motor = CreateMotorAt(WarpOrigin);
            var agent = motor.GetComponent<NavMeshAgent>();
            // Raw Warp's tolerance is ~20x radius, so a small-radius agent accepts points far enough away to
            // fail (and strand) even though a 5-unit SamplePosition validation would pass them.
            agent.radius = 0.15f;
            var edgeX = MeshEdgeXAlongPositiveXFrom(WarpOrigin);
            var positionBefore = motor.transform.position;

            Assert.IsFalse(motor.Warp(new Vector3(edgeX + 3f, 0f, WarpOrigin.z)));

            Assert.AreEqual(positionBefore, motor.transform.position);
            Assert.IsTrue(agent.isOnNavMesh, "A refused Warp must leave the agent on the mesh, still able to move.");
            Assert.AreEqual(MoveStatus.Pending, motor.SetDestination(WarpOrigin + new Vector3(1f, 0f, 1f)).Status);
            yield break;
        }

        [UnityTest]
        public IEnumerator SetDestinationRoutesAroundARealObstacleAndArrives()
        {
            CreateGroundPlane(Vector3.zero, 20f);
            // A wall spanning most of the ground between start and destination, with a gap only at one end --
            // a straight line from start to destination is blocked; a real path must detour around it.
            CreateWallObstacle(new Vector3(0f, 1f, 0f), new Vector3(16f, 2f, 1f));
            BakeNavMesh();

            var motor = CreateMotorAt(new Vector3(-8f, 0f, -5f));
            var destination = new Vector3(8f, 0f, 5f);

            var result = motor.SetDestination(destination);
            Assert.AreEqual(MoveStatus.Pending, result.Status);
            Assert.IsTrue(result.RequestId.IsValid);

            MoveStatus? terminal = null;
            yield return WaitForTerminalStatus(motor, status => terminal = status);

            Assert.AreEqual(MoveStatus.Arrived, terminal, "A real path around the wall must reach the destination, not just get close on the blocked straight line.");
            Assert.AreEqual(result.RequestId, motor.Snapshot.RequestId);
        }

        [UnityTest]
        public IEnumerator SetDestinationOnADisconnectedNavMeshIslandReportsBlockedNotArrived()
        {
            CreateGroundPlane(new Vector3(0f, 0f, 0f), 10f);
            // A second island far enough away that the bake produces two disconnected NavMesh regions --
            // nothing physically "walls off" the destination, but it is genuinely unreachable on the baked
            // mesh, exactly the "partial path" case design sec.4.3 says must never read as Arrived.
            CreateGroundPlane(new Vector3(60f, 0f, 0f), 10f);
            BakeNavMesh();

            var motor = CreateMotorAt(new Vector3(0f, 0f, 0f));
            var unreachableDestination = new Vector3(60f, 0f, 0f);

            var result = motor.SetDestination(unreachableDestination);
            Assert.AreEqual(MoveStatus.Pending, result.Status);

            MoveStatus? terminal = null;
            yield return WaitForTerminalStatus(motor, status => terminal = status);

            Assert.AreEqual(MoveStatus.Blocked, terminal, "A partial path to a disconnected island must never be reported as Arrived.");
        }

        [UnityTest]
        public IEnumerator StopCancelsTheCurrentRequestWithTheSameRequestId()
        {
            CreateGroundPlane(Vector3.zero, 20f);
            BakeNavMesh();

            var motor = CreateMotorAt(new Vector3(-8f, 0f, 0f), speed: 1f); // slow, so it's still mid-path when Stop() is called
            var result = motor.SetDestination(new Vector3(8f, 0f, 0f));

            yield return null; // let pathPending resolve so the agent is actually moving
            yield return null;

            motor.Stop();

            Assert.AreEqual(MoveStatus.Cancelled, motor.Snapshot.Status);
            Assert.AreEqual(result.RequestId, motor.Snapshot.RequestId, "Cancelling must report against the SAME request that was cancelled.");
        }

        [UnityTest]
        public IEnumerator RetargetingIssuesANewRequestIdAndTheOldOneNoLongerReflectsCurrentState()
        {
            CreateGroundPlane(Vector3.zero, 20f);
            BakeNavMesh();

            var motor = CreateMotorAt(new Vector3(-8f, 0f, 0f));
            var first = motor.SetDestination(new Vector3(0f, 0f, 0f));
            yield return null;

            var second = motor.SetDestination(new Vector3(8f, 0f, 0f));

            Assert.AreNotEqual(first.RequestId, second.RequestId, "Retargeting must issue a fresh request id, not reuse the superseded one.");
            Assert.AreEqual(second.RequestId, motor.Snapshot.RequestId, "Snapshot must reflect only the newest request.");

            MoveStatus? terminal = null;
            yield return WaitForTerminalStatus(motor, status => terminal = status);
            Assert.AreEqual(MoveStatus.Arrived, terminal);
            Assert.AreEqual(second.RequestId, motor.Snapshot.RequestId, "A stale completion for the superseded first request must never appear as the current one.");
        }

        [UnityTest]
        public IEnumerator AnInvalidRequestLeavesThePreviousRequestUntouched()
        {
            CreateGroundPlane(Vector3.zero, 20f);
            BakeNavMesh();

            var motor = CreateMotorAt(new Vector3(-8f, 0f, 0f), speed: 1f);
            var goodResult = motor.SetDestination(new Vector3(8f, 0f, 0f));
            yield return null;

            var agent = motor.GetComponent<NavMeshAgent>();
            agent.enabled = false; // simulate the mover becoming unable to accept a new order mid-flight

            var badResult = motor.SetDestination(new Vector3(0f, 0f, 5f));

            Assert.AreEqual(MoveStatus.Invalid, badResult.Status);
            Assert.AreEqual(MoveRequestId.None, badResult.RequestId);
            Assert.AreEqual(goodResult.RequestId, motor.Snapshot.RequestId, "A rejected request must not disturb the request that was already in flight.");
        }
    }

    public class FacingControllerTests
    {
        private GameObject go;

        [TearDown]
        public void TearDown()
        {
            if (go != null) UnityEngine.Object.DestroyImmediate(go);
            NavMesh.RemoveAllNavMeshData();
        }

        [Test]
        public void AwakeDisablesTheNavMeshAgentsOwnRotation()
        {
            go = new GameObject("TestFacing");
            var agent = go.AddComponent<NavMeshAgent>();
            go.AddComponent<NavMeshCharacterMotor>();
            go.AddComponent<FacingController>();

            Assert.IsFalse(agent.updateRotation, "FacingController must be the sole writer of rotation -- ownership invariant #3.");
        }

        // Time-budgeted, not frame-count-budgeted -- see WaitForTerminalStatus's comment on
        // CharacterMotionTests for why a raw frame count under-counts simulated time in headless -batchmode.
        // do-while, not while: IsFacingComplete defaults to true until FacingController's own Update() has
        // actually run at least once against the newly-issued requests, so checking it before the first
        // yield would exit immediately without ever advancing the rotation.
        private static IEnumerator WaitForFacingComplete(FacingController facing, float maxSimulatedSeconds = 5f)
        {
            var elapsed = 0f;
            do
            {
                elapsed += Time.deltaTime;
                yield return null;
            } while (elapsed < maxSimulatedSeconds && !facing.IsFacingComplete);
        }

        [UnityTest]
        public IEnumerator TurnsTowardTheHighestPriorityRequestOverTime()
        {
            go = new GameObject("TestFacing");
            go.transform.rotation = Quaternion.identity;
            var facing = go.AddComponent<FacingController>();

            facing.RequestFacing(FacingPriority.Motion, Vector3.forward); // lower priority, should lose
            facing.RequestFacing(FacingPriority.CommittedAction, Vector3.right);

            yield return WaitForFacingComplete(facing);

            Assert.IsTrue(facing.IsFacingComplete);
            var forward = go.transform.forward;
            Assert.Greater(Vector3.Dot(forward, Vector3.right), 0.99f, "Must face the CommittedAction direction, not the lower-priority Motion one.");
        }

        [UnityTest]
        public IEnumerator ClearingTheHigherPriorityRequestFallsBackToTheNextOne()
        {
            go = new GameObject("TestFacing");
            var facing = go.AddComponent<FacingController>();

            // Motion is motor-owned and cleared when this fixture has no IMovable.
            // Use a persistent lower-priority request so this actually tests fallback.
            facing.RequestFacing(FacingPriority.Interaction, Vector3.forward);
            facing.RequestFacing(FacingPriority.CommittedAction, Vector3.right);
            yield return null;

            facing.ClearFacing(FacingPriority.CommittedAction);

            yield return WaitForFacingComplete(facing);

            var forward = go.transform.forward;
            Assert.Greater(Vector3.Dot(forward, Vector3.forward), 0.99f, "With CommittedAction cleared, Interaction must take over.");
        }
    }
}
