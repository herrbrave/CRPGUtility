using System.Collections;
using System.Collections.Generic;
using System.Linq;
using CRPG.Abilities;
using CRPG.Actors;
using CRPG.Combat;
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
        public IEnumerator AMoverWithNoWayAroundAnOccupiedCellStopsShortReportsBlockedAndCanRetryOnceItIsFree()
        {
            // A one-cell-wide corridor: there is genuinely no detour around the blocker.
            var arena = CreateArena(10, 1, Enumerable.Empty<(int, int)>());
            var blocker = RegisterMover(arena, arena.Center(2, 0), movementMax: 100);
            var walker = RegisterMover(arena, arena.Center(0, 0), movementMax: 100);
            yield return null; // let both claim the cell they stand in

            var request = walker.Motor.SetDestination(arena.Center(5, 0));
            MoveStatus? terminal = null;
            yield return WaitForTerminalStatus(walker.Motor, s => terminal = s);

            Assert.AreEqual(MoveStatus.Blocked, terminal);
            Assert.AreEqual(request.RequestId, walker.Motor.Snapshot.RequestId);
            Assert.Less(Vector3.Distance(arena.Center(1, 0), walker.Transform.position), 1e-4f, "It must stop in its own last cell, not part-way into the occupied one.");
            Assert.IsTrue(grid.IsOccupied(new GridCoordinate(2, 0), out var holder));
            Assert.AreEqual(MotorOf(blocker).OccupantId, holder);
            Assert.IsTrue(grid.IsOccupied(new GridCoordinate(1, 0), out var walkerCell) && walkerCell == MotorOf(walker).OccupantId);
            Assert.IsFalse(grid.IsOccupied(new GridCoordinate(0, 0), out _), "The cell it left must have been released.");

            // The blocker steps aside; the retry now succeeds.
            Assert.IsTrue(blocker.Motor.Warp(arena.Center(9, 0)));
            walker.Motor.SetDestination(arena.Center(5, 0));
            MoveStatus? retry = null;
            yield return WaitForTerminalStatus(walker.Motor, s => retry = s);
            Assert.AreEqual(MoveStatus.Arrived, retry);
        }

        // A diagonal hop from (2,2) to a point near the top-left of cell (3,3) leaves cell (2,2), enters the unclaimed
        // corner cell (2,3), and only then reaches the claimed goal cell (3,3).
        private static Vector3 NearTheTopLeftOfCell(Arena arena, int x, int z) => arena.Center(x, z) + new Vector3(-0.45f, 0f, 0.4f);

        private static IEnumerator WalkUntilInCell(Mover mover, WalkabilityGrid grid, GridCoordinate cell)
        {
            var elapsed = 0f;
            while (grid.ToGrid(mover.Transform.position) != cell && elapsed < 10f)
            {
                yield return null;
                elapsed += Time.deltaTime;
            }

            Assert.AreEqual(cell, grid.ToGrid(mover.Transform.position), "Test setup: the mover never reached the transit cell.");
        }

        [UnityTest]
        public IEnumerator StoppingMidDiagonalHopInAnUnclaimedCornerCellClaimsThatCellInsteadOfHoldingNothing()
        {
            var arena = CreateArena(6, 6, Enumerable.Empty<(int, int)>());
            var mover = RegisterMover(arena, arena.Center(2, 2), movementMax: 100, speed: 2f);
            yield return null;

            mover.Motor.SetDestination(NearTheTopLeftOfCell(arena, 3, 3));
            yield return WalkUntilInCell(mover, grid, new GridCoordinate(2, 3));
            mover.Motor.Stop();

            var id = MotorOf(mover).OccupantId;
            Assert.IsTrue(grid.IsOccupied(new GridCoordinate(2, 3), out var holder) && holder == id, "It must hold the cell it is standing in right now.");
            Assert.IsFalse(grid.IsOccupied(new GridCoordinate(2, 2), out _));
            Assert.IsFalse(grid.IsOccupied(new GridCoordinate(3, 3), out _), "...and exactly that one.");
        }

        [UnityTest]
        public IEnumerator StoppingMidDiagonalHopWhereSomeoneElseHoldsTheCornerCellKeepsWhatItAlreadyHeldNeverNothing()
        {
            var arena = CreateArena(6, 6, Enumerable.Empty<(int, int)>());
            var mover = RegisterMover(arena, arena.Center(2, 2), movementMax: 100, speed: 2f);
            var other = RegisterMover(arena, arena.Center(2, 3), movementMax: 100); // parked in the corner cell
            yield return null;

            mover.Motor.SetDestination(NearTheTopLeftOfCell(arena, 3, 3));
            yield return WalkUntilInCell(mover, grid, new GridCoordinate(2, 3));
            mover.Motor.Stop();

            var id = MotorOf(mover).OccupantId;
            var holdsSomething = (grid.IsOccupied(new GridCoordinate(2, 2), out var a) && a == id)
                                 || (grid.IsOccupied(new GridCoordinate(3, 3), out var b) && b == id);
            Assert.IsTrue(holdsSomething, "It must not drop every claim and end up standing on someone else's cell holding nothing.");
            Assert.IsTrue(grid.IsOccupied(new GridCoordinate(2, 3), out var parked) && parked == MotorOf(other).OccupantId, "The parked character keeps its cell.");
        }

        [UnityTest]
        public IEnumerator AMoverRoutesAroundAStationaryOccupantWhenAnotherWayExists()
        {
            // A parked character in the middle of a 3-wide corridor: the walker waits briefly, then goes around it.
            var arena = CreateArena(12, 3, Enumerable.Empty<(int, int)>());
            var parked = RegisterMover(arena, arena.Center(5, 1), movementMax: 100);
            var walker = RegisterMover(arena, arena.Center(1, 1), movementMax: 100);
            yield return null;
            var destination = arena.Center(10, 1);

            var request = walker.Motor.SetDestination(destination);
            MoveStatus? terminal = null;
            yield return WaitForTerminalStatus(walker.Motor, s => terminal = s);

            Assert.AreEqual(MoveStatus.Arrived, terminal, "There is room to pass: it must go around, not give up.");
            Assert.AreEqual(request.RequestId, walker.Motor.Snapshot.RequestId, "It is still the same request throughout.");
            Assert.Less(Vector3.Distance(destination, walker.Transform.position), 1e-4f);
            Assert.IsTrue(grid.IsOccupied(new GridCoordinate(5, 1), out var stillThere) && stillThere == MotorOf(parked).OccupantId, "The parked character was never disturbed.");
        }

        [UnityTest]
        public IEnumerator AMoverWhoseDestinationCellIsTheOccupiedOneCannotRouteAroundItAndIsBlocked()
        {
            var arena = CreateArena(12, 3, Enumerable.Empty<(int, int)>());
            RegisterMover(arena, arena.Center(8, 1), movementMax: 100); // parked exactly on the destination
            var walker = RegisterMover(arena, arena.Center(1, 1), movementMax: 100);
            yield return null;

            walker.Motor.SetDestination(arena.Center(8, 1));
            MoveStatus? terminal = null;
            yield return WaitForTerminalStatus(walker.Motor, s => terminal = s);

            Assert.AreEqual(MoveStatus.Blocked, terminal, "Re-planning never silently changes where the mover was told to go.");
        }

        [UnityTest]
        public IEnumerator MoversWalkingInSingleFileDirectlyBehindEachOtherAllArriveNoneBlocked()
        {
            // A party leaving in a column in a ONE-cell-wide corridor (so re-planning around the leader is impossible and
            // only waiting can work): each follower is exactly one cell behind the mover ahead, all heading the same
            // way. A follower must be able to step into the cell the leader is in the middle of leaving.
            var arena = CreateArena(20, 1, Enumerable.Empty<(int, int)>());
            var leader = RegisterMover(arena, arena.Center(3, 0), movementMax: 100, speed: 5f);
            var second = RegisterMover(arena, arena.Center(2, 0), movementMax: 100, speed: 5f);
            var third = RegisterMover(arena, arena.Center(1, 0), movementMax: 100, speed: 5f);
            yield return null;

            leader.Motor.SetDestination(arena.Center(15, 0));
            second.Motor.SetDestination(arena.Center(14, 0));
            third.Motor.SetDestination(arena.Center(13, 0));

            var results = new Dictionary<string, MoveStatus>();
            yield return WaitForTerminalStatus(leader.Motor, s => results["leader"] = s);
            yield return WaitForTerminalStatus(second.Motor, s => results["second"] = s);
            yield return WaitForTerminalStatus(third.Motor, s => results["third"] = s);

            Assert.AreEqual(MoveStatus.Arrived, results["leader"]);
            Assert.AreEqual(MoveStatus.Arrived, results["second"], "The follower one cell behind must not be blocked by a mover who is leaving.");
            Assert.AreEqual(MoveStatus.Arrived, results["third"]);
        }

        [UnityTest]
        public IEnumerator NegativeControl_WithNoClaimWaitAFollowerIsBlockedByAMoverWhoIsLeaving()
        {
            // Proves the wait is what makes single file work (in a corridor with no way around): with ClaimWaitSeconds = 0
            // the follower is Blocked at once.
            var arena = CreateArena(20, 1, Enumerable.Empty<(int, int)>());
            var leader = RegisterMover(arena, arena.Center(3, 0), movementMax: 100, speed: 5f);
            var follower = RegisterMover(arena, arena.Center(2, 0), movementMax: 100, speed: 5f);
            MotorOf(follower).ClaimWaitSeconds = 0f;
            yield return null;

            leader.Motor.SetDestination(arena.Center(15, 0));
            follower.Motor.SetDestination(arena.Center(14, 0));

            MoveStatus? result = null;
            yield return WaitForTerminalStatus(follower.Motor, s => result = s);
            Assert.AreEqual(MoveStatus.Blocked, result);
        }

        [UnityTest]
        public IEnumerator TwoMoversMeetingHeadOnInACorridorBothGiveUpAsBlockedInsteadOfHangingForever()
        {
            var arena = CreateArena(12, 1, Enumerable.Empty<(int, int)>());
            var a = RegisterMover(arena, arena.Center(1, 0), movementMax: 100, speed: 8f);
            var b = RegisterMover(arena, arena.Center(10, 0), movementMax: 100, speed: 8f);
            yield return null;

            a.Motor.SetDestination(arena.Center(10, 0));
            b.Motor.SetDestination(arena.Center(1, 0));

            MoveStatus? resultA = null;
            MoveStatus? resultB = null;
            yield return WaitForTerminalStatus(a.Motor, s => resultA = s, maxSimulatedSeconds: 10f);
            yield return WaitForTerminalStatus(b.Motor, s => resultB = s, maxSimulatedSeconds: 10f);

            Assert.AreEqual(MoveStatus.Blocked, resultA);
            Assert.AreEqual(MoveStatus.Blocked, resultB);
            Assert.AreNotEqual(new GridCoordinate(0, 0), grid.ToGrid(a.Transform.position));
        }

        // ------------------------------------------------------------------------------------------------
        // Task 1.5: the real tactical action layer (MoveActionExecution / ActionScheduler / reactions), unchanged
        // ------------------------------------------------------------------------------------------------

        private sealed class CallbackEffect : AbilityEffect
        {
            private readonly System.Action onApply;
            public CallbackEffect(System.Action onApply) => this.onApply = onApply;
            public override void Apply(AbilityExecutionContext context) => onApply();
        }

        [UnityTest]
        public IEnumerator AMidCellThreatCrossingStopsResolvesAnOpportunityAttackAndTheMoverCarriesOnToTheGridDestination()
        {
            var arena = CreateArena(30, 3, Enumerable.Empty<(int, int)>());
            var mover = RegisterMover(arena, arena.Center(0, 1), movementMax: 100, speed: 8f);

            var hp = TacticalTestFactory.CreateResourceDefinition("resource-hp-grid", "Hit Points");
            var reactionResource = TacticalTestFactory.CreateResourceDefinition("resource-reaction-grid", "Reaction");
            var rulesDatabase = new RulesDatabase();

            var damageEffect = new DamageEffect();
            TacticalTestFactory.SetField(damageEffect, "hitPointResource", hp);
            TacticalTestFactory.SetField(damageEffect, "amount", new DiceExpression(1, 2));

            Vector3? firedAt = null;
            var opportunityAttack = TacticalTestFactory.CreateAbility(
                "ability-opportunity-grid", reactionResource, 1, 0f, 0f, tags: AbilityTag.Reaction,
                effects: new AbilityEffect[] { damageEffect, new CallbackEffect(() => firedAt = mover.Transform.position) });
            rulesDatabase.Register(opportunityAttack);
            var feature = TacticalTestFactory.CreateFeatureDefinition("feature-opportunist-grid", "Opportunist", new[] { opportunityAttack.Id });
            rulesDatabase.Register(feature);

            characters.TryGetCharacter(mover.Id, out var moverState);
            moverState.AddResource(new ResourceState(hp.Id, max: 20));

            var reactorId = RuntimeEntityId.NewId();
            var reactor = new CharacterState(reactorId);
            reactor.AddResource(new ResourceState(reactionResource.Id, max: 1));
            reactor.GrantFeature(feature.Id);
            characters.Register(reactor);
            var reactorX = arena.Center(10, 1).x + 0.4f; // deliberately off a cell centre
            positions.Set(reactorId, new Vector3(reactorX, arena.Center(10, 1).y, arena.Center(10, 1).z));

            var damageRandom = new TestRandomSource();
            damageRandom.EnqueueResult(2);
            var reactionService = new ReactionService(
                characters, new AbilityResolutionService(characters, new DiceService(damageRandom)), new FirstEligibleReactionProvider(), reactionResource);
            var reactingResolution = new MovementResolutionService(
                characters, positions, new NoOpMovementController(), reactionService: reactionService, rulesDatabase: rulesDatabase);

            var action = new MoveActionExecution(
                mover.Id, mover.Motor, reactingResolution, arena.Nav, characters, positions, MovementResourceId,
                arena.Center(20, 1), potentialReactors: new[] { reactorId });

            yield return WaitForActionToEnd(action);

            Assert.AreEqual(ActionOutcome.Succeeded, action.Outcome);
            Assert.IsTrue(action.ArrivedAtDestination, "The mover survives the reaction and keeps going to the real destination.");
            Assert.AreEqual(20, MovementSpent(mover, 100), "20 straight cells, spent exactly, despite the mid-route stop.");
            moverState.TryGetResource(hp.Id, out var moverHp);
            Assert.AreEqual(18, moverHp.Current);
            reactor.TryGetResource(reactionResource.Id, out var reactionPool);
            Assert.AreEqual(0, reactionPool.Current);

            Assert.IsTrue(firedAt.HasValue, "The opportunity attack must have resolved.");
            Assert.AreEqual(reactorX + 1.5f, firedAt.Value.x, 1e-3f, "It fires exactly where the mover LEFT the threat range: a mid-cell point, reached exactly by the grid motor.");
            var offCentre = Mathf.Abs(firedAt.Value.x - Mathf.Floor(firedAt.Value.x) - 0.5f);
            Assert.Greater(offCentre, 0.05f, "...and that point is genuinely not a cell centre.");
        }

        [UnityTest]
        public IEnumerator AnOpportunityAttackKillingTheMoverEndsTheGridMoveInterrupted()
        {
            var arena = CreateArena(30, 3, Enumerable.Empty<(int, int)>());
            var mover = RegisterMover(arena, arena.Center(0, 1), movementMax: 100, speed: 8f);

            var reactionResource = TacticalTestFactory.CreateResourceDefinition("resource-reaction-kill-grid", "Reaction");
            var rulesDatabase = new RulesDatabase();
            var killAbility = TacticalTestFactory.CreateAbility(
                "ability-kill-reaction-grid", reactionResource, 1, 0f, 0f, tags: AbilityTag.Reaction,
                effects: new UnregisterEffect(characters, mover.Id));
            rulesDatabase.Register(killAbility);
            var feature = TacticalTestFactory.CreateFeatureDefinition("feature-killer-grid", "Killer", new[] { killAbility.Id });
            rulesDatabase.Register(feature);

            var reactorId = RuntimeEntityId.NewId();
            var reactor = new CharacterState(reactorId);
            reactor.AddResource(new ResourceState(reactionResource.Id, max: 1));
            reactor.GrantFeature(feature.Id);
            characters.Register(reactor);
            positions.Set(reactorId, arena.Center(10, 1));

            var reactionService = new ReactionService(
                characters, new AbilityResolutionService(characters, new DiceService(new TestRandomSource())), new FirstEligibleReactionProvider(), reactionResource);
            var reactingResolution = new MovementResolutionService(
                characters, positions, new NoOpMovementController(), reactionService: reactionService, rulesDatabase: rulesDatabase);

            var action = new MoveActionExecution(
                mover.Id, mover.Motor, reactingResolution, arena.Nav, characters, positions, MovementResourceId,
                arena.Center(20, 1), potentialReactors: new[] { reactorId });

            yield return WaitForActionToEnd(action);

            Assert.AreEqual(ActionOutcome.Interrupted, action.Outcome);
            Assert.IsFalse(action.ArrivedAtDestination);
            Assert.IsFalse(characters.TryGetCharacter(mover.Id, out _));
        }

        [UnityTest]
        public IEnumerator ActionSchedulerRunsAGridMoveAndRefusesASecondWhileItIsBusy()
        {
            var arena = CreateArena(12, 4, Enumerable.Empty<(int, int)>());
            var mover = RegisterMover(arena, arena.Center(1, 1), movementMax: 100, speed: 8f);
            var scheduler = new ActionScheduler(
                new AbilityResolutionService(characters, new DiceService(new TestRandomSource())), movementResolution, arena.Nav, characters, positions);

            var move = scheduler.StartMove(mover.Id, mover.Motor, MovementResourceId, arena.Center(8, 1));

            Assert.IsNotNull(move);
            Assert.IsTrue(scheduler.IsBusy);
            Assert.IsNull(scheduler.StartMove(mover.Id, mover.Motor, MovementResourceId, arena.Center(2, 2)), "One action at a time.");

            var elapsed = 0f;
            while (scheduler.IsBusy && elapsed < 30f)
            {
                scheduler.Tick(Time.deltaTime);
                elapsed += Time.deltaTime;
                yield return null;
            }

            Assert.IsFalse(scheduler.IsBusy);
            Assert.AreEqual(ActionOutcome.Succeeded, move.Outcome);
            Assert.IsTrue(move.ArrivedAtDestination);
            Assert.AreEqual(7, MovementSpent(mover, 100));
        }

        [UnityTest]
        public IEnumerator AMeleeClosingMoveStopsExactlyTheStopDistanceFromTheTarget()
        {
            var arena = CreateArena(20, 3, Enumerable.Empty<(int, int)>());
            var mover = RegisterMover(arena, arena.Center(1, 1), movementMax: 100);
            var targetId = RuntimeEntityId.NewId();
            var targetPosition = arena.Center(12, 1);
            positions.Set(targetId, targetPosition);

            var action = new MoveActionExecution(
                mover.Id, mover.Motor, movementResolution, arena.Nav, characters, positions, MovementResourceId, targetId, stopDistance: 1.5f);

            yield return WaitForActionToEnd(action);

            Assert.AreEqual(ActionOutcome.Succeeded, action.Outcome);
            Assert.IsTrue(action.ArrivedAtDestination);
            Assert.AreEqual(1.5f, Vector3.Distance(mover.Transform.position, targetPosition), 1e-3f,
                "In open ground the destination lands inside a walkable cell, so the motor honours the exact stop distance -- no cell-snapping fuzz.");
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

        [UnityTest]
        public IEnumerator AThreeMemberPartyClickedAcrossAWallThroughANarrowGapAllArrive()
        {
            // The full exploration path with real objects: one click -> PartyExplorationController -> formation
            // resolution (slot avoidance) -> three real grid motors, funnelling through a 2-cell gap in a wall.
            var wall = Enumerable.Range(0, 8).Where(z => z != 3 && z != 4).Select(z => (8, z)).ToList();
            var arena = CreateArena(20, 8, wall);
            var selectables = new TransformSelectableSource();
            var movers = new List<Mover>();
            for (var i = 0; i < 3; i++)
            {
                var mover = RegisterMover(arena, arena.Center(2, 3 + i), movementMax: 100, speed: 6f);
                movers.Add(mover);
                selectables.Add(mover.Id, mover.Transform);
            }

            yield return null;

            var party = new PartyState(movers.Select(m => m.Id));
            var source = new FakeCommandSource();
            var controller = new PartyExplorationController(
                party, new PartyMovementService(service, selectables), new LineFormationSolver(), source, formationSpacing: 1.5f);
            foreach (var mover in movers) controller.RegisterMotor(mover.Id, mover.Motor);

            source.RaiseMove(new MoveCommand(arena.Center(16, 4), Vector3.forward));

            var results = new Dictionary<RuntimeEntityId, MoveStatus>();
            foreach (var mover in movers)
            {
                var id = mover.Id;
                yield return WaitForTerminalStatus(mover.Motor, s => results[id] = s, maxSimulatedSeconds: 40f);
            }

            foreach (var mover in movers)
                Assert.AreEqual(MoveStatus.Arrived, results[mover.Id], "Every member must make it through the gap to its own cell.");
            var finalCells = movers.Select(m => grid.ToGrid(m.Transform.position)).ToList();
            Assert.AreEqual(3, finalCells.Distinct().Count());
            Assert.IsTrue(finalCells.All(c => c.X > 8), "All three ended up on the far side of the wall.");

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
