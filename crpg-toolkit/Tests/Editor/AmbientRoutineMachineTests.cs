using System;
using System.Collections.Generic;
using System.Linq;
using CRPG.Core;
using CRPG.Gameplay.Ambient;
using CRPG.Navigation;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.AI;

namespace CRPG.Tests.EditMode
{
    public sealed class AmbientRoutineMachineTests
    {
        private sealed class FakeMotor : IMovable
        {
            public readonly List<Vector3> Destinations = new();
            public Vector3 Position;
            public MoveRequestId SnapshotId;
            public MoveStatus Status = MoveStatus.Arrived;
            public float Remaining;
            public int StopCalls;
            public bool RejectNext;

            public MoveResult SetDestination(Vector3 destination)
            {
                if (RejectNext) { RejectNext = false; return MoveResult.Invalid(); }
                SnapshotId = MoveRequestId.NewId();
                Status = MoveStatus.Moving;
                Remaining = 100f;
                Destinations.Add(destination);
                return new MoveResult(MoveStatus.Pending, SnapshotId);
            }

            public void Stop() { StopCalls++; Status = MoveStatus.Cancelled; }
            public bool Warp(Vector3 position) { Position = position; return true; }
            public MotionSnapshot Snapshot => new(Vector3.zero, Vector3.zero, true, Remaining, Status, SnapshotId);

            public void Arrive()
            {
                Position = Destinations[^1];
                Status = MoveStatus.Arrived;
                Remaining = 0f;
            }

            /// <summary>Someone else took the motor: the snapshot now carries a different request id.</summary>
            public void Steal() { SnapshotId = MoveRequestId.NewId(); Status = MoveStatus.Moving; }
        }

        private sealed class FakeNav : INavMeshService
        {
            public Func<Vector3, bool> Walkable = _ => true;
            public Func<Vector3, Vector3, bool> Connected = (_, _) => true;

            public bool TrySamplePosition(Vector3 point, out Vector3 sampledPosition, float maxDistance = 5f, int areaMask = NavMesh.AllAreas)
            {
                sampledPosition = point;
                return Walkable(point);
            }

            public bool TryCalculatePathLength(Vector3 from, Vector3 to, out float length, int areaMask = NavMesh.AllAreas)
            {
                length = Vector3.Distance(from, to);
                return Connected(from, to);
            }

            public bool TryCalculatePath(Vector3 from, Vector3 to, out Vector3[] corners, int areaMask = NavMesh.AllAreas)
            {
                corners = new[] { from, to };
                return Connected(from, to);
            }
        }

        private sealed class FakeAnimation : IRoutineAnimation
        {
            public readonly List<string> Begun = new();
            public int Ended;
            public float ClipLength;

            public float Begin(DefinitionId cue) { Begun.Add(cue.Value); return ClipLength; }
            public void End() => Ended++;
        }

        private sealed class FakeFacing : IRoutineFacing
        {
            public readonly List<Vector3> Faced = new();
            public int Cleared;
            public void Face(Vector3 direction) => Faced.Add(direction);
            public void Clear() => Cleared++;
        }

        private sealed class FakeProp : IRoutineProp
        {
            public bool Holding { get; set; }
            public int Attaches;
            public int Detaches;
            public CarriedPropDefinition LastProp;

            public bool Attach(CarriedPropDefinition prop) { Attaches++; LastProp = prop; Holding = true; return true; }
            public void Detach() { Detaches++; Holding = false; }
        }

        private FakeMotor motor;
        private FakeNav nav;
        private FakeAnimation animation;
        private FakeFacing facing;
        private FakeProp prop;
        private readonly List<string> logs = new();
        private readonly List<UnityEngine.Object> toDestroy = new();

        [SetUp]
        public void SetUp()
        {
            motor = new FakeMotor { Position = Vector3.zero };
            nav = new FakeNav();
            animation = new FakeAnimation();
            facing = new FakeFacing();
            prop = new FakeProp();
            logs.Clear();
        }

        [TearDown]
        public void TearDown()
        {
            foreach (var o in toDestroy) if (o != null) UnityEngine.Object.DestroyImmediate(o);
            toDestroy.Clear();
        }

        private AmbientRoutineMachine Make(RoutinePlan plan, int seed = 7) =>
            new(plan, motor, nav, animation, facing, prop, () => motor.Position, seed, logs.Add);

        private static RoutinePlan WanderPlan(float radius = 6f, float pauseMin = 2f, float pauseMax = 6f) =>
            new() { Kind = NpcRoutineKind.Wander, Centre = new Vector3(10f, 0f, 10f), Radius = radius, PauseMin = pauseMin, PauseMax = pauseMax };

        private static RoutineStopPlan Stop(string name, float x, WorkStopAction action = WorkStopAction.None, float seconds = 1f, string cue = null, CarriedPropDefinition prop = null) =>
            new() { Label = name, Position = new Vector3(x, 0f, 0f), Facing = Vector3.forward, Action = action, Seconds = seconds, Cue = new DefinitionId(cue), Prop = prop };

        private static RoutinePlan LoopPlan(params RoutineStopPlan[] stops) =>
            new() { Kind = NpcRoutineKind.WorkLoop, Stops = stops.ToList() };

        private CarriedPropDefinition NewProp()
        {
            var p = ScriptableObject.CreateInstance<CarriedPropDefinition>();
            toDestroy.Add(p);
            return p;
        }

        private static void Run(AmbientRoutineMachine machine, float seconds, float step = 0.1f)
        {
            for (var t = 0f; t < seconds; t += step) machine.Tick(step);
        }

        private static bool RunUntil(AmbientRoutineMachine machine, Func<bool> done, float maxSeconds = 60f, float step = 0.1f)
        {
            for (var t = 0f; t < maxSeconds; t += step)
            {
                if (done()) return true;
                machine.Tick(step);
            }

            return done();
        }

        // ---- Stand ----

        [Test]
        public void Stand_DoesNothing()
        {
            var machine = Make(new RoutinePlan { Kind = NpcRoutineKind.Stand });
            Run(machine, 30f);

            Assert.IsTrue(machine.IsInert);
            Assert.AreEqual(0, motor.Destinations.Count);
        }

        // ---- Wander ----

        [Test]
        public void Wander_PicksPointsInsideTheCircle_AndKeepsWalking()
        {
            var plan = WanderPlan(radius: 5f);
            var machine = Make(plan);

            for (var leg = 0; leg < 12; leg++)
            {
                Assert.IsTrue(RunUntil(machine, () => motor.Destinations.Count > leg), $"leg {leg} never started");
                motor.Arrive();
            }

            foreach (var d in motor.Destinations)
            {
                var flat = new Vector3(d.x - plan.Centre.x, 0f, d.z - plan.Centre.z);
                Assert.LessOrEqual(flat.magnitude, plan.Radius + 0.5f, $"{d} left the wander circle");
            }
        }

        [Test]
        public void Wander_PausesBetweenLegsForTheAuthoredTime()
        {
            var machine = Make(WanderPlan(pauseMin: 3f, pauseMax: 3f));
            RunUntil(machine, () => motor.Destinations.Count == 1);
            motor.Arrive();
            machine.Tick(0.1f); // sees the arrival, starts the pause

            Run(machine, 2.5f);
            Assert.AreEqual(1, motor.Destinations.Count, "still pausing");

            Run(machine, 1f);
            Assert.AreEqual(2, motor.Destinations.Count, "pause over, next leg planned");
        }

        [Test]
        public void Wander_NeverWalksToAPointItCannotReach()
        {
            nav.Connected = (_, to) => to.x > 10f;
            var machine = Make(WanderPlan());
            for (var leg = 0; leg < 10; leg++)
            {
                RunUntil(machine, () => motor.Destinations.Count > leg);
                motor.Arrive();
            }

            Assert.IsTrue(motor.Destinations.All(d => d.x > 10f));
        }

        [Test]
        public void Wander_WithNowhereToGo_BacksOffInsteadOfSpinning()
        {
            nav.Walkable = _ => false;
            var machine = Make(WanderPlan());
            Run(machine, 60f);

            Assert.AreEqual(0, motor.Destinations.Count);
            Assert.IsFalse(machine.IsInert, "may recover if the surface changes");
        }

        [Test]
        public void Wander_BlockedLeg_PicksAnotherPoint()
        {
            var machine = Make(WanderPlan());
            RunUntil(machine, () => motor.Destinations.Count == 1);
            motor.Status = MoveStatus.Blocked;

            Assert.IsTrue(RunUntil(machine, () => motor.Destinations.Count == 2, 10f));
        }

        [Test]
        public void Wander_RejectedDestination_IsRetriedLater()
        {
            motor.RejectNext = true;
            var machine = Make(WanderPlan());

            Assert.IsTrue(RunUntil(machine, () => motor.Destinations.Count == 1, 30f));
        }

        [Test]
        public void Wander_AStalledLeg_IsAbandonedAndTheMotorStopped()
        {
            var machine = Make(WanderPlan());
            RunUntil(machine, () => motor.Destinations.Count == 1);
            motor.Remaining = 50f; // never gets closer

            Assert.IsTrue(RunUntil(machine, () => motor.StopCalls == 1, AmbientRoutineMachine.StallSeconds + 2f));
            Assert.IsTrue(RunUntil(machine, () => motor.Destinations.Count == 2, 10f), "then picks another point");
        }

        [Test]
        public void Wander_ProgressKeepsAMovingLegAlive()
        {
            var machine = Make(WanderPlan());
            RunUntil(machine, () => motor.Destinations.Count == 1);

            for (var i = 0; i < 100; i++)
            {
                motor.Remaining = 100f - i * 0.5f; // 0.5 m per tick of 0.1 s
                machine.Tick(0.1f);
            }

            Assert.AreEqual(0, motor.StopCalls);
        }

        [Test]
        public void Wander_AWalkWhoseRemainingDistanceIsUnknown_IsNotStalledWhileTheNpcIsActuallyMoving()
        {
            // NavMeshAgent.remainingDistance is Infinity until the last stretch of a multi-corner path.
            var machine = Make(WanderPlan());
            RunUntil(machine, () => motor.Destinations.Count == 1);

            for (var i = 0; i < 200; i++)
            {
                motor.Remaining = float.PositiveInfinity;
                motor.Position += new Vector3(0.5f, 0f, 0f); // 5 m/s along the path
                machine.Tick(0.1f);
            }

            Assert.AreEqual(0, motor.StopCalls, "twenty seconds of real walking is not a stall");
        }

        [Test]
        public void Wander_WhenAnotherSystemTakesTheMotor_LeavesItAloneThenResumes()
        {
            var machine = Make(WanderPlan());
            RunUntil(machine, () => motor.Destinations.Count == 1);
            motor.Steal();

            machine.Tick(0.1f);
            Assert.AreEqual(0, motor.StopCalls, "must not cancel someone else's move");
            Assert.AreEqual(AmbientRoutineMachine.Phase.Idle, machine.Current);

            motor.Status = MoveStatus.Arrived;
            Assert.IsTrue(RunUntil(machine, () => motor.Destinations.Count == 2, 10f), "resumes after the back-off");
        }

        [Test]
        public void Wander_SameSeed_SameWalk()
        {
            List<Vector3> Walk(int seed)
            {
                SetUp();
                var machine = Make(WanderPlan(), seed);
                for (var leg = 0; leg < 6; leg++)
                {
                    RunUntil(machine, () => motor.Destinations.Count > leg);
                    motor.Arrive();
                }

                return motor.Destinations.ToList();
            }

            var a = Walk(5);
            var b = Walk(5);
            var c = Walk(6);
            CollectionAssert.AreEqual(a, b);
            CollectionAssert.AreNotEqual(a, c);
        }

        // ---- Work loop ----

        [Test]
        public void WorkLoop_VisitsTheStopsInOrder_AndWraps()
        {
            var machine = Make(LoopPlan(Stop("a", 1f), Stop("b", 2f), Stop("c", 3f)));

            for (var visit = 0; visit < 7; visit++)
            {
                Assert.IsTrue(RunUntil(machine, () => motor.Destinations.Count > visit, 30f), $"visit {visit}");
                motor.Arrive();
            }

            CollectionAssert.AreEqual(new[] { 1f, 2f, 3f, 1f, 2f, 3f, 1f }, motor.Destinations.Select(d => d.x).ToArray());
        }

        [Test]
        public void WorkLoop_FacesTheStopWhileWorking_ThenReleasesFacing()
        {
            var stop = Stop("a", 1f, WorkStopAction.Work, seconds: 2f, cue: "work-fix");
            stop.Facing = Vector3.right;
            var machine = Make(LoopPlan(stop));

            RunUntil(machine, () => motor.Destinations.Count == 1);
            motor.Arrive();
            machine.Tick(0.1f);

            Assert.AreEqual(AmbientRoutineMachine.Phase.Acting, machine.Current);
            CollectionAssert.AreEqual(new[] { Vector3.right }, facing.Faced);
            CollectionAssert.AreEqual(new[] { "work-fix" }, animation.Begun);

            Assert.IsTrue(RunUntil(machine, () => machine.Current != AmbientRoutineMachine.Phase.Acting, 5f));
            Assert.AreEqual(1, animation.Ended);
            Assert.GreaterOrEqual(facing.Cleared, 1);
        }

        [Test]
        public void WorkLoop_CarriesAPropFromPickUpToDrop()
        {
            var crate = NewProp();
            var machine = Make(LoopPlan(
                Stop("pile-a", 1f, WorkStopAction.PickUp, 1f, "pickup", crate),
                Stop("pile-b", 9f, WorkStopAction.Drop, 1f, "pickup")));

            RunUntil(machine, () => motor.Destinations.Count == 1);
            motor.Arrive();
            Assert.IsTrue(RunUntil(machine, () => prop.Holding, 5f));
            Assert.AreSame(crate, prop.LastProp);

            RunUntil(machine, () => motor.Destinations.Count == 2);
            Assert.IsTrue(prop.Holding, "still carrying while walking");
            motor.Arrive();
            Assert.IsTrue(RunUntil(machine, () => !prop.Holding, 5f));
            Assert.AreEqual(1, prop.Attaches);
            Assert.AreEqual(1, prop.Detaches);
        }

        [Test]
        public void WorkLoop_PropAppearsHalfwayThroughThePickUp_NotAtTheStart()
        {
            var machine = Make(LoopPlan(Stop("a", 1f, WorkStopAction.PickUp, 2f, null, NewProp())));
            RunUntil(machine, () => motor.Destinations.Count == 1);
            motor.Arrive();
            machine.Tick(0.1f);

            Run(machine, 0.5f);
            Assert.IsFalse(prop.Holding);
            Run(machine, 0.7f);
            Assert.IsTrue(prop.Holding);
        }

        [Test]
        public void WorkLoop_PickUpWhileAlreadyHolding_DoesNotAttachASecondProp()
        {
            prop.Holding = true;
            var machine = Make(LoopPlan(Stop("a", 1f, WorkStopAction.PickUp, 1f, null, NewProp())));
            RunUntil(machine, () => motor.Destinations.Count == 1);
            motor.Arrive();
            Run(machine, 2f);

            Assert.AreEqual(0, prop.Attaches);
        }

        [Test]
        public void WorkLoop_DropWithNothingInTheHands_IsHarmless()
        {
            var machine = Make(LoopPlan(Stop("a", 1f, WorkStopAction.Drop, 1f)));
            RunUntil(machine, () => motor.Destinations.Count == 1);
            motor.Arrive();
            Run(machine, 2f);

            Assert.AreEqual(0, prop.Detaches);
        }

        [Test]
        public void WorkLoop_ThePropGoneFromTheHands_MeansNotHolding_EvenIfTheMachineAttachedIt()
        {
            var machine = Make(LoopPlan(
                Stop("a", 1f, WorkStopAction.PickUp, 1f, null, NewProp()),
                Stop("b", 2f, WorkStopAction.Drop, 1f)));
            RunUntil(machine, () => motor.Destinations.Count == 1);
            motor.Arrive();
            RunUntil(machine, () => prop.Holding);

            prop.Holding = false; // the model was swapped; the prop went with it
            RunUntil(machine, () => motor.Destinations.Count == 2);
            motor.Arrive();
            Run(machine, 2f);

            Assert.AreEqual(0, prop.Detaches, "the world says nothing is held");
        }

        [Test]
        public void WorkLoop_AUnreachableStop_IsSkippedAtStart_AndLogged()
        {
            nav.Connected = (_, to) => to.x != 2f;
            var machine = Make(LoopPlan(Stop("a", 1f), Stop("unreachable", 2f), Stop("c", 3f)));

            for (var visit = 0; visit < 4; visit++)
            {
                RunUntil(machine, () => motor.Destinations.Count > visit, 30f);
                motor.Arrive();
            }

            CollectionAssert.AreEqual(new[] { 1f, 3f, 1f, 3f }, motor.Destinations.Select(d => d.x).ToArray());
            Assert.IsTrue(logs.Any(l => l.Contains("unreachable")));
        }

        [Test]
        public void WorkLoop_WithNoReachableStop_StandsStill()
        {
            nav.Walkable = _ => false;
            var machine = Make(LoopPlan(Stop("a", 1f), Stop("b", 2f)));
            Run(machine, 20f);

            Assert.IsTrue(machine.IsInert);
            Assert.AreEqual(0, motor.Destinations.Count);
        }

        [Test]
        public void WorkLoop_AStopThatCannotBeReached_IsRetriedThenSkipped()
        {
            var machine = Make(LoopPlan(Stop("stuck", 1f), Stop("fine", 2f)));
            var attempts = 0;
            for (; attempts < 10 && !motor.Destinations.Any(d => d.x == 2f); attempts++)
            {
                RunUntil(machine, () => motor.Destinations.Count > attempts, 30f);
                if (motor.Destinations[attempts].x == 2f) break;
                motor.Status = MoveStatus.Blocked;
            }

            var stuckAttempts = motor.Destinations.Count(d => d.x == 1f);
            Assert.AreEqual(AmbientRoutineMachine.MaxStopRetries + 1, stuckAttempts);
            Assert.IsTrue(logs.Any(l => l.Contains("stuck")));
            Assert.AreEqual(2f, motor.Destinations[^1].x);
        }

        [Test]
        public void WorkLoop_ActionEndsOnItsTimer_EvenWhenTheAnimationNeverReportsBack()
        {
            animation.ClipLength = 0f;
            var machine = Make(LoopPlan(Stop("a", 1f, WorkStopAction.Work, 3f, "cue")));
            RunUntil(machine, () => motor.Destinations.Count == 1);
            motor.Arrive();
            machine.Tick(0.1f);

            Run(machine, 2.5f);
            Assert.AreEqual(AmbientRoutineMachine.Phase.Acting, machine.Current);
            Run(machine, 1f);
            Assert.AreNotEqual(AmbientRoutineMachine.Phase.Acting, machine.Current);
        }

        [Test]
        public void WorkLoop_ALongOneShot_IsNeverCutShort()
        {
            animation.ClipLength = 4f;
            var machine = Make(LoopPlan(Stop("a", 1f, WorkStopAction.Work, 1f, "cue")));
            RunUntil(machine, () => motor.Destinations.Count == 1);
            motor.Arrive();
            machine.Tick(0.1f);

            Run(machine, 3.5f);
            Assert.AreEqual(AmbientRoutineMachine.Phase.Acting, machine.Current);
        }

        [Test]
        public void WorkLoop_ZeroSecondsAndNoCue_StillTakesAMinimumMoment()
        {
            var machine = Make(LoopPlan(Stop("a", 1f, WorkStopAction.None, 0f)));
            RunUntil(machine, () => motor.Destinations.Count == 1);
            motor.Arrive();
            machine.Tick(0.1f);
            Assert.AreEqual(AmbientRoutineMachine.Phase.Acting, machine.Current);

            machine.Tick(0.1f);
            Assert.AreEqual(AmbientRoutineMachine.Phase.Acting, machine.Current, "never shorter than the minimum action time");
            Run(machine, 0.3f);
            Assert.AreNotEqual(AmbientRoutineMachine.Phase.Acting, machine.Current);
        }

        // ---- Suspension ----

        [Test]
        public void Suspend_WhileWalking_StopsOwnMoveAndReplansTheSameStopOnResume()
        {
            var machine = Make(LoopPlan(Stop("a", 1f), Stop("b", 2f)));
            RunUntil(machine, () => motor.Destinations.Count == 1);

            machine.SetSuspended(true);
            Assert.AreEqual(1, motor.StopCalls);
            Run(machine, 10f);
            Assert.AreEqual(1, motor.Destinations.Count, "nothing happens while suspended");

            machine.SetSuspended(false);
            Assert.IsTrue(RunUntil(machine, () => motor.Destinations.Count == 2, 5f));
            Assert.AreEqual(1f, motor.Destinations[1].x, "same stop again");
        }

        [Test]
        public void Resume_WaitsAMomentBeforeTheNextStep_SoANpcDoesNotWalkOffTheInstantAConversationEnds()
        {
            var machine = Make(LoopPlan(Stop("a", 1f), Stop("b", 2f)));
            RunUntil(machine, () => motor.Destinations.Count == 1);
            machine.SetSuspended(true);
            machine.SetSuspended(false);

            Run(machine, AmbientRoutineMachine.ResumeDelaySeconds - 0.3f);
            Assert.AreEqual(1, motor.Destinations.Count, "still standing there");

            Run(machine, 0.6f);
            Assert.AreEqual(2, motor.Destinations.Count, "then carries on");
        }

        [Test]
        public void Suspend_DoesNotStopAMoveThatBelongsToSomeoneElse()
        {
            var machine = Make(WanderPlan());
            RunUntil(machine, () => motor.Destinations.Count == 1);
            motor.Steal();

            machine.SetSuspended(true);
            Assert.AreEqual(0, motor.StopCalls);
        }

        [Test]
        public void Suspend_WhileWorking_ReleasesPoseAndFacing_ButKeepsTheCarriedProp()
        {
            var machine = Make(LoopPlan(Stop("a", 1f, WorkStopAction.PickUp, 2f, "cue", NewProp())));
            RunUntil(machine, () => motor.Destinations.Count == 1);
            motor.Arrive();
            Run(machine, 1.5f);
            Assert.IsTrue(prop.Holding);

            var clearedBefore = facing.Cleared;
            machine.SetSuspended(true);

            Assert.AreEqual(1, animation.Ended);
            Assert.Greater(facing.Cleared, clearedBefore);
            Assert.IsTrue(prop.Holding);
        }

        [Test]
        public void Interrupt_IsSafeBeforeAnythingHasStarted()
        {
            var machine = Make(WanderPlan());
            Assert.DoesNotThrow(machine.Interrupt);
            Assert.DoesNotThrow(() => machine.SetSuspended(true));
            Assert.DoesNotThrow(() => machine.SetSuspended(false));
        }

        // ---- Spec ----

        [Test]
        public void Spec_DefaultsToStand_AndResolvesTransformsToPositions()
        {
            Assert.AreEqual(NpcRoutineKind.Stand, new NpcRoutineSpec().Kind);

            var anchor = new GameObject("anchor");
            var stopObj = new GameObject("stop");
            toDestroy.Add(anchor);
            toDestroy.Add(stopObj);
            anchor.transform.position = new Vector3(1f, 0f, 2f);
            stopObj.transform.position = new Vector3(5f, 0f, 6f);
            stopObj.transform.rotation = Quaternion.Euler(0f, 90f, 0f);

            var spec = new NpcRoutineSpec
            {
                Kind = NpcRoutineKind.WorkLoop,
                Stops = { new WorkStop { Point = stopObj.transform, Action = WorkStopAction.Work, Seconds = 3f }, new WorkStop { Point = null } },
            };
            var plan = spec.Resolve(anchor.transform);

            Assert.AreEqual(new Vector3(1f, 0f, 2f), plan.Centre, "empty centre falls back to the anchor");
            Assert.AreEqual(1, plan.Stops.Count, "a stop with no transform is dropped");
            Assert.AreEqual(new Vector3(5f, 0f, 6f), plan.Stops[0].Position);
            Assert.AreEqual(1f, plan.Stops[0].Facing.x, 0.001f);
        }

        [Test]
        public void Spec_Reset_ClearsEverythingSoANewSpawnPointNeverInheritsStops()
        {
            var obj = new GameObject("p");
            toDestroy.Add(obj);
            var spec = new NpcRoutineSpec { Kind = NpcRoutineKind.WorkLoop, AreaCentre = obj.transform, Seed = 9, Stops = { new WorkStop { Point = obj.transform } } };

            spec.Reset();

            Assert.AreEqual(NpcRoutineKind.Stand, spec.Kind);
            Assert.IsNull(spec.AreaCentre);
            Assert.AreEqual(0, spec.Stops.Count);
            Assert.AreEqual(0, spec.Seed);
        }
    }
}
