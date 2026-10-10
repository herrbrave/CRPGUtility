using System;
using System.Collections;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using CRPG.Core;
using CRPG.Gameplay;
using CRPG.Gameplay.Ambient;
using CRPG.Gameplay.Interactions;
using CRPG.Navigation;
using NUnit.Framework;
using Unity.AI.Navigation;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.TestTools;

namespace CRPG.Tests.PlayMode
{
    /// <summary>The ambient routines on a real NavMesh, with real agents, motors and facing, plus the interaction
    /// hook that freezes an NPC while the party walks up to it. Time budgets use accumulated deltaTime, never raw
    /// frame counts (batch PlayMode ticks frames far faster than realtime).</summary>
    public class AmbientRoutineIntegrationTests
    {
        private readonly List<GameObject> spawned = new();
        private readonly List<UnityEngine.Object> assets = new();
        private GameSession session;

        [SetUp]
        public void SetUp()
        {
            session = new GameSession(new RulesDatabase(), new DiceService(new SeededRandomSource(1)));
        }

        [TearDown]
        public void TearDown()
        {
            foreach (var go in spawned) if (go != null) UnityEngine.Object.DestroyImmediate(go);
            spawned.Clear();
            foreach (var a in assets) if (a != null) UnityEngine.Object.DestroyImmediate(a);
            assets.Clear();
            session.Dispose();
            NavMesh.RemoveAllNavMeshData();
        }

        // ---------------------------------------------------------------- fixtures

        private void BakeGround(float halfSize = 20f)
        {
            var plane = GameObject.CreatePrimitive(PrimitiveType.Plane);
            plane.transform.localScale = new Vector3(halfSize / 5f, 1f, halfSize / 5f);
            plane.isStatic = true;
            spawned.Add(plane);

            var surface = new GameObject("NavMeshSurface").AddComponent<NavMeshSurface>();
            spawned.Add(surface.gameObject);
            surface.BuildNavMesh();
        }

        private Transform Marker(string name, Vector3 position, float yaw = 0f)
        {
            var go = new GameObject(name) { transform = { position = position, rotation = Quaternion.Euler(0f, yaw, 0f) } };
            spawned.Add(go);
            return go.transform;
        }

        private (GameObject go, CharacterAgent agent) CreateNpc(Vector3 position)
        {
            var go = new GameObject("RoutineNpc") { transform = { position = position } };
            var navAgent = go.AddComponent<NavMeshAgent>();
            navAgent.speed = 6f;
            navAgent.acceleration = 40f;
            go.AddComponent<NavMeshCharacterMotor>();
            go.AddComponent<FacingController>();
            var agent = go.AddComponent<CharacterAgent>();
            spawned.Add(go);
            navAgent.Warp(position);
            agent.Bind(RuntimeEntityId.NewId(), session);
            return (go, agent);
        }

        private static IEnumerator Simulate(float seconds, Func<bool> until = null)
        {
            var elapsed = 0f;
            do
            {
                elapsed += Time.deltaTime;
                yield return null;
                if (until != null && until()) yield break;
            }
            while (elapsed < seconds);
        }

        // ---------------------------------------------------------------- wander

        [UnityTest]
        public IEnumerator Wander_WalksAroundInsideItsArea_OnARealNavMesh()
        {
            BakeGround();
            var centre = Marker("centre", new Vector3(5f, 0f, 5f));
            var (go, agent) = CreateNpc(centre.position);
            var spec = new NpcRoutineSpec { Kind = NpcRoutineKind.Wander, AreaCentre = centre, AreaRadius = 4f, PauseMin = 0.2f, PauseMax = 0.5f };
            Assert.IsNotNull(NpcRoutines.Attach(agent, spec, centre, "walker"));

            var maxFromCentre = 0f;
            var travelled = 0f;
            var last = go.transform.position;
            var elapsed = 0f;
            while (elapsed < 40f)
            {
                elapsed += Time.deltaTime;
                yield return null;
                var p = go.transform.position;
                travelled += Vector3.Distance(p, last);
                last = p;
                maxFromCentre = Mathf.Max(maxFromCentre, Vector3.Distance(new Vector3(p.x, 0f, p.z), new Vector3(centre.position.x, 0f, centre.position.z)));
            }

            Assert.Greater(travelled, 5f, "the NPC never really walked anywhere");
            Assert.LessOrEqual(maxFromCentre, 4f + 1.5f, "left its wander area");
        }

        [UnityTest]
        public IEnumerator Wander_StandsStillWhileSuspended_AndMovesAgainOnResume()
        {
            BakeGround();
            var centre = Marker("centre", Vector3.zero);
            var (go, agent) = CreateNpc(Vector3.zero);
            NpcRoutines.Attach(agent, new NpcRoutineSpec { Kind = NpcRoutineKind.Wander, AreaCentre = centre, AreaRadius = 6f, PauseMin = 0.1f, PauseMax = 0.3f }, centre, "walker");

            var owner = new object();
            session.Ambient.Suspend(agent.EntityId, owner);
            yield return Simulate(1f);
            var parked = go.transform.position;
            yield return Simulate(6f);
            Assert.Less(Vector3.Distance(parked, go.transform.position), 0.05f, "moved while suspended");

            session.Ambient.Resume(agent.EntityId, owner);
            var moved = false;
            yield return Simulate(20f, () => moved = Vector3.Distance(parked, go.transform.position) > 1f);
            Assert.IsTrue(moved, "never resumed");
        }

        [UnityTest]
        public IEnumerator Wander_LowersItsAvoidancePriorityWhileActive_AndRestoresItWhenSuspended()
        {
            BakeGround();
            var (go, agent) = CreateNpc(Vector3.zero);
            var navAgent = go.GetComponent<NavMeshAgent>();
            navAgent.avoidancePriority = 50;
            NpcRoutines.Attach(agent, new NpcRoutineSpec { Kind = NpcRoutineKind.Wander, AreaRadius = 5f }, go.transform, "walker");

            yield return Simulate(0.3f);
            Assert.AreEqual(NpcRoutineRunner.RoutineAvoidancePriority, navAgent.avoidancePriority);

            session.Ambient.Suspend(agent.EntityId, this);
            yield return Simulate(0.3f);
            Assert.AreEqual(50, navAgent.avoidancePriority);
        }

        // ---------------------------------------------------------------- work loop

        [UnityTest]
        public IEnumerator WorkLoop_WalksBetweenItsStops_AndFacesTheStopWhileWorking()
        {
            BakeGround();
            var a = Marker("stop-a", new Vector3(-8f, 0f, 0f), yaw: 0f);
            var b = Marker("stop-b", new Vector3(8f, 0f, 0f), yaw: 180f); // forward = -Z
            var (go, agent) = CreateNpc(Vector3.zero);
            var spec = new NpcRoutineSpec
            {
                Kind = NpcRoutineKind.WorkLoop,
                Stops =
                {
                    new WorkStop { Point = a, Action = WorkStopAction.Work, Seconds = 1.5f },
                    new WorkStop { Point = b, Action = WorkStopAction.Work, Seconds = 1.5f },
                },
            };
            Assert.IsNotNull(NpcRoutines.Attach(agent, spec, go.transform, "worker"));

            var visitedA = false;
            var visitedB = false;
            var facedStopB = false;
            var elapsed = 0f;
            while (elapsed < 60f && !(visitedA && visitedB && facedStopB))
            {
                elapsed += Time.deltaTime;
                yield return null;
                var p = go.transform.position;
                visitedA |= Vector3.Distance(p, a.position) < 0.8f;
                if (Vector3.Distance(p, b.position) < 0.8f)
                {
                    visitedB = true;
                    facedStopB |= go.transform.forward.z < -0.9f;
                }
            }

            Assert.IsTrue(visitedA, "never reached stop A");
            Assert.IsTrue(visitedB, "never reached stop B");
            Assert.IsTrue(facedStopB, "did not turn to the stop's facing while working");
        }

        [UnityTest]
        public IEnumerator WorkLoop_ReleasesItsFacingBetweenStops_SoItWalksForwardNotSideways()
        {
            BakeGround();
            var a = Marker("stop-a", new Vector3(10f, 0f, 0f), yaw: 180f); // arrives heading +X, works facing -Z
            var b = Marker("stop-b", new Vector3(-10f, 0f, 0f), yaw: 180f);
            var (go, agent) = CreateNpc(Vector3.zero);
            NpcRoutines.Attach(agent, new NpcRoutineSpec
            {
                Kind = NpcRoutineKind.WorkLoop,
                Stops =
                {
                    new WorkStop { Point = a, Seconds = 0.5f },
                    new WorkStop { Point = b, Seconds = 0.5f },
                },
            }, go.transform, "worker");

            // After the first stop it must head -X and turn to face its direction of travel, which the Ambient
            // request would block if it were left standing.
            var turnedToTravel = false;
            var reachedA = false;
            var elapsed = 0f;
            while (elapsed < 60f && !turnedToTravel)
            {
                elapsed += Time.deltaTime;
                yield return null;
                reachedA |= Vector3.Distance(go.transform.position, a.position) < 0.8f;
                if (reachedA && go.transform.position.x < 4f) turnedToTravel = go.transform.forward.x < -0.7f;
            }

            Assert.IsTrue(turnedToTravel, "stayed turned toward the old work spot while walking away");
        }

        [UnityTest]
        public IEnumerator WorkLoop_APropOnAModelWithoutHands_WarnsAndTheLoopCarriesOn()
        {
            BakeGround();
            var prop = ScriptableObject.CreateInstance<CarriedPropDefinition>();
            prop.Configure(new GameObject("crate-prefab"), HumanBodyBones.RightHand, Vector3.zero, Vector3.zero);
            spawned.Add(prop.Prefab);
            assets.Add(prop);

            var a = Marker("pile-a", new Vector3(-6f, 0f, 0f));
            var b = Marker("pile-b", new Vector3(6f, 0f, 0f));
            var (go, agent) = CreateNpc(Vector3.zero);
            NpcRoutines.Attach(agent, new NpcRoutineSpec
            {
                Kind = NpcRoutineKind.WorkLoop,
                Stops =
                {
                    new WorkStop { Point = a, Action = WorkStopAction.PickUp, Seconds = 0.5f, Prop = prop },
                    new WorkStop { Point = b, Action = WorkStopAction.Drop, Seconds = 0.5f },
                },
            }, go.transform, "carrier");

            LogAssert.Expect(LogType.Warning, new Regex("cannot carry"));
            var visitedB = false;
            var elapsed = 0f;
            while (elapsed < 60f && !visitedB)
            {
                elapsed += Time.deltaTime;
                yield return null;
                visitedB = Vector3.Distance(go.transform.position, b.position) < 0.8f;
            }

            Assert.IsTrue(visitedB, "the loop stalled after the prop could not be attached");
        }

        // ---------------------------------------------------------------- interaction hook

        private sealed class FakeCommandSource : IPlayerCommandSource
        {
            public event Action<SelectCommand> Select;
            public event Action<MoveCommand> Move;
            public event Action<InteractCommand> Interact;
            public event Action Cancel;

            public void RaiseInteract(RuntimeEntityId target) => Interact?.Invoke(new InteractCommand(target));
            public void RaiseCancel() => Cancel?.Invoke();
        }

        private sealed class FakeMovable : MonoBehaviour, IMovable
        {
            public bool Reject;
            private MoveRequestId id = MoveRequestId.None;

            public MoveResult SetDestination(Vector3 destination)
            {
                if (Reject) return MoveResult.Invalid();
                id = MoveRequestId.NewId();
                return new MoveResult(MoveStatus.Pending, id);
            }

            public void Stop() { }
            public bool Warp(Vector3 position) { transform.position = position; return true; }
            public MotionSnapshot Snapshot => new(Vector3.zero, Vector3.zero, true, 10f, MoveStatus.Moving, id);
        }

        private sealed class NoopAdapter : MonoBehaviour, IInteractionAdapter
        {
            public DefinitionId ActionId { get; } = new("noop");
            public bool IsAvailable(RuntimeEntityId performer) => true;
            public IInteractionExecution BeginExecute(RuntimeEntityId performer, Action<InteractionExecutionResult> onFinished) => null;
        }

        private (CharacterAgent npc, WorldInteractable interactable) CreateTalker(Vector3 position)
        {
            var anchor = Marker("talk-anchor", position);
            var go = new GameObject("Talker") { transform = { position = position } };
            go.SetActive(false);
            go.AddComponent<NoopAdapter>();
            var interactable = go.AddComponent<WorldInteractable>();
            interactable.Configure(anchor, 1.5f);
            var agent = go.AddComponent<CharacterAgent>();
            go.SetActive(true);
            spawned.Add(go);
            agent.Bind(RuntimeEntityId.NewId(), session);
            return (agent, interactable);
        }

        private (InteractionController controller, FakeCommandSource source, FakeMovable performer) CreateInteraction()
        {
            var go = new GameObject("Performer") { transform = { position = new Vector3(50f, 0f, 0f) } };
            var movable = go.AddComponent<FakeMovable>();
            var performer = go.AddComponent<CharacterAgent>();
            spawned.Add(go);
            var id = RuntimeEntityId.NewId();
            performer.Bind(id, session);
            session.Party.AddMember(id);

            var source = new FakeCommandSource();
            return (new InteractionController(source, session.Party, session), source, movable);
        }

        [Test]
        public void Interaction_SuspendsTheTargetNpcFromAcceptanceUntilTheRequestEnds()
        {
            var (npc, interactable) = CreateTalker(Vector3.zero);
            var (controller, source, _) = CreateInteraction();

            source.RaiseInteract(interactable.EntityId);
            Assert.IsNotNull(controller.CurrentRequest, "the approach should be under way");
            Assert.IsTrue(session.Ambient.IsSuspended(npc.EntityId), "the NPC must stop wandering during the approach");

            source.RaiseCancel();
            Assert.IsFalse(session.Ambient.IsSuspended(npc.EntityId), "and resume once the request ends");
            controller.Dispose();
        }

        [Test]
        public void Interaction_ARejectedApproach_NeverLeavesTheNpcFrozen()
        {
            var (npc, interactable) = CreateTalker(Vector3.zero);
            var (controller, source, performer) = CreateInteraction();
            performer.Reject = true;

            source.RaiseInteract(interactable.EntityId);

            Assert.IsNull(controller.CurrentRequest);
            Assert.IsFalse(session.Ambient.IsSuspended(npc.EntityId));
            controller.Dispose();
        }

        [Test]
        public void Interaction_ASecondRequestOnAnotherNpc_ReleasesTheFirstAndHoldsTheSecond()
        {
            var (first, firstTarget) = CreateTalker(Vector3.zero);
            var (second, secondTarget) = CreateTalker(new Vector3(5f, 0f, 0f));
            var (controller, source, _) = CreateInteraction();

            source.RaiseInteract(firstTarget.EntityId);
            source.RaiseInteract(secondTarget.EntityId);

            Assert.IsFalse(session.Ambient.IsSuspended(first.EntityId));
            Assert.IsTrue(session.Ambient.IsSuspended(second.EntityId));
            controller.Dispose();
            Assert.IsFalse(session.Ambient.IsSuspended(second.EntityId), "disposing the controller releases everything it held");
        }

        [Test]
        public void Interaction_AnInteractableThatIsNotAnNpc_IsUnaffected()
        {
            var anchor = Marker("chest-anchor", Vector3.zero);
            var go = new GameObject("Chest");
            go.SetActive(false);
            go.AddComponent<NoopAdapter>();
            var interactable = go.AddComponent<WorldInteractable>();
            interactable.Configure(anchor, 1.5f);
            go.SetActive(true);
            spawned.Add(go);
            var (controller, source, _) = CreateInteraction();

            Assert.DoesNotThrow(() => source.RaiseInteract(interactable.EntityId));
            controller.Dispose();
        }
    }
}
