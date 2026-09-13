using System;
using System.Collections;
using System.Collections.Generic;
using CRPG.Core;
using CRPG.Gameplay;
using CRPG.Gameplay.Interactions;
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
    /// Task 3.3 (Production Foundation): the shared interaction lifecycle (Docs/Design/Production-Foundation.md,
    /// "Interaction"). InteractionController.Tick takes an explicit deltaTime rather than reading Time.deltaTime
    /// itself, and most scenarios position the performer already within range so no approach is needed at all --
    /// both deliberate, so the bulk of this suite can run as plain [Test] without waiting on real frames. Only
    /// scenarios that need FacingController's own real Update() loop (which this class does not control) or a
    /// genuinely baked NavMesh actually resolving a path use [UnityTest].
    /// </summary>
    public class InteractionLifecycleTests
    {
        private readonly List<GameObject> spawned = new();
        private readonly List<GameSession> sessions = new();

        [TearDown]
        public void TearDown()
        {
            foreach (var go in spawned) if (go != null) UnityEngine.Object.DestroyImmediate(go);
            spawned.Clear();

            foreach (var session in sessions) session.Dispose();
            sessions.Clear();

            NavMesh.RemoveAllNavMeshData();
        }

        // ---------------------------------------------------------------- fakes

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
            public MoveStatus StatusToReport = MoveStatus.Moving;
            public bool RejectNextSetDestination;
            private MoveRequestId currentId = MoveRequestId.None;

            public MoveResult SetDestination(Vector3 destination)
            {
                if (RejectNextSetDestination) return new MoveResult(MoveStatus.Invalid, MoveRequestId.None);

                currentId = MoveRequestId.NewId();
                StatusToReport = MoveStatus.Pending;
                return new MoveResult(StatusToReport, currentId);
            }

            public void Stop() => StatusToReport = MoveStatus.Cancelled; // RequestId retained, matching IMovable's real contract

            /// <summary>Simulates an external order (e.g. a fresh player move) taking over -- a genuinely new id.</summary>
            public void SimulateExternalOrder()
            {
                currentId = MoveRequestId.NewId();
                StatusToReport = MoveStatus.Moving;
            }

            public MotionSnapshot Snapshot => new(Vector3.zero, Vector3.zero, true, 0f, StatusToReport, currentId);
        }

        private sealed class FakeExecution : IInteractionExecution
        {
            public int CancelCount;
            public void Cancel() => CancelCount++;
        }

        private sealed class FakeAdapter : MonoBehaviour, IInteractionAdapter
        {
            public bool Available = true;
            public bool CompleteSynchronously;
            public bool ThrowOnBeginExecute;
            public InteractionExecutionResult SynchronousResult = InteractionExecutionResult.Succeeded;
            public int BeginExecuteCallCount;
            public FakeExecution LastExecution;
            public Action<InteractionExecutionResult> PendingCallback;

            public DefinitionId ActionId { get; } = new("fake-action");
            public bool IsAvailable(RuntimeEntityId performer) => Available;

            public IInteractionExecution BeginExecute(RuntimeEntityId performer, Action<InteractionExecutionResult> onFinished)
            {
                BeginExecuteCallCount++;
                if (ThrowOnBeginExecute) throw new InvalidOperationException("fake adapter failure");

                if (CompleteSynchronously)
                {
                    onFinished(SynchronousResult);
                    return null;
                }

                PendingCallback = onFinished;
                LastExecution = new FakeExecution();
                return LastExecution;
            }
        }

        // ---------------------------------------------------------------- fixtures

        private GameSession NewSession()
        {
            var session = new GameSession(new RulesDatabase(), new DiceService(new SeededRandomSource(1)));
            sessions.Add(session);
            return session;
        }

        /// <summary>A performer with a fake, fully-controllable IMovable and NO FacingController -- for
        /// scenarios focused on Executing-phase/execution-contract behavior, which then never wait on a real
        /// FacingController.Update() loop (ArriveAndBeginTurning skips straight to BeginExecute when Facing is
        /// null).</summary>
        private (GameObject go, CharacterAgent agent, FakeMovable movable) CreatePerformerNoFacing(GameSession session, PartyState party, Vector3 position)
        {
            var go = new GameObject("PerformerNoFacing") { transform = { position = position } };
            var movable = go.AddComponent<FakeMovable>();
            var agent = go.AddComponent<CharacterAgent>();
            spawned.Add(go);

            var id = RuntimeEntityId.NewId();
            agent.Bind(id, session);
            party.AddMember(id);
            return (go, agent, movable);
        }

        /// <summary>A performer with a fake IMovable AND a real FacingController -- for the handful of
        /// scenarios that specifically need to prove the TurningToFace mechanic.</summary>
        private (GameObject go, CharacterAgent agent, FakeMovable movable, FacingController facing) CreatePerformerWithFacing(GameSession session, PartyState party, Vector3 position)
        {
            var go = new GameObject("PerformerWithFacing") { transform = { position = position } };
            var movable = go.AddComponent<FakeMovable>();
            var facing = go.AddComponent<FacingController>();
            var agent = go.AddComponent<CharacterAgent>();
            spawned.Add(go);

            var id = RuntimeEntityId.NewId();
            agent.Bind(id, session);
            party.AddMember(id);
            return (go, agent, movable, facing);
        }

        private (GameObject go, WorldInteractable interactable, FakeAdapter adapter) CreateTarget(Vector3 anchorPosition, float range = 1.5f)
        {
            var anchorGo = new GameObject("Anchor") { transform = { position = anchorPosition } };
            spawned.Add(anchorGo);

            var go = new GameObject("Target");
            go.SetActive(false); // Awake must not run until Configure has set the required anchor
            var adapter = go.AddComponent<FakeAdapter>();
            var interactable = go.AddComponent<WorldInteractable>();
            interactable.Configure(anchorGo.transform, range);
            go.SetActive(true);
            spawned.Add(go);

            return (go, interactable, adapter);
        }

        private static GameObject CreateGroundPlane(Vector3 center, float halfSize)
        {
            var plane = GameObject.CreatePrimitive(PrimitiveType.Plane);
            plane.transform.position = center;
            plane.transform.localScale = new Vector3(halfSize / 5f, 1f, halfSize / 5f);
            plane.isStatic = true;
            return plane;
        }

        private void BakeNavMesh()
        {
            var surface = new GameObject("NavMeshSurface").AddComponent<NavMeshSurface>();
            spawned.Add(surface.gameObject);
            surface.BuildNavMesh();
        }

        private (GameObject go, CharacterAgent agent, FacingController facing) CreateRealPerformer(GameSession session, PartyState party, Vector3 position, float speed = 8f)
        {
            var go = new GameObject("RealPerformer");
            go.transform.position = position;
            var navAgent = go.AddComponent<NavMeshAgent>();
            navAgent.speed = speed;
            navAgent.acceleration = 40f;
            var motor = go.AddComponent<NavMeshCharacterMotor>();
            var facing = go.AddComponent<FacingController>();
            var agent = go.AddComponent<CharacterAgent>();
            spawned.Add(go);
            navAgent.Warp(position);

            var id = RuntimeEntityId.NewId();
            agent.Bind(id, session);
            party.AddMember(id);
            return (go, agent, facing);
        }

        // ---------------------------------------------------------------- 1/2: happy path

        [UnityTest]
        public IEnumerator FullLifecycleApproachTurnExecuteCompletesAndReleasesFacing()
        {
            CreateGroundPlane(Vector3.zero, 20f);
            BakeNavMesh();

            var session = NewSession();
            var party = session.Party;
            var (performerGo, agent, facing) = CreateRealPerformer(session, party, new Vector3(-5f, 0f, 0f));
            var (_, interactable, adapter) = CreateTarget(new Vector3(5f, 0f, 0f));
            adapter.CompleteSynchronously = true;

            var commandSource = new FakeCommandSource();
            var controller = new InteractionController(commandSource, party, session);
            InteractionOutcome? outcome = null;
            controller.RequestEnded += o => outcome = o;

            commandSource.RaiseInteract(interactable.EntityId);
            Assert.IsNotNull(controller.CurrentRequest);
            Assert.AreEqual(InteractionPhase.Approaching, controller.CurrentRequest.Phase);

            var elapsed = 0f;
            while (outcome == null && elapsed < 15f)
            {
                controller.Tick(Time.deltaTime);
                elapsed += Time.deltaTime;
                yield return null;
            }

            Assert.AreEqual(InteractionOutcome.Completed, outcome);
            Assert.AreEqual(1, adapter.BeginExecuteCallCount);
            Assert.IsNull(controller.CurrentRequest);

            // Prove FacingPriority.Interaction was actually released, not just that nothing else happens to be
            // requesting: issue a REAL move in the opposite direction. Motion-priority facing only engages from
            // genuine velocity, and Motion is the LOWEST tier -- if Interaction were still held (it outranks
            // Motion), this real movement's own facing would never be allowed to win.
            var oppositeDestination = performerGo.transform.position + (performerGo.transform.position - interactable.InteractionPoint).normalized * 5f;
            agent.Motor.SetDestination(oppositeDestination);

            var elapsedTurn = 0f;
            var towardOpposite = (oppositeDestination - performerGo.transform.position).normalized;
            while (elapsedTurn < 5f && Vector3.Dot(performerGo.transform.forward, towardOpposite) < 0.9f)
            {
                elapsedTurn += Time.deltaTime;
                yield return null;
            }

            Assert.Greater(Vector3.Dot(performerGo.transform.forward, towardOpposite), 0.9f,
                "The character must be free to turn toward its new real movement once the interaction ends -- FacingPriority.Interaction must have been released.");

            controller.Dispose();
        }

        [Test]
        public void AlreadyInRangeSkipsApproachAndExecutesImmediately()
        {
            var session = NewSession();
            var party = session.Party;
            var (_, agent, movable) = CreatePerformerNoFacing(session, party, Vector3.zero);
            var (_, interactable, adapter) = CreateTarget(new Vector3(0.5f, 0f, 0f));
            adapter.CompleteSynchronously = true;

            var commandSource = new FakeCommandSource();
            var controller = new InteractionController(commandSource, party, session);

            commandSource.RaiseInteract(interactable.EntityId);

            Assert.AreEqual(1, adapter.BeginExecuteCallCount);
            Assert.IsNull(controller.CurrentRequest); // synchronous completion already ended it

            controller.Dispose();
        }

        // ---------------------------------------------------------------- 3: cancel during Approaching

        [Test]
        public void CancelDuringApproachingStopsOwnedMovementAndNeverClaimsFacing()
        {
            var session = NewSession();
            var party = session.Party;
            var (_, agent, movable) = CreatePerformerNoFacing(session, party, new Vector3(-10f, 0f, 0f));
            var (_, interactable, _) = CreateTarget(new Vector3(10f, 0f, 0f));

            var commandSource = new FakeCommandSource();
            var controller = new InteractionController(commandSource, party, session);
            InteractionOutcome? outcome = null;
            controller.RequestEnded += o => outcome = o;

            commandSource.RaiseInteract(interactable.EntityId);
            Assert.AreEqual(InteractionPhase.Approaching, controller.CurrentRequest.Phase);

            controller.Cancel();

            Assert.AreEqual(InteractionOutcome.Cancelled, outcome);
            Assert.AreEqual(MoveStatus.Cancelled, movable.Snapshot.Status);
            Assert.IsNull(controller.CurrentRequest);

            controller.Dispose();
        }

        // ---------------------------------------------------------------- 4: cancel during Executing (Talk)

        [Test]
        public void CancelDuringExecutingCancelsTheExecutionExactlyOnceAndIgnoresALateCallback()
        {
            var session = NewSession();
            var party = session.Party;
            CreatePerformerNoFacing(session, party, Vector3.zero);
            var (_, interactable, adapter) = CreateTarget(new Vector3(0.5f, 0f, 0f));
            adapter.CompleteSynchronously = false;

            var commandSource = new FakeCommandSource();
            var controller = new InteractionController(commandSource, party, session);
            var outcomes = new List<InteractionOutcome>();
            controller.RequestEnded += outcomes.Add;

            commandSource.RaiseInteract(interactable.EntityId);
            Assert.AreEqual(InteractionPhase.Executing, controller.CurrentRequest.Phase);
            var execution = adapter.LastExecution;
            var pendingCallback = adapter.PendingCallback;

            controller.Cancel();

            Assert.AreEqual(1, execution.CancelCount);
            CollectionAssert.AreEqual(new[] { InteractionOutcome.Cancelled }, outcomes);

            // A late callback from the already-cancelled execution must be ignored, not finish a new request.
            pendingCallback(InteractionExecutionResult.Succeeded);
            CollectionAssert.AreEqual(new[] { InteractionOutcome.Cancelled }, outcomes);

            controller.Dispose();
        }

        // ---------------------------------------------------------------- 5: disabled (not destroyed) target

        [Test]
        public void DisablingTheTargetDuringApproachEndsAsTargetLost()
        {
            var session = NewSession();
            var party = session.Party;
            CreatePerformerNoFacing(session, party, new Vector3(-10f, 0f, 0f));
            var (targetGo, interactable, _) = CreateTarget(new Vector3(10f, 0f, 0f));

            var commandSource = new FakeCommandSource();
            var controller = new InteractionController(commandSource, party, session);
            InteractionOutcome? outcome = null;
            controller.RequestEnded += o => outcome = o;

            commandSource.RaiseInteract(interactable.EntityId);
            Assert.IsNotNull(controller.CurrentRequest);

            targetGo.SetActive(false); // disabled, not destroyed -- must not be treated as still-null-safe

            controller.Tick(0f);

            Assert.AreEqual(InteractionOutcome.TargetLost, outcome);
            controller.Dispose();
        }

        [Test]
        public void DestroyingTheTargetDuringApproachEndsAsTargetLost()
        {
            var session = NewSession();
            var party = session.Party;
            CreatePerformerNoFacing(session, party, new Vector3(-10f, 0f, 0f));
            var (targetGo, interactable, _) = CreateTarget(new Vector3(10f, 0f, 0f));

            var commandSource = new FakeCommandSource();
            var controller = new InteractionController(commandSource, party, session);
            InteractionOutcome? outcome = null;
            controller.RequestEnded += o => outcome = o;

            commandSource.RaiseInteract(interactable.EntityId);
            spawned.Remove(targetGo);
            UnityEngine.Object.DestroyImmediate(targetGo);

            controller.Tick(0f);

            Assert.AreEqual(InteractionOutcome.TargetLost, outcome);
            controller.Dispose();
        }

        // ---------------------------------------------------------------- 6: lost performer

        [Test]
        public void DisablingThePerformerDuringApproachEndsAsPerformerLost()
        {
            var session = NewSession();
            var party = session.Party;
            var (performerGo, _, _) = CreatePerformerNoFacing(session, party, new Vector3(-10f, 0f, 0f));
            var (_, interactable, _) = CreateTarget(new Vector3(10f, 0f, 0f));

            var commandSource = new FakeCommandSource();
            var controller = new InteractionController(commandSource, party, session);
            InteractionOutcome? outcome = null;
            controller.RequestEnded += o => outcome = o;

            commandSource.RaiseInteract(interactable.EntityId);
            performerGo.SetActive(false);

            controller.Tick(0f);

            Assert.AreEqual(InteractionOutcome.PerformerLost, outcome);
            controller.Dispose();
        }

        [Test]
        public void DestroyingThePerformerDuringApproachEndsAsPerformerLost()
        {
            var session = NewSession();
            var party = session.Party;
            var (performerGo, _, _) = CreatePerformerNoFacing(session, party, new Vector3(-10f, 0f, 0f));
            var (_, interactable, _) = CreateTarget(new Vector3(10f, 0f, 0f));

            var commandSource = new FakeCommandSource();
            var controller = new InteractionController(commandSource, party, session);
            InteractionOutcome? outcome = null;
            controller.RequestEnded += o => outcome = o;

            commandSource.RaiseInteract(interactable.EntityId);
            spawned.Remove(performerGo);
            UnityEngine.Object.DestroyImmediate(performerGo);

            controller.Tick(0f);

            Assert.AreEqual(InteractionOutcome.PerformerLost, outcome);
            controller.Dispose();
        }

        // ---------------------------------------------------------------- 7: host OnDisable

        [Test]
        public void DisablingTheHostBehaviourCancelsAnyActiveRequest()
        {
            var session = NewSession();
            var party = session.Party;
            CreatePerformerNoFacing(session, party, Vector3.zero);
            var (_, interactable, adapter) = CreateTarget(new Vector3(0.5f, 0f, 0f));
            adapter.CompleteSynchronously = false;

            var hostGo = new GameObject("Host");
            spawned.Add(hostGo);
            var host = hostGo.AddComponent<InteractionControllerBehaviour>();
            var commandSource = new FakeCommandSource();
            host.Initialize(commandSource, party, session);

            commandSource.RaiseInteract(interactable.EntityId);
            Assert.IsNotNull(host.Controller.CurrentRequest);

            hostGo.SetActive(false);

            Assert.IsNull(host.Controller.CurrentRequest);
            Assert.AreEqual(1, adapter.LastExecution.CancelCount);
        }

        // ---------------------------------------------------------------- 8: session teardown

        [Test]
        public void DisposingTheSessionMidInteractionEndsItAsSessionEndedOnNextTick()
        {
            var session = NewSession();
            var party = session.Party;
            CreatePerformerNoFacing(session, party, new Vector3(-10f, 0f, 0f));
            var (_, interactable, _) = CreateTarget(new Vector3(10f, 0f, 0f));

            var commandSource = new FakeCommandSource();
            var controller = new InteractionController(commandSource, party, session);
            InteractionOutcome? outcome = null;
            controller.RequestEnded += o => outcome = o;

            commandSource.RaiseInteract(interactable.EntityId);
            session.Dispose();
            sessions.Remove(session); // already disposed, don't double-dispose in TearDown

            controller.Tick(0f);

            Assert.AreEqual(InteractionOutcome.SessionEnded, outcome);
        }

        // ---------------------------------------------------------------- 9/10: synchronous + duplicate completion

        [Test]
        public void SynchronousCompletionEndsExactlyOnceBeforeBeginExecuteEvenReturns()
        {
            var session = NewSession();
            var party = session.Party;
            CreatePerformerNoFacing(session, party, Vector3.zero);
            var (_, interactable, adapter) = CreateTarget(new Vector3(0.5f, 0f, 0f));
            adapter.CompleteSynchronously = true;

            var commandSource = new FakeCommandSource();
            var controller = new InteractionController(commandSource, party, session);
            var outcomes = new List<InteractionOutcome>();
            controller.RequestEnded += outcomes.Add;

            commandSource.RaiseInteract(interactable.EntityId);

            CollectionAssert.AreEqual(new[] { InteractionOutcome.Completed }, outcomes);
            Assert.IsNull(controller.CurrentRequest);

            controller.Dispose();
        }

        [Test]
        public void ADuplicateOnFinishedInvocationFromABuggyAdapterIsIgnored()
        {
            var session = NewSession();
            var party = session.Party;
            CreatePerformerNoFacing(session, party, Vector3.zero);
            var (_, interactable, adapter) = CreateTarget(new Vector3(0.5f, 0f, 0f));
            adapter.CompleteSynchronously = false;

            var commandSource = new FakeCommandSource();
            var controller = new InteractionController(commandSource, party, session);
            var outcomes = new List<InteractionOutcome>();
            controller.RequestEnded += outcomes.Add;

            commandSource.RaiseInteract(interactable.EntityId);
            var callback = adapter.PendingCallback;

            callback(InteractionExecutionResult.Succeeded);
            callback(InteractionExecutionResult.Succeeded); // duplicate -- must not fire RequestEnded twice

            CollectionAssert.AreEqual(new[] { InteractionOutcome.Completed }, outcomes);

            controller.Dispose();
        }

        // ---------------------------------------------------------------- 11: adapter throws

        [Test]
        public void AnExceptionFromBeginExecuteIsCaughtAndReportedAsFailed()
        {
            var session = NewSession();
            var party = session.Party;
            CreatePerformerNoFacing(session, party, Vector3.zero);
            var (_, interactable, adapter) = CreateTarget(new Vector3(0.5f, 0f, 0f));
            adapter.ThrowOnBeginExecute = true;

            var commandSource = new FakeCommandSource();
            var controller = new InteractionController(commandSource, party, session);
            InteractionOutcome? outcome = null;
            controller.RequestEnded += o => outcome = o;

            LogAssert.Expect(LogType.Exception, new System.Text.RegularExpressions.Regex(".*"));
            commandSource.RaiseInteract(interactable.EntityId);

            Assert.AreEqual(InteractionOutcome.Failed, outcome);
            Assert.IsNull(controller.CurrentRequest);

            controller.Dispose();
        }

        // ---------------------------------------------------------------- 12: new move order during Executing

        [Test]
        public void ANewMoveOrderDuringExecutingSupersedesTheInteraction()
        {
            var session = NewSession();
            var party = session.Party;
            var (_, _, movable) = CreatePerformerNoFacing(session, party, Vector3.zero);
            var (_, interactable, adapter) = CreateTarget(new Vector3(0.5f, 0f, 0f));
            adapter.CompleteSynchronously = false;

            var commandSource = new FakeCommandSource();
            var controller = new InteractionController(commandSource, party, session);
            InteractionOutcome? outcome = null;
            controller.RequestEnded += o => outcome = o;

            commandSource.RaiseInteract(interactable.EntityId);
            Assert.AreEqual(InteractionPhase.Executing, controller.CurrentRequest.Phase);

            movable.SimulateExternalOrder(); // a fresh player move elsewhere, while Talk (say) is open
            controller.Tick(0f);

            Assert.AreEqual(InteractionOutcome.Superseded, outcome);
            Assert.AreEqual(1, adapter.LastExecution.CancelCount);

            controller.Dispose();
        }

        // ---------------------------------------------------------------- 13: rejected new move order

        [Test]
        public void ARejectedNewMoveOrderDoesNotDisturbAnInFlightApproach()
        {
            var session = NewSession();
            var party = session.Party;
            var (_, _, movable) = CreatePerformerNoFacing(session, party, new Vector3(-10f, 0f, 0f));
            var (_, interactable, _) = CreateTarget(new Vector3(10f, 0f, 0f));

            var commandSource = new FakeCommandSource();
            var controller = new InteractionController(commandSource, party, session);
            InteractionOutcome? outcome = null;
            controller.RequestEnded += o => outcome = o;

            commandSource.RaiseInteract(interactable.EntityId);
            var ownedId = controller.CurrentRequest.OwnedMotionRequestId;

            // Mirrors PartyMovementService leaving existing movement untouched when formation resolution is
            // Unreachable -- no SetDestination call actually reaches the motor, so nothing about its Snapshot changes.
            movable.RejectNextSetDestination = true;
            var rejected = movable.SetDestination(new Vector3(999f, 0f, 999f));
            Assert.AreEqual(MoveStatus.Invalid, rejected.Status);

            controller.Tick(0.1f);

            Assert.IsNull(outcome);
            Assert.IsNotNull(controller.CurrentRequest);
            Assert.AreEqual(ownedId, controller.CurrentRequest.OwnedMotionRequestId);

            controller.Dispose();
        }

        // ---------------------------------------------------------------- 14: second Interact replaces the first

        [Test]
        public void ASecondInteractCommandWhileApproachingReplacesTheFirst()
        {
            var session = NewSession();
            var party = session.Party;
            CreatePerformerNoFacing(session, party, new Vector3(-10f, 0f, 0f));
            var (_, firstTarget, _) = CreateTarget(new Vector3(10f, 0f, 0f));
            var (_, secondTarget, _) = CreateTarget(new Vector3(-10f, 0f, 10f));

            var commandSource = new FakeCommandSource();
            var controller = new InteractionController(commandSource, party, session);
            var outcomes = new List<InteractionOutcome>();
            controller.RequestEnded += outcomes.Add;

            commandSource.RaiseInteract(firstTarget.EntityId);
            commandSource.RaiseInteract(secondTarget.EntityId);

            CollectionAssert.AreEqual(new[] { InteractionOutcome.Superseded }, outcomes);
            Assert.AreSame(secondTarget, controller.CurrentRequest.Target);

            controller.Dispose();
        }

        [Test]
        public void ASecondInteractCommandWhileExecutingReplacesTheFirst()
        {
            var session = NewSession();
            var party = session.Party;
            CreatePerformerNoFacing(session, party, Vector3.zero);
            var (_, firstTarget, firstAdapter) = CreateTarget(new Vector3(0.5f, 0f, 0f));
            var (_, secondTarget, secondAdapter) = CreateTarget(new Vector3(-0.5f, 0f, 0f));
            firstAdapter.CompleteSynchronously = false;
            secondAdapter.CompleteSynchronously = true;

            var commandSource = new FakeCommandSource();
            var controller = new InteractionController(commandSource, party, session);
            var outcomes = new List<InteractionOutcome>();
            controller.RequestEnded += outcomes.Add;

            commandSource.RaiseInteract(firstTarget.EntityId);
            Assert.AreEqual(InteractionPhase.Executing, controller.CurrentRequest.Phase);

            commandSource.RaiseInteract(secondTarget.EntityId);

            Assert.AreEqual(1, firstAdapter.LastExecution.CancelCount);
            CollectionAssert.AreEqual(new[] { InteractionOutcome.Superseded, InteractionOutcome.Completed }, outcomes);

            controller.Dispose();
        }

        // ---------------------------------------------------------------- 15: blocked approach

        [Test]
        public void FakeBlockedStatusEndsTheInteractionWithoutExecuting()
        {
            var session = NewSession();
            var party = session.Party;
            var (_, _, movable) = CreatePerformerNoFacing(session, party, new Vector3(-10f, 0f, 0f));
            var (_, interactable, adapter) = CreateTarget(new Vector3(10f, 0f, 0f));

            var commandSource = new FakeCommandSource();
            var controller = new InteractionController(commandSource, party, session);
            InteractionOutcome? outcome = null;
            controller.RequestEnded += o => outcome = o;

            commandSource.RaiseInteract(interactable.EntityId);
            movable.StatusToReport = MoveStatus.Blocked;
            controller.Tick(0.1f);

            Assert.AreEqual(InteractionOutcome.Blocked, outcome);
            Assert.AreEqual(0, adapter.BeginExecuteCallCount);

            controller.Dispose();
        }

        [UnityTest]
        public IEnumerator RealDisconnectedNavMeshIslandApproachEndsBlocked()
        {
            CreateGroundPlane(new Vector3(0f, 0f, 0f), 10f);
            CreateGroundPlane(new Vector3(60f, 0f, 0f), 10f); // disconnected -- a real partial path, not Arrived
            BakeNavMesh();

            var session = NewSession();
            var party = session.Party;
            CreateRealPerformer(session, party, Vector3.zero);
            var (_, interactable, adapter) = CreateTarget(new Vector3(60f, 0f, 0f));

            var commandSource = new FakeCommandSource();
            var controller = new InteractionController(commandSource, party, session);
            InteractionOutcome? outcome = null;
            controller.RequestEnded += o => outcome = o;

            commandSource.RaiseInteract(interactable.EntityId);

            var elapsed = 0f;
            while (outcome == null && elapsed < 15f)
            {
                controller.Tick(Time.deltaTime);
                elapsed += Time.deltaTime;
                yield return null;
            }

            Assert.AreEqual(InteractionOutcome.Blocked, outcome);
            Assert.AreEqual(0, adapter.BeginExecuteCallCount);

            controller.Dispose();
        }

        // ---------------------------------------------------------------- 16: approach timeout

        [Test]
        public void ApproachThatNeverReachesATerminalStatusTimesOut()
        {
            var session = NewSession();
            var party = session.Party;
            CreatePerformerNoFacing(session, party, new Vector3(-10f, 0f, 0f));
            var (_, interactable, adapter) = CreateTarget(new Vector3(10f, 0f, 0f));

            var commandSource = new FakeCommandSource();
            var controller = new InteractionController(commandSource, party, session, approachTimeoutSeconds: 5f);
            InteractionOutcome? outcome = null;
            controller.RequestEnded += o => outcome = o;

            commandSource.RaiseInteract(interactable.EntityId); // FakeMovable stays Moving forever
            controller.Tick(10f); // one big tick, past the 5s timeout

            Assert.AreEqual(InteractionOutcome.ApproachTimedOut, outcome);
            Assert.AreEqual(0, adapter.BeginExecuteCallCount);

            controller.Dispose();
        }

        // ---------------------------------------------------------------- 17: arrived but outside range

        [Test]
        public void ArrivingWithoutActuallyClosingTheDistanceEndsAsApproachInsufficient()
        {
            var session = NewSession();
            var party = session.Party;
            // Performer never actually moves -- only the fake motor's Status changes -- so it is still far
            // from the target's InteractionPoint when Arrived is reported.
            var (_, _, movable) = CreatePerformerNoFacing(session, party, new Vector3(-10f, 0f, 0f));
            var (_, interactable, adapter) = CreateTarget(new Vector3(10f, 0f, 0f), range: 1.5f);

            var commandSource = new FakeCommandSource();
            var controller = new InteractionController(commandSource, party, session);
            InteractionOutcome? outcome = null;
            controller.RequestEnded += o => outcome = o;

            commandSource.RaiseInteract(interactable.EntityId);
            movable.StatusToReport = MoveStatus.Arrived;
            controller.Tick(0.1f);

            Assert.AreEqual(InteractionOutcome.ApproachInsufficient, outcome);
            Assert.AreEqual(0, adapter.BeginExecuteCallCount);

            controller.Dispose();
        }

        // ---------------------------------------------------------------- unavailable at revalidation

        [Test]
        public void BecomingUnavailableBetweenQueueingAndArrivalEndsAsUnavailable()
        {
            var session = NewSession();
            var party = session.Party;
            var (performerGo, _, movable) = CreatePerformerNoFacing(session, party, new Vector3(-10f, 0f, 0f));
            var (_, interactable, adapter) = CreateTarget(new Vector3(10f, 0f, 0f));

            var commandSource = new FakeCommandSource();
            var controller = new InteractionController(commandSource, party, session);
            InteractionOutcome? outcome = null;
            controller.RequestEnded += o => outcome = o;

            commandSource.RaiseInteract(interactable.EntityId);
            adapter.Available = false; // e.g. a loot container someone else emptied while this one was walking over

            // FakeMovable doesn't actually drive the transform, so simulate a genuine arrival by moving the
            // performer to the interaction point ourselves -- isolates this test to the availability check
            // rather than also tripping the (separately tested) range check.
            performerGo.transform.position = interactable.InteractionPoint;
            movable.StatusToReport = MoveStatus.Arrived;
            controller.Tick(0.1f);

            Assert.AreEqual(InteractionOutcome.Unavailable, outcome);
            Assert.AreEqual(0, adapter.BeginExecuteCallCount);

            controller.Dispose();
        }
    }
}
