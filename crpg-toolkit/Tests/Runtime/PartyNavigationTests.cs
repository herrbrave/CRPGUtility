using System.Collections;
using System.Collections.Generic;
using CRPG.Core;
using CRPG.Exploration;
using CRPG.Gameplay;
using CRPG.Navigation;
using CRPG.Party;
using NUnit.Framework;
using Unity.AI.Navigation;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.TestTools;

namespace CRPG.Tests.PlayMode
{
    /// <summary>
    /// Task 3.2: PartyMovementService's practical-follower-destination retry and PartyExplorationController's
    /// truthful per-member outcome reporting, exercised against a REAL baked NavMesh (real obstacles, real
    /// disconnected geometry) -- mirrors CharacterMotionTests' own established convention of baking a real
    /// NavMesh per test rather than mocking INavMeshService, since a mock can't distinguish a genuine partial/
    /// disconnected path from a fabricated one.
    /// </summary>
    public class PartyNavigationTests
    {
        private readonly List<GameObject> spawned = new();

        [TearDown]
        public void TearDown()
        {
            foreach (var go in spawned) if (go != null) Object.DestroyImmediate(go);
            spawned.Clear();
            NavMesh.RemoveAllNavMeshData(); // process-global -- must not bleed into the next test
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
            plane.transform.localScale = new Vector3(halfSize / 5f, 1f, halfSize / 5f);
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

        private void BakeNavMesh() => Track(new GameObject("NavMeshSurface")).AddComponent<NavMeshSurface>().BuildNavMesh();

        private NavMeshCharacterMotor CreateMotorAt(RuntimeEntityId id, Vector3 position, FakeSelectableSource selectables, float speed = 8f)
        {
            var go = Track(new GameObject($"Member-{id}"));
            go.transform.position = position;
            var agent = go.AddComponent<NavMeshAgent>();
            agent.speed = speed;
            agent.acceleration = 40f;
            var motor = go.AddComponent<NavMeshCharacterMotor>();
            agent.Warp(position);
            selectables.Add(id, position);
            return motor;
        }

        private sealed class FakeSelectableSource : ISelectableSource
        {
            private readonly List<SelectableEntity> entities = new();
            public void Add(RuntimeEntityId id, Vector3 position) => entities.Add(new SelectableEntity(id, position));
            public IEnumerable<SelectableEntity> GetSelectables() => entities;
        }

        // Single-point formation -- every member gets exactly the requested point, so a test can place a
        // formation slot precisely without a real solver's spacing math complicating the geometry.
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
        public IEnumerator ALegitimateLongDetourAroundAWallStillResolvesExact()
        {
            CreateGroundPlane(Vector3.zero, 20f);
            // A wall spanning most of the ground with a gap only at one end -- straight line is blocked, but a
            // real (long) path around it exists. This is exactly the case a path-length-ratio heuristic would
            // wrongly reject; PartyMovementService must not use one.
            CreateWallObstacle(new Vector3(0f, 1f, 0f), new Vector3(16f, 2f, 1f));
            BakeNavMesh();

            var selectables = new FakeSelectableSource();
            var member = RuntimeEntityId.NewId();
            CreateMotorAt(member, new Vector3(-8f, 0f, -5f), selectables);
            var destination = new Vector3(8f, 0f, 5f); // on the far side of the wall -- a real detour is required

            var service = new PartyMovementService(new NavMeshService(), selectables);
            var order = new PartyMoveOrder(new[] { member }, destination, Vector3.forward, new FixedPointFormationSolver(destination));

            var resolved = service.ResolveDestinations(order);

            Assert.AreEqual(FormationResolutionOutcome.Exact, resolved[member].Outcome);
            yield break;
        }

        [UnityTest]
        public IEnumerator AFormationSlotOnADisconnectedIslandResolvesUnreachableWithoutFreezingOtherSelectedMembers()
        {
            CreateGroundPlane(new Vector3(0f, 0f, 0f), 10f);
            CreateGroundPlane(new Vector3(60f, 0f, 0f), 10f); // disconnected -- genuinely unreachable, not just far
            BakeNavMesh();

            var selectables = new FakeSelectableSource();
            var stuck = RuntimeEntityId.NewId();
            var free = RuntimeEntityId.NewId();
            var stuckMotor = CreateMotorAt(stuck, new Vector3(0f, 0f, 0f), selectables);
            var freeMotor = CreateMotorAt(free, new Vector3(0f, 0f, 2f), selectables);

            var party = new PartyState(new[] { stuck, free });
            var movementService = new PartyMovementService(new NavMeshService(), selectables);
            var commandSource = new RaisableCommandSource();
            var controller = new PartyExplorationController(party, movementService, new ClusterFormationSolver(), commandSource);
            controller.RegisterMotor(stuck, stuckMotor);
            controller.RegisterMotor(free, freeMotor);

            var outcomes = new Dictionary<RuntimeEntityId, MoveResult>();
            controller.MemberMoveOutcome += (id, result) => outcomes[id] = result;

            // "stuck" is sent toward the disconnected island; the whole order shares one destination, but
            // ClusterFormationSolver gives each member a slightly different slot near it -- both slots land on
            // the unreachable island since it's far smaller than the formation spacing.
            commandSource.RaiseMove(new MoveCommand(new Vector3(60f, 0f, 0f), Vector3.forward));
            yield return null;

            Assert.IsTrue(outcomes.ContainsKey(stuck));
            Assert.AreEqual(MoveStatus.Invalid, outcomes[stuck].Status, "An unreachable formation slot must report Invalid, never a synthesized Arrived.");

            // Now move "free" somewhere reachable on its own home island -- it must still work normally,
            // proving the earlier unreachable order for "stuck" never froze the rest of the group.
            party.ClearSelection();
            party.SelectMany(new[] { free });
            commandSource.RaiseMove(new MoveCommand(new Vector3(3f, 0f, 3f), Vector3.forward));
            yield return null;

            Assert.IsTrue(outcomes.ContainsKey(free));
            Assert.AreNotEqual(MoveStatus.Invalid, outcomes[free].Status);
        }

        [UnityTest]
        public IEnumerator AMoveOrderToASelectedSubsetNeverTouchesUnselectedMembersMotors()
        {
            CreateGroundPlane(Vector3.zero, 20f);
            BakeNavMesh();

            var selectables = new FakeSelectableSource();
            var selected = RuntimeEntityId.NewId();
            var unselected = RuntimeEntityId.NewId();
            var selectedMotor = CreateMotorAt(selected, new Vector3(-2f, 0f, 0f), selectables);
            var unselectedMotor = CreateMotorAt(unselected, new Vector3(2f, 0f, 0f), selectables);

            var party = new PartyState(new[] { selected, unselected }); // both selected by default
            party.SelectMany(new[] { selected }); // narrow to just one -- exercises PartyState's existing selection machinery
            var movementService = new PartyMovementService(new NavMeshService(), selectables);
            var commandSource = new RaisableCommandSource();
            var controller = new PartyExplorationController(party, movementService, new ClusterFormationSolver(), commandSource);
            controller.RegisterMotor(selected, selectedMotor);
            controller.RegisterMotor(unselected, unselectedMotor);

            commandSource.RaiseMove(new MoveCommand(new Vector3(5f, 0f, 5f), Vector3.forward));
            yield return null;

            Assert.IsTrue(selectedMotor.Snapshot.Status is MoveStatus.Pending or MoveStatus.Moving or MoveStatus.Arrived,
                "The selected member must have actually received the move request.");
            Assert.AreEqual(MoveRequestId.None, unselectedMotor.Snapshot.RequestId,
                "An unselected member must never receive a request at all -- still exactly its untouched default snapshot.");
        }

        private sealed class RaisableCommandSource : IPlayerCommandSource
        {
            public event System.Action<SelectCommand> Select;
            public event System.Action<MoveCommand> Move;
            public event System.Action<InteractCommand> Interact;
            public event System.Action Cancel;

            public void RaiseMove(MoveCommand command) => Move?.Invoke(command);
        }
    }
}
