using System.Collections;
using System.Collections.Generic;
using CRPG.Actors;
using CRPG.Combat;
using CRPG.Core;
using CRPG.Gameplay;
using CRPG.Gameplay.Actions;
using CRPG.Navigation;
using CRPG.Navigation.Grid;
using CRPG.Presentation.Sprites;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace CRPG.Tests.PlayMode
{
    /// <summary>
    /// 2D campaign Task 2.1: RockingMotionDriver wired to a motor Snapshot. Deterministic cases use a fake motor;
    /// the integration cases use the real GridCharacterMotor driven by the real MoveActionExecution, which is what
    /// produces the one-cell legs (and their 2-3 frame zero-velocity gaps) the coast exists for.
    /// </summary>
    public class RockingMotionDriverTests
    {
        private sealed class FakeMovable : MonoBehaviour, IMovable
        {
            public MotionSnapshot Current = MotionSnapshot.None;
            public MoveResult SetDestination(Vector3 destination) => default;
            public void Stop() { }
            public bool Warp(Vector3 position) => true;
            public MotionSnapshot Snapshot => Current;
        }

        private readonly List<GameObject> spawned = new();

        [TearDown]
        public void TearDown()
        {
            Time.timeScale = 1f;
            foreach (var go in spawned) if (go != null) Object.DestroyImmediate(go);
            spawned.Clear();
        }

        private GameObject Make(string name, Transform parent = null)
        {
            var go = new GameObject(name);
            if (parent != null) go.transform.SetParent(parent, false);
            spawned.Add(go);
            return go;
        }

        /// <summary>CharacterRoot (fake motor) -> Card -> RockPivot, with the driver on the card. Built inactive so the
        /// driver's Awake sees the finished hierarchy.</summary>
        private (FakeMovable motor, RockingMotionDriver driver, Transform pivot, Transform root) BuildFake()
        {
            var root = Make("CharacterRoot");
            root.SetActive(false);
            var motor = root.AddComponent<FakeMovable>();
            var card = Make("Card", root.transform);
            var pivot = Make("RockPivot", card.transform).transform;
            var driver = card.AddComponent<RockingMotionDriver>();
            driver.RockPivot = pivot;
            root.SetActive(true);
            return (motor, driver, pivot, root.transform);
        }

        private static MotionSnapshot Moving(Vector3 velocity, MoveStatus status = MoveStatus.Moving) =>
            new(velocity, velocity, true, 5f, status, MoveRequestId.None);

        private static IEnumerator Frames(int count)
        {
            for (var i = 0; i < count; i++) yield return null;
        }

        private static IEnumerator Seconds(float simulated)
        {
            var elapsed = 0f;
            while (elapsed < simulated)
            {
                elapsed += Time.deltaTime;
                yield return null;
            }
        }

        [UnityTest]
        public IEnumerator VelocityTiltsThePivotAndIdleLeavesItExactlyLevel()
        {
            var (motor, driver, pivot, _) = BuildFake();

            yield return Frames(5);
            Assert.AreEqual(Quaternion.identity, pivot.localRotation, "idle: exactly level");

            motor.Current = Moving(new Vector3(2f, 0f, 0f));
            yield return Seconds(0.6f);
            Assert.IsTrue(driver.Motion.IsMoving);
            Assert.Greater(driver.Motion.Amplitude, 0.9f);
            // Quaternion.Equals is exact component equality and the Transform stores a normalized copy, so compare by angle.
            Assert.Less(Quaternion.Angle(Quaternion.Euler(0f, 0f, driver.Motion.TiltDegrees), pivot.localRotation), 0.01f, "the pivot shows the state's tilt about the card normal");

            motor.Current = MotionSnapshot.None;
            yield return Seconds(1.5f);
            Assert.IsFalse(driver.Motion.IsMoving);
            Assert.AreEqual(Quaternion.identity, pivot.localRotation, "settles to exactly level");
        }

        [UnityTest]
        public IEnumerator AMovingStatusWithZeroVelocityNeverTilts()
        {
            // The claim-wait shape: the motor is Moving but standing still. The Phase 1 hand-off rule: key off velocity, never status.
            var (motor, driver, pivot, _) = BuildFake();
            motor.Current = Moving(Vector3.zero, MoveStatus.Moving);

            yield return Seconds(1f);

            Assert.IsFalse(driver.Motion.IsMoving);
            Assert.AreEqual(Quaternion.identity, pivot.localRotation);
        }

        [UnityTest]
        public IEnumerator VerticalOnlyVelocityDoesNotTilt()
        {
            var (motor, driver, pivot, _) = BuildFake();
            motor.Current = Moving(new Vector3(0f, 5f, 0f));

            yield return Seconds(1f);

            Assert.IsFalse(driver.Motion.IsMoving);
            Assert.AreEqual(Quaternion.identity, pivot.localRotation);
        }

        [UnityTest]
        public IEnumerator ATimeScaleOfZeroFreezesTheRock()
        {
            var (motor, driver, pivot, _) = BuildFake();
            motor.Current = Moving(new Vector3(2f, 0f, 0f));
            yield return Seconds(0.5f);
            Assert.IsTrue(driver.Motion.IsMoving);

            Time.timeScale = 0f;
            yield return null;
            var frozenPhase = driver.Motion.Phase;
            var frozenRotation = pivot.localRotation;
            for (var i = 0; i < 10; i++) yield return null;

            Assert.AreEqual(frozenPhase, driver.Motion.Phase, "no phase advance while paused");
            Assert.AreEqual(frozenRotation, pivot.localRotation);

            Time.timeScale = 1f;
            yield return Seconds(0.3f);
            Assert.AreNotEqual(frozenPhase, driver.Motion.Phase, "and it resumes");
        }

        [UnityTest]
        public IEnumerator DisablingLevelsThePivotAndResetsTheStateAndEnablingStartsLevel()
        {
            var (motor, driver, pivot, _) = BuildFake();
            motor.Current = Moving(new Vector3(2f, 0f, 0f));
            yield return Seconds(0.5f);
            Assert.AreNotEqual(Quaternion.identity, pivot.localRotation);

            driver.enabled = false;
            Assert.AreEqual(Quaternion.identity, pivot.localRotation);
            Assert.AreEqual(0f, driver.Motion.Amplitude);
            Assert.IsFalse(driver.Motion.IsMoving);

            motor.Current = MotionSnapshot.None;
            driver.enabled = true;
            yield return Frames(3);
            Assert.AreEqual(Quaternion.identity, pivot.localRotation, "re-enabled level, not mid-tilt");
        }

        [UnityTest]
        public IEnumerator DestroyingThePivotThenDisablingTheDriverThrowsNothing()
        {
            var (motor, driver, pivot, _) = BuildFake();
            motor.Current = Moving(new Vector3(2f, 0f, 0f));
            yield return Frames(5);

            Object.DestroyImmediate(pivot.gameObject);
            Assert.DoesNotThrow(() => driver.enabled = false);
            LogAssert.NoUnexpectedReceived();
        }

        [UnityTest]
        public IEnumerator AMissingMovableWarnsOnceAndIdlesAndAMovableAddedLaterIsPickedUp()
        {
            var root = Make("CharacterRoot");
            var card = Make("Card", root.transform);
            var pivot = Make("RockPivot", card.transform).transform;
            LogAssert.Expect(LogType.Warning, new System.Text.RegularExpressions.Regex("No IMovable found"));
            var driver = card.AddComponent<RockingMotionDriver>();
            driver.RockPivot = pivot;

            yield return Frames(10);
            Assert.AreEqual(Quaternion.identity, pivot.localRotation);
            LogAssert.NoUnexpectedReceived(); // exactly the one expected warning, no per-frame spam

            var motor = root.AddComponent<FakeMovable>();
            motor.Current = Moving(new Vector3(2f, 0f, 0f));
            yield return Seconds(0.5f);
            Assert.IsTrue(driver.Motion.IsMoving, "picked up the next frame, no delay");
        }

        [UnityTest]
        public IEnumerator AMissingPivotWarnsOnceAndNeverThrows()
        {
            var root = Make("CharacterRoot");
            root.AddComponent<FakeMovable>();
            var card = Make("Card", root.transform);
            LogAssert.Expect(LogType.Warning, new System.Text.RegularExpressions.Regex("No rockPivot"));
            card.AddComponent<RockingMotionDriver>();

            yield return Frames(10);

            LogAssert.NoUnexpectedReceived();
        }

        [UnityTest]
        public IEnumerator RockingNeverMovesCharacterRootOrItsCollider()
        {
            var (motor, driver, pivot, root) = BuildFake();
            var collider = root.gameObject.AddComponent<BoxCollider>();
            var position = root.position;
            var rotation = root.rotation;
            var bounds = collider.bounds;
            motor.Current = Moving(new Vector3(2f, 0f, 0f));

            yield return Seconds(1f);

            Assert.Greater(driver.Motion.Amplitude, 0.9f);
            Assert.AreEqual(position, root.position);
            Assert.AreEqual(rotation, root.rotation);
            Assert.AreEqual(bounds, collider.bounds, "click targeting can never mis-hit a tilted sprite");
        }

        // ---------------------------------------------------------------------------------------------
        // Integration: real GridCharacterMotor + real MoveActionExecution
        // ---------------------------------------------------------------------------------------------

        private static readonly Vector3 ArenaOrigin = new(4000f, 0f, 4000f);
        private static readonly DefinitionId MovementResource = new("resource-movement-rocking");

        private sealed class GridRig
        {
            public GridCharacterMotor Motor;
            public RockingMotionDriver Driver;
            public Transform Pivot;
            public GridPathfindingService Nav;
            public WalkabilityGrid Grid;
            public CharacterRegistry Characters;
            public TestPositionProvider Positions;
            public MovementResolutionService Resolution;
            public RuntimeEntityId Id;
        }

        private GridRig BuildGridRig(float speed = 4f, int movementMax = 100, params GridCoordinate[] blocked)
        {
            var rig = new GridRig
            {
                Grid = new WalkabilityGrid(ArenaOrigin, 1f, 12, 3, blocked),
                Nav = new GridPathfindingService(),
                Characters = new CharacterRegistry(),
                Positions = new TestPositionProvider(),
            };
            rig.Nav.SetActiveGrid(rig.Grid);
            rig.Resolution = new MovementResolutionService(rig.Characters, rig.Positions, new NoOpMovementController());

            rig.Id = RuntimeEntityId.NewId();
            var state = new CharacterState(rig.Id);
            state.AddResource(new ResourceState(MovementResource, max: movementMax));
            rig.Characters.Register(state);

            var root = Make("GridCharacter");
            root.SetActive(false);
            root.transform.position = rig.Grid.ToWorld(new GridCoordinate(0, 1));
            rig.Motor = root.AddComponent<GridCharacterMotor>();
            rig.Motor.Pathfinding = rig.Nav;
            rig.Motor.Speed = speed;
            var card = Make("Card", root.transform);
            rig.Pivot = Make("RockPivot", card.transform).transform;
            rig.Driver = card.AddComponent<RockingMotionDriver>();
            rig.Driver.RockPivot = rig.Pivot;
            root.SetActive(true);
            rig.Positions.Track(rig.Id, root.transform);
            return rig;
        }

        private static MoveActionExecution Move(GridRig rig, GridCoordinate to) =>
            new(rig.Id, rig.Motor, rig.Resolution, rig.Nav, rig.Characters, rig.Positions, MovementResource, rig.Grid.ToWorld(to));

        [UnityTest]
        public IEnumerator ARealGridMoveRocksWhileMovingAndSettlesLevelAfterArrival()
        {
            var rig = BuildGridRig();
            yield return null;

            var request = rig.Motor.SetDestination(rig.Grid.ToWorld(new GridCoordinate(8, 1)));
            Assert.AreNotEqual(MoveStatus.Invalid, request.Status);
            var sawRocking = false;
            var elapsed = 0f;
            do
            {
                elapsed += Time.deltaTime;
                yield return null;
                if (rig.Driver.Motion.IsMoving && rig.Driver.Motion.Amplitude > 0.5f) sawRocking = true;
            } while (elapsed < 30f && rig.Motor.Snapshot.Status != MoveStatus.Arrived);

            Assert.IsTrue(sawRocking, "rocked during the move");
            yield return Seconds(1.5f);
            Assert.IsFalse(rig.Driver.Motion.IsMoving);
            Assert.AreEqual(Quaternion.identity, rig.Pivot.localRotation, "level once settled");
        }

        [UnityTest]
        public IEnumerator OneCellLegsThroughTheRealMoveActionExecutionNeverBreakTheRock()
        {
            // Two staggered walls force a serpentine path: every corner is a stop, and every stop is its own leg.
            var rig = BuildGridRig(4f, 100, new GridCoordinate(3, 0), new GridCoordinate(3, 1), new GridCoordinate(6, 1), new GridCoordinate(6, 2));
            yield return null;

            var action = Move(rig, new GridCoordinate(9, 1));
            var elapsed = 0f;
            var zeroRun = 0;
            var maxZeroRun = 0;
            var sawMovement = false;
            var movingFrames = 0;
            var brokeAtFrame = -1;
            var frame = 0;
            while (elapsed < 30f && !action.Outcome.HasValue)
            {
                action.Tick(Time.deltaTime);
                elapsed += Time.deltaTime;
                yield return null;
                frame++;

                var speed = rig.Motor.Snapshot.Velocity.magnitude;
                if (speed > 0.01f) { sawMovement = true; zeroRun = 0; }
                else if (sawMovement) { zeroRun++; maxZeroRun = Mathf.Max(maxZeroRun, zeroRun); }
                if (sawMovement) movingFrames++;

                // Once the character has started moving (allowing the one-frame read lag of Update order) it must keep
                // rocking until the whole action ends.
                if (movingFrames > 3 && !action.Outcome.HasValue && !rig.Driver.Motion.IsMoving && brokeAtFrame < 0) brokeAtFrame = frame;
            }

            Assert.IsTrue(action.Outcome.HasValue, "the move finished");
            Assert.IsTrue(sawMovement);
            Assert.AreEqual(-1, brokeAtFrame, $"the rock stopped mid-action at frame {brokeAtFrame} (a leg gap broke it); longest zero-velocity run was {maxZeroRun} frames");
            Debug.Log($"[RockingMotionDriverTests] longest zero-velocity run across leg boundaries: {maxZeroRun} frames");
            Assert.Greater(maxZeroRun, 0, "the scenario really did produce zero-velocity gaps between legs (otherwise this test proves nothing)");
            Assert.Less(maxZeroRun, rig.Driver.Settings.coastMinFrames + 1, "the measured leg gap is inside the coast's frame floor");

            yield return Seconds(1.5f);
            Assert.IsFalse(rig.Driver.Motion.IsMoving);
            Assert.AreEqual(Quaternion.identity, rig.Pivot.localRotation);
        }
    }
}
