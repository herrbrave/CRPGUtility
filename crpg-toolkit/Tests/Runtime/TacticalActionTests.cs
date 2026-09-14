using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using CRPG.Abilities;
using CRPG.Actors;
using CRPG.Combat;
using CRPG.Core;
using CRPG.Gameplay.Actions;
using CRPG.Navigation;
using NUnit.Framework;
using Unity.AI.Navigation;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.TestTools;

namespace CRPG.Tests.PlayMode
{
    /// <summary>
    /// Task 4.3: time-based action/movement orchestration (Docs/Design/Production-Foundation.md secs.4.4/6).
    /// Builds AbilityDefinition/ResourceDefinition/FeatureDefinition fixtures via a small local reflection
    /// helper since Tests/Editor's TestDefinitionFactory isn't visible from this PlayMode assembly (mirrors
    /// SessionLoadCoordinatorTests/ConversationControllerTests' own established precedent).
    /// </summary>
    internal static class TacticalTestFactory
    {
        public static void SetField(object target, string fieldName, object value)
        {
            var type = target.GetType();
            while (type != null)
            {
                var field = type.GetField(fieldName, BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.DeclaredOnly);
                if (field != null) { field.SetValue(target, value); return; }
                type = type.BaseType;
            }

            throw new Exception($"Field '{fieldName}' not found on {target.GetType().Name}.");
        }

        public static ResourceDefinition CreateResourceDefinition(string id, string displayName)
        {
            var definition = ScriptableObject.CreateInstance<ResourceDefinition>();
            SetField(definition, "id", id);
            SetField(definition, "displayName", displayName);
            return definition;
        }

        public static FeatureDefinition CreateFeatureDefinition(string id, string displayName, IReadOnlyList<DefinitionId> grantedAbilityIds)
        {
            var definition = ScriptableObject.CreateInstance<FeatureDefinition>();
            SetField(definition, "id", id);
            SetField(definition, "displayName", displayName);
            SetField(definition, "grantedAbilityIds", new List<DefinitionId>(grantedAbilityIds));
            return definition;
        }

        public static AbilityDefinition CreateAbility(
            string id, ResourceDefinition costResource, int costAmount, float prepareSeconds, float recoverSeconds,
            AbilityTag tags = AbilityTag.None, params AbilityEffect[] effects)
        {
            var ability = ScriptableObject.CreateInstance<AbilityDefinition>();
            SetField(ability, "id", id);
            SetField(ability, "displayName", id);
            SetField(ability, "tags", tags);

            object boxedCost = new AbilityCost();
            SetField(boxedCost, "resource", costResource);
            SetField(boxedCost, "amount", costAmount);
            SetField(ability, "costs", new List<AbilityCost> { (AbilityCost)boxedCost });

            var targeting = new TargetingDefinition();
            SetField(targeting, "minTargets", 1);
            SetField(targeting, "maxTargets", 1);
            SetField(ability, "targeting", targeting);

            SetField(ability, "effects", new List<AbilityEffect>(effects));
            SetField(ability, "prepareSeconds", prepareSeconds);
            SetField(ability, "recoverSeconds", recoverSeconds);

            return ability;
        }
    }

    /// <summary>Dictionary-based IPositionProvider -- either a fixed Vector3 (a stationary reactor/destination
    /// marker) or a live Transform (a real moving character), mirroring CRPG.Actors.SceneEntityPositionProvider's
    /// shape closely enough for these tests without depending on TestProject-only code.</summary>
    internal sealed class TestPositionProvider : IPositionProvider
    {
        private readonly Dictionary<RuntimeEntityId, Vector3> fixedPositions = new();
        private readonly Dictionary<RuntimeEntityId, Transform> liveTransforms = new();

        public void Set(RuntimeEntityId id, Vector3 position) => fixedPositions[id] = position;
        public void Track(RuntimeEntityId id, Transform transform) => liveTransforms[id] = transform;

        public Vector3 GetPosition(RuntimeEntityId entity)
        {
            if (liveTransforms.TryGetValue(entity, out var transform)) return transform.position;
            return fixedPositions.TryGetValue(entity, out var position) ? position : Vector3.zero;
        }
    }

    internal sealed class NoOpMovementController : IMovementController
    {
        public float TryMoveTowards(RuntimeEntityId entity, Vector3 destination, float maxDistance) => 0f;
    }

    /// <summary>Test-only effect that removes a character from the registry outright when applied -- the
    /// simplest way to represent "this reaction kills the target" for the death-during-reaction scenario,
    /// without needing a full HP/death-save pipeline. Captures the concrete CharacterRegistry directly rather
    /// than going through AbilityExecutionContext.Characters, which is deliberately read-only
    /// (ICharacterRegistry).</summary>
    internal sealed class UnregisterEffect : AbilityEffect
    {
        private readonly CharacterRegistry registry;
        private readonly RuntimeEntityId victim;

        public UnregisterEffect(CharacterRegistry registry, RuntimeEntityId victim)
        {
            this.registry = registry;
            this.victim = victim;
        }

        public override void Apply(AbilityExecutionContext context) => registry.Unregister(victim);
    }

    public class AbilityActionExecutionTests
    {
        private CharacterRegistry characters;
        private ResourceDefinition costResource;
        private RuntimeEntityId caster;
        private RuntimeEntityId target;
        private AbilityResolutionService abilityResolution;

        [SetUp]
        public void SetUp()
        {
            characters = new CharacterRegistry();
            costResource = TacticalTestFactory.CreateResourceDefinition("resource-action-tac", "Action");

            var casterState = new CharacterState(RuntimeEntityId.NewId());
            casterState.AddResource(new ResourceState(costResource.Id, max: 5));
            characters.Register(casterState);
            caster = casterState.Id;

            var targetState = new CharacterState(RuntimeEntityId.NewId());
            characters.Register(targetState);
            target = targetState.Id;

            abilityResolution = new AbilityResolutionService(characters, new DiceService(new TestRandomSource()));
        }

        [TearDown]
        public void TearDown() => LogAssert.ignoreFailingMessages = false;

        private AbilityDefinition CreateAbility(float prepareSeconds, float recoverSeconds, params AbilityEffect[] effects) =>
            TacticalTestFactory.CreateAbility(Guid.NewGuid().ToString(), costResource, 1, prepareSeconds, recoverSeconds, effects: effects);

        [Test]
        public void PhasesAdvanceThroughPrepareCommitRecoverCompleteWithAuthoredDurations()
        {
            var ability = CreateAbility(1f, 1f);
            var action = new AbilityActionExecution(caster, abilityResolution, ability, new[] { target });

            Assert.AreEqual(ActionPhase.Preparing, action.Phase);

            action.Tick(0.5f);
            Assert.AreEqual(ActionPhase.Preparing, action.Phase);

            action.Tick(0.5f); // Prepare fully elapses -> commits -> Recovering
            Assert.AreEqual(ActionPhase.Recovering, action.Phase);
            Assert.IsTrue(action.ExecutionResult.Succeeded);

            action.Tick(1f); // Recover fully elapses -> Complete
            Assert.AreEqual(ActionPhase.Complete, action.Phase);
            Assert.AreEqual(ActionOutcome.Succeeded, action.Outcome);
        }

        [Test]
        public void OneLargeTickProducesTheSameEndStateAsSeveralSmallerTicksSummingToTheSameTotal()
        {
            var abilityA = CreateAbility(1f, 1f);
            var actionA = new AbilityActionExecution(caster, abilityResolution, abilityA, new[] { target });
            actionA.Tick(2.5f);

            var casterState2 = new CharacterState(RuntimeEntityId.NewId());
            casterState2.AddResource(new ResourceState(costResource.Id, max: 5));
            characters.Register(casterState2);
            var abilityB = CreateAbility(1f, 1f);
            var actionB = new AbilityActionExecution(casterState2.Id, abilityResolution, abilityB, new[] { target });
            for (var i = 0; i < 5; i++) actionB.Tick(0.5f);

            Assert.AreEqual(ActionPhase.Complete, actionA.Phase);
            Assert.AreEqual(actionA.Phase, actionB.Phase);
            Assert.AreEqual(actionA.Outcome, actionB.Outcome);
        }

        [Test]
        public void CancelDuringPreparingCancelsAndSpendsNothing()
        {
            var ability = CreateAbility(1f, 0f);
            var action = new AbilityActionExecution(caster, abilityResolution, ability, new[] { target });

            action.Cancel();

            Assert.AreEqual(ActionOutcome.Cancelled, action.Outcome);
            characters.TryGetCharacter(caster, out var casterState);
            casterState.TryGetResource(costResource.Id, out var resource);
            Assert.AreEqual(5, resource.Current);
        }

        [Test]
        public void CancelDuringRecoveringIsANoOpAndTheActionStillCompletesSuccessfully()
        {
            var ability = CreateAbility(0f, 1f);
            var action = new AbilityActionExecution(caster, abilityResolution, ability, new[] { target });
            action.Tick(0.01f); // Prepare(0s) commits immediately -> Recovering

            Assert.AreEqual(ActionPhase.Recovering, action.Phase);

            action.Cancel();
            Assert.AreEqual(ActionPhase.Recovering, action.Phase);
            Assert.IsNull(action.Outcome);

            action.Tick(1f);
            Assert.AreEqual(ActionOutcome.Succeeded, action.Outcome);
        }

        [Test]
        public void TargetRemovedBeforeCommitIsRejectedNotThrown()
        {
            var ability = CreateAbility(1f, 0f);
            var action = new AbilityActionExecution(caster, abilityResolution, ability, new[] { target });

            characters.Unregister(target);

            Assert.DoesNotThrow(() => action.Tick(1f));
            Assert.AreEqual(ActionOutcome.Rejected, action.Outcome);
        }

        [Test]
        public void AnAbilityMissingARequiredEffectServiceIsRejectedAtCommit()
        {
            var ability = CreateAbility(0f, 0f, new AttackRollEffect());
            var action = new AbilityActionExecution(caster, abilityResolution, ability, new[] { target });

            action.Tick(0.1f);

            Assert.AreEqual(ActionOutcome.Rejected, action.Outcome);
        }

        [Test]
        public void EndedFiresExactlyOnceEvenIfCancelIsCalledAfterNaturalCompletion()
        {
            var ability = CreateAbility(0f, 0f);
            var action = new AbilityActionExecution(caster, abilityResolution, ability, new[] { target });
            var endedCount = 0;
            action.Ended += _ => endedCount++;

            action.Tick(0.01f);

            Assert.AreEqual(ActionOutcome.Succeeded, action.Outcome);
            Assert.AreEqual(1, endedCount);

            action.Cancel();
            Assert.AreEqual(1, endedCount);
        }

        private sealed class RecordingPresentationHook : IActionPresentationHook
        {
            public int CallCount;
            public void RequestActionCue(RuntimeEntityId source, AbilityDefinition ability) => CallCount++;
        }

        private sealed class ThrowingPresentationHook : IActionPresentationHook
        {
            public void RequestActionCue(RuntimeEntityId source, AbilityDefinition ability) => throw new InvalidOperationException("boom");
        }

        [Test]
        public void RequestsThePresentationCueExactlyOnceAtCommitWhenAHookIsWired()
        {
            var ability = CreateAbility(0f, 0.2f);
            var hook = new RecordingPresentationHook();
            var action = new AbilityActionExecution(caster, abilityResolution, ability, new[] { target }, presentationHook: hook);

            action.Tick(0.01f);

            Assert.AreEqual(1, hook.CallCount);
        }

        [Test]
        public void AThrowingPresentationHookDoesNotStrandAnAlreadyCommittedAction()
        {
            LogAssert.ignoreFailingMessages = true; // the hook's exception is expected to be logged, not thrown out
            var ability = CreateAbility(0f, 0.2f);
            var action = new AbilityActionExecution(caster, abilityResolution, ability, new[] { target }, presentationHook: new ThrowingPresentationHook());

            action.Tick(0.3f);

            Assert.AreEqual(ActionOutcome.Succeeded, action.Outcome);
        }

        [Test]
        public void CompletesCorrectlyWithNoPresentationHookWiredAtAllDespiteRealAuthoredDurations()
        {
            var ability = CreateAbility(0.2f, 0.2f);
            var action = new AbilityActionExecution(caster, abilityResolution, ability, new[] { target });

            action.Tick(0.5f);

            Assert.AreEqual(ActionOutcome.Succeeded, action.Outcome);
        }

        [Test]
        public void TickWithZeroDeltaIsATrueNoOpEvenMidPreparing()
        {
            var ability = CreateAbility(1f, 0f);
            var action = new AbilityActionExecution(caster, abilityResolution, ability, new[] { target });

            action.Tick(0.5f);
            for (var i = 0; i < 10; i++) action.Tick(0f);
            Assert.AreEqual(ActionPhase.Preparing, action.Phase);

            action.Tick(0.5f); // only the second REAL tick should finish Preparing
            Assert.AreEqual(ActionOutcome.Succeeded, action.Outcome);
        }
    }

    public class ActionSchedulerTests
    {
        private CharacterRegistry characters;
        private TestPositionProvider positions;
        private ResourceDefinition costResource;
        private RuntimeEntityId caster;
        private RuntimeEntityId target;
        private AbilityResolutionService abilityResolution;
        private MovementResolutionService movementResolution;

        private sealed class FixedInitiativeRule : IInitiativeRule
        {
            private readonly Dictionary<RuntimeEntityId, int> rolls;
            public FixedInitiativeRule(Dictionary<RuntimeEntityId, int> rolls) => this.rolls = rolls;

            public RollResult RollInitiative(RuntimeEntityId combatant) =>
                new(new RollRequest(new DiceExpression(1, 20), source: combatant), new[] { new DieRollResult(20, rolls[combatant], true) }, 0);
        }

        [SetUp]
        public void SetUp()
        {
            characters = new CharacterRegistry();
            positions = new TestPositionProvider();
            costResource = TacticalTestFactory.CreateResourceDefinition("resource-action-sched", "Action");

            var casterState = new CharacterState(RuntimeEntityId.NewId());
            casterState.AddResource(new ResourceState(costResource.Id, max: 5));
            characters.Register(casterState);
            caster = casterState.Id;
            positions.Set(caster, Vector3.zero);

            var targetState = new CharacterState(RuntimeEntityId.NewId());
            characters.Register(targetState);
            target = targetState.Id;
            positions.Set(target, Vector3.one);

            abilityResolution = new AbilityResolutionService(characters, new DiceService(new TestRandomSource()));
            movementResolution = new MovementResolutionService(characters, positions, new NoOpMovementController());
        }

        private CombatEncounterService CreateEncounterService(Dictionary<RuntimeEntityId, int> rolls)
        {
            var initiative = new FixedInitiativeRule(rolls);
            var turnResources = new TurnResourceSet(Array.Empty<ResourceDefinition>());
            var service = new CombatEncounterService(characters, initiative, turnResources);
            service.StartCombat(new List<RuntimeEntityId>(rolls.Keys));
            return service;
        }

        private AbilityDefinition CreateAbility() => TacticalTestFactory.CreateAbility(Guid.NewGuid().ToString(), costResource, 1, 0f, 0f);

        [Test]
        public void StartAbilityIsNotGatedAtAllWithoutAnEncounterService()
        {
            var scheduler = new ActionScheduler(abilityResolution, movementResolution, new NavMeshService(), characters, positions);

            var result = scheduler.StartAbility(caster, CreateAbility(), new[] { target });

            Assert.IsNotNull(result);
        }

        [Test]
        public void StartAbilityIsNotGatedWhenTheEncounterServiceHasNoCombatInProgress()
        {
            var encounterService = new CombatEncounterService(characters, new FixedInitiativeRule(new Dictionary<RuntimeEntityId, int>()), new TurnResourceSet(Array.Empty<ResourceDefinition>()));
            var scheduler = new ActionScheduler(abilityResolution, movementResolution, new NavMeshService(), characters, positions, encounterService);

            var result = scheduler.StartAbility(caster, CreateAbility(), new[] { target });

            Assert.IsNotNull(result);
        }

        [Test]
        public void StartAbilityRejectsANonCurrentTurnCombatant()
        {
            var other = RuntimeEntityId.NewId();
            characters.Register(new CharacterState(other));
            var encounterService = CreateEncounterService(new Dictionary<RuntimeEntityId, int> { [caster] = 20, [other] = 5 });
            var scheduler = new ActionScheduler(abilityResolution, movementResolution, new NavMeshService(), characters, positions, encounterService);

            var result = scheduler.StartAbility(other, CreateAbility(), new[] { target });

            Assert.IsNull(result);
        }

        [Test]
        public void StartAbilityAllowsTheCurrentTurnCombatant()
        {
            var other = RuntimeEntityId.NewId();
            characters.Register(new CharacterState(other));
            var encounterService = CreateEncounterService(new Dictionary<RuntimeEntityId, int> { [caster] = 20, [other] = 5 });
            var scheduler = new ActionScheduler(abilityResolution, movementResolution, new NavMeshService(), characters, positions, encounterService);

            var result = scheduler.StartAbility(caster, CreateAbility(), new[] { target });

            Assert.IsNotNull(result);
        }

        [Test]
        public void StartAbilityRejectsWhenTheEncounterHasNoCurrentTurn()
        {
            var encounterService = CreateEncounterService(new Dictionary<RuntimeEntityId, int> { [caster] = 20 });
            encounterService.Encounter.RemoveCombatant(caster); // Encounter still non-null; CurrentTurn now null

            var scheduler = new ActionScheduler(abilityResolution, movementResolution, new NavMeshService(), characters, positions, encounterService);
            var result = scheduler.StartAbility(caster, CreateAbility(), new[] { target });

            Assert.IsNull(result);
        }

        [Test]
        public void StartMoveRejectsANonCurrentTurnCombatantWithoutTouchingAnyCollaborator()
        {
            var other = RuntimeEntityId.NewId();
            characters.Register(new CharacterState(other));
            var encounterService = CreateEncounterService(new Dictionary<RuntimeEntityId, int> { [caster] = 20, [other] = 5 });
            var scheduler = new ActionScheduler(abilityResolution, movementResolution, new NavMeshService(), characters, positions, encounterService);

            // motor is deliberately null -- the turn gate must reject before ever touching it.
            var result = scheduler.StartMove(other, motor: null, movementResourceId: default, towardTarget: target, stopDistance: 0f);

            Assert.IsNull(result);
        }

        [Test]
        public void RejectsASecondStartWhileAnActionIsAlreadyInFlight()
        {
            var scheduler = new ActionScheduler(abilityResolution, movementResolution, new NavMeshService(), characters, positions);
            var longAbility = TacticalTestFactory.CreateAbility(Guid.NewGuid().ToString(), costResource, 1, 1f, 0f); // real Prepare duration -- stays in flight

            var first = scheduler.StartAbility(caster, longAbility, new[] { target });
            Assert.IsNotNull(first);
            Assert.IsTrue(scheduler.IsBusy);

            var second = scheduler.StartAbility(caster, longAbility, new[] { target });
            Assert.IsNull(second);
        }

        [Test]
        public void ClearsCurrentActionOnceItEnds()
        {
            var scheduler = new ActionScheduler(abilityResolution, movementResolution, new NavMeshService(), characters, positions);
            var ability = TacticalTestFactory.CreateAbility(Guid.NewGuid().ToString(), costResource, 1, 1f, 0f);

            scheduler.StartAbility(caster, ability, new[] { target });
            Assert.IsTrue(scheduler.IsBusy);

            scheduler.Tick(1f);

            Assert.IsFalse(scheduler.IsBusy);
        }
    }

    /// <summary>
    /// Task 4.3: MoveActionExecution against a REAL baked NavMesh (real obstacles, real disconnected geometry,
    /// a real NavMeshCharacterMotor) -- same established convention as CharacterMotionTests/PartyNavigationTests
    /// ("a mock can't distinguish a genuine partial/blocked path from a fabricated one"), duplicated locally per
    /// those files' own precedent of not sharing bake/track helpers across test classes.
    /// </summary>
    public class MoveActionExecutionTests
    {
        private static readonly DefinitionId MovementResourceId = new("resource-movement-tac");

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
            foreach (var go in spawned)
            {
                if (go != null) UnityEngine.Object.DestroyImmediate(go);
            }

            spawned.Clear();
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

        private NavMeshSurface bakeSurface;

        // Correctness fix, confirmed empirically (a test that passes reliably in isolation via -testFilter
        // intermittently got a false Rejected outcome only as part of the full PlayMode suite): pre-existing
        // CharacterMotionTests/PartyNavigationTests (out of this task's scope to modify) never track/destroy
        // their own ground planes either, so NavMeshSurface.BuildNavMesh()'s default CollectObjects.All
        // silently re-bakes over EVERY fixture's leftover static geometry still in the scene whenever this
        // fixture happens to run later in the same continuous PlayMode session -- not a NavMesh "needs a
        // settle frame" timing issue, which was tried first and made no difference. Parenting every plane/
        // wall this fixture creates under one shared surface and collecting only Children makes this
        // fixture's own bakes immune to any other fixture's leaked geometry, past or future, without needing
        // to fix every other file's own test-isolation gap.
        private NavMeshSurface BakeSurface()
        {
            if (bakeSurface == null)
            {
                bakeSurface = Track(new GameObject("NavMeshSurface")).AddComponent<NavMeshSurface>();
                bakeSurface.collectObjects = CollectObjects.Children;
            }

            return bakeSurface;
        }

        private GameObject CreateGroundPlane(Vector3 center, float halfSize)
        {
            var plane = GameObject.CreatePrimitive(PrimitiveType.Plane);
            plane.transform.position = center;
            plane.transform.localScale = new Vector3(halfSize / 5f, 1f, halfSize / 5f);
            plane.isStatic = true;
            plane.transform.SetParent(BakeSurface().transform, worldPositionStays: true);
            return Track(plane);
        }

        private GameObject CreateWallObstacle(Vector3 center, Vector3 size)
        {
            var wall = GameObject.CreatePrimitive(PrimitiveType.Cube);
            wall.transform.position = center;
            wall.transform.localScale = size;
            wall.isStatic = true;
            wall.transform.SetParent(BakeSurface().transform, worldPositionStays: true);
            return Track(wall);
        }

        private void BakeNavMesh() => BakeSurface().BuildNavMesh();

        private readonly struct MoverFixture
        {
            public MoverFixture(RuntimeEntityId id, NavMeshCharacterMotor motor)
            {
                Id = id;
                Motor = motor;
            }

            public RuntimeEntityId Id { get; }
            public NavMeshCharacterMotor Motor { get; }
        }

        private MoverFixture RegisterMover(Vector3 startPosition, int movementMax, float speed = 8f)
        {
            var mover = new CharacterState(RuntimeEntityId.NewId());
            mover.AddResource(new ResourceState(MovementResourceId, max: movementMax));
            characters.Register(mover);

            var go = Track(new GameObject("TacticalMover"));
            go.transform.position = startPosition;
            var agent = go.AddComponent<NavMeshAgent>();
            agent.speed = speed;
            agent.acceleration = 40f;
            var motor = go.AddComponent<NavMeshCharacterMotor>();
            agent.Warp(startPosition);

            positions.Track(mover.Id, go.transform);
            return new MoverFixture(mover.Id, motor);
        }

        private RuntimeEntityId RegisterTarget(Vector3 position)
        {
            var targetState = new CharacterState(RuntimeEntityId.NewId());
            characters.Register(targetState);
            positions.Set(targetState.Id, position);
            return targetState.Id;
        }

        // Budgeted by accumulated Time.deltaTime, not a raw frame count -- see CharacterMotionTests'
        // WaitForTerminalStatus for why (headless -batchmode ticks frames far faster than realtime).
        private static IEnumerator WaitForActionToEnd(ActionExecution action, float maxSimulatedSeconds = 15f)
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

        [UnityTest]
        public IEnumerator AnInitiallyDisconnectedDestinationIsRejectedDuringPlanning()
        {
            CreateGroundPlane(new Vector3(0f, 0f, 0f), 10f);
            CreateGroundPlane(new Vector3(60f, 0f, 0f), 10f); // disconnected island
            BakeNavMesh();

            var mover = RegisterMover(new Vector3(0f, 0f, 0f), movementMax: 100);
            var target = RegisterTarget(new Vector3(60f, 0f, 0f));

            var action = new MoveActionExecution(
                mover.Id, mover.Motor, movementResolution, new NavMeshService(), characters, positions, MovementResourceId, target, stopDistance: 0f);

            Assert.AreEqual(ActionOutcome.Rejected, action.Outcome);
            Assert.AreEqual(ActionPhase.Complete, action.Phase);
            yield break;
        }

        [UnityTest]
        public IEnumerator ABlockageAfterAnAcceptedPathEndsTheActionInterrupted()
        {
            CreateGroundPlane(Vector3.zero, 20f);
            BakeNavMesh();

            var mover = RegisterMover(new Vector3(-8f, 0f, 0f), movementMax: 100, speed: 1f); // slow -- still mid-path
            var target = RegisterTarget(new Vector3(8f, 0f, 0f));
            var action = new MoveActionExecution(
                mover.Id, mover.Motor, movementResolution, new NavMeshService(), characters, positions, MovementResourceId, target, stopDistance: 0f);

            Assert.AreEqual(ActionPhase.Committing, action.Phase);
            action.Tick(Time.deltaTime);
            yield return null;
            action.Tick(Time.deltaTime);
            yield return null;

            mover.Motor.GetComponent<NavMeshAgent>().enabled = false; // simulate a live obstruction mid-flight
            action.Tick(Time.deltaTime);

            Assert.AreEqual(ActionOutcome.Interrupted, action.Outcome);
        }

        [UnityTest]
        public IEnumerator BudgetExhaustedBeforeAnyRealProgressProducesAZeroDistanceSuccessWithoutOverspending()
        {
            CreateGroundPlane(Vector3.zero, 20f);
            BakeNavMesh();

            var mover = RegisterMover(new Vector3(0f, 0f, 0f), movementMax: 0); // nothing to spend at all
            var target = RegisterTarget(new Vector3(8f, 0f, 0f));
            var action = new MoveActionExecution(
                mover.Id, mover.Motor, movementResolution, new NavMeshService(), characters, positions, MovementResourceId, target, stopDistance: 0f);

            yield return WaitForActionToEnd(action);

            Assert.AreEqual(ActionOutcome.Succeeded, action.Outcome);
            Assert.IsFalse(action.ArrivedAtDestination);
            Assert.Less(Vector3.Distance(Vector3.zero, mover.Motor.transform.position), 0.5f);
            characters.TryGetCharacter(mover.Id, out var moverState);
            moverState.TryGetResource(MovementResourceId, out var movement);
            Assert.AreEqual(0, movement.Current);
        }

        [UnityTest]
        public IEnumerator BudgetExhaustedPartwayThroughAMultiSegmentPathClipsAndSettlesCumulativelyNotPerCorner()
        {
            CreateGroundPlane(Vector3.zero, 20f);
            CreateWallObstacle(new Vector3(0f, 1f, 0f), new Vector3(16f, 2f, 1f)); // forces a real detour
            BakeNavMesh();

            var start = new Vector3(-8f, 0f, -5f);
            var mover = RegisterMover(start, movementMax: 5, speed: 8f); // clearly less than the full detour distance
            var target = RegisterTarget(new Vector3(8f, 0f, 5f));
            var action = new MoveActionExecution(
                mover.Id, mover.Motor, movementResolution, new NavMeshService(), characters, positions, MovementResourceId, target, stopDistance: 0f);

            yield return WaitForActionToEnd(action);

            Assert.AreEqual(ActionOutcome.Succeeded, action.Outcome);
            Assert.IsFalse(action.ArrivedAtDestination, "5 units of budget cannot cover this detour -- it must stop short.");

            characters.TryGetCharacter(mover.Id, out var moverState);
            moverState.TryGetResource(MovementResourceId, out var movement);
            Assert.AreEqual(0, movement.Current, "The full affordable budget must be spent, not left partially unspent.");

            var traveled = Vector3.Distance(start, mover.Motor.transform.position);
            Assert.LessOrEqual(traveled, 5.5f, "Must not travel meaningfully further than the affordable budget.");
        }

        [UnityTest]
        public IEnumerator ALongStraightSegmentStopsAndResolvesAReactionAtTheThreatBoundaryNotAtTheFarCorner()
        {
            CreateGroundPlane(new Vector3(10f, 0f, 0f), 30f);
            BakeNavMesh();

            var mover = RegisterMover(new Vector3(0f, 0f, 0f), movementMax: 100, speed: 8f);
            var target = RegisterTarget(new Vector3(20f, 0f, 0f));

            var hp = TacticalTestFactory.CreateResourceDefinition("resource-hp-tac", "Hit Points");
            var reactionResource = TacticalTestFactory.CreateResourceDefinition("resource-reaction-tac", "Reaction");
            var rulesDatabase = new RulesDatabase();

            var damageEffect = new DamageEffect();
            TacticalTestFactory.SetField(damageEffect, "hitPointResource", hp);
            TacticalTestFactory.SetField(damageEffect, "amount", new DiceExpression(1, 2));

            var opportunityAttack = TacticalTestFactory.CreateAbility(
                "ability-opportunity-tac", reactionResource, 1, 0f, 0f, tags: AbilityTag.Reaction, effects: damageEffect);
            rulesDatabase.Register(opportunityAttack);

            var feature = TacticalTestFactory.CreateFeatureDefinition("feature-opportunist-tac", "Opportunist", new[] { opportunityAttack.Id });
            rulesDatabase.Register(feature);

            characters.TryGetCharacter(mover.Id, out var moverState);
            moverState.AddResource(new ResourceState(hp.Id, max: 20));

            var reactorId = RuntimeEntityId.NewId();
            var reactor = new CharacterState(reactorId);
            reactor.AddResource(new ResourceState(reactionResource.Id, max: 1));
            reactor.GrantFeature(feature.Id);
            characters.Register(reactor);
            positions.Set(reactorId, new Vector3(10f, 0f, 0f)); // well before the segment's far end at x=20

            var damageRandom = new TestRandomSource();
            damageRandom.EnqueueResult(2);
            var reactionAbilityResolution = new AbilityResolutionService(characters, new DiceService(damageRandom));
            var reactionService = new ReactionService(characters, reactionAbilityResolution, new FirstEligibleReactionProvider(), reactionResource);
            var movingResolution = new MovementResolutionService(characters, positions, new NoOpMovementController(), reactionService: reactionService, rulesDatabase: rulesDatabase);

            var action = new MoveActionExecution(
                mover.Id, mover.Motor, movingResolution, new NavMeshService(), characters, positions, MovementResourceId, target, stopDistance: 0f,
                potentialReactors: new[] { reactorId });

            yield return WaitForActionToEnd(action);

            Assert.AreEqual(ActionOutcome.Succeeded, action.Outcome);
            Assert.IsTrue(action.ArrivedAtDestination, "The mover survives the reaction and should keep going to the real destination.");
            moverState.TryGetResource(hp.Id, out var moverHp);
            Assert.AreEqual(18, moverHp.Current);
            reactor.TryGetResource(reactionResource.Id, out var reactionPool);
            Assert.AreEqual(0, reactionPool.Current);
        }

        [UnityTest]
        public IEnumerator AReactorsOpportunityAttackKillingTheMoverEndsTheActionInterrupted()
        {
            CreateGroundPlane(new Vector3(10f, 0f, 0f), 30f);
            BakeNavMesh();

            var mover = RegisterMover(new Vector3(0f, 0f, 0f), movementMax: 100, speed: 8f);
            var target = RegisterTarget(new Vector3(20f, 0f, 0f));

            var reactionResource = TacticalTestFactory.CreateResourceDefinition("resource-reaction-kill-tac", "Reaction");
            var rulesDatabase = new RulesDatabase();

            var killAbility = TacticalTestFactory.CreateAbility(
                "ability-kill-reaction-tac", reactionResource, 1, 0f, 0f, tags: AbilityTag.Reaction,
                effects: new UnregisterEffect(characters, mover.Id));
            rulesDatabase.Register(killAbility);

            var feature = TacticalTestFactory.CreateFeatureDefinition("feature-killer-tac", "Killer", new[] { killAbility.Id });
            rulesDatabase.Register(feature);

            var reactorId = RuntimeEntityId.NewId();
            var reactor = new CharacterState(reactorId);
            reactor.AddResource(new ResourceState(reactionResource.Id, max: 1));
            reactor.GrantFeature(feature.Id);
            characters.Register(reactor);
            positions.Set(reactorId, new Vector3(10f, 0f, 0f));

            var reactionAbilityResolution = new AbilityResolutionService(characters, new DiceService(new TestRandomSource()));
            var reactionService = new ReactionService(characters, reactionAbilityResolution, new FirstEligibleReactionProvider(), reactionResource);
            var movingResolution = new MovementResolutionService(characters, positions, new NoOpMovementController(), reactionService: reactionService, rulesDatabase: rulesDatabase);

            var action = new MoveActionExecution(
                mover.Id, mover.Motor, movingResolution, new NavMeshService(), characters, positions, MovementResourceId, target, stopDistance: 0f,
                potentialReactors: new[] { reactorId });

            yield return WaitForActionToEnd(action);

            Assert.AreEqual(ActionOutcome.Interrupted, action.Outcome);
            Assert.IsFalse(action.ArrivedAtDestination);
            Assert.IsFalse(characters.TryGetCharacter(mover.Id, out _));
        }

        [UnityTest]
        public IEnumerator CancelMidTraversalSettlesDistanceAndStopsTheMotorItOwns()
        {
            CreateGroundPlane(Vector3.zero, 20f);
            BakeNavMesh();

            var mover = RegisterMover(new Vector3(-8f, 0f, 0f), movementMax: 100, speed: 1f); // slow
            var target = RegisterTarget(new Vector3(8f, 0f, 0f));
            var action = new MoveActionExecution(
                mover.Id, mover.Motor, movementResolution, new NavMeshService(), characters, positions, MovementResourceId, target, stopDistance: 0f);

            action.Tick(Time.deltaTime);
            yield return null;
            yield return null; // let real movement genuinely begin

            action.Cancel();

            Assert.AreEqual(ActionOutcome.Cancelled, action.Outcome);
            Assert.AreEqual(MoveStatus.Cancelled, mover.Motor.Snapshot.Status);
            characters.TryGetCharacter(mover.Id, out var moverState);
            moverState.TryGetResource(MovementResourceId, out var movement);
            Assert.Less(movement.Current, 100, "Partial progress must be spent, not left free.");
        }

        [UnityTest]
        public IEnumerator ARealTimeScalePauseFreezesMovementCompletely()
        {
            CreateGroundPlane(Vector3.zero, 20f);
            BakeNavMesh();

            var mover = RegisterMover(new Vector3(-8f, 0f, 0f), movementMax: 100, speed: 4f);
            var target = RegisterTarget(new Vector3(8f, 0f, 0f));
            var action = new MoveActionExecution(
                mover.Id, mover.Motor, movementResolution, new NavMeshService(), characters, positions, MovementResourceId, target, stopDistance: 0f);

            action.Tick(Time.deltaTime);
            yield return null;
            yield return null;
            yield return null; // let real movement genuinely begin

            var positionBeforePause = mover.Motor.transform.position;
            var remainingBeforePause = mover.Motor.Snapshot.RemainingDistance;

            Time.timeScale = 0f;
            try
            {
                for (var i = 0; i < 5; i++)
                {
                    action.Tick(Time.deltaTime); // Time.deltaTime reads 0 while timeScale is 0
                    yield return null;
                }

                Assert.AreEqual(positionBeforePause, mover.Motor.transform.position);
                Assert.AreEqual(remainingBeforePause, mover.Motor.Snapshot.RemainingDistance, 0.0001f);
            }
            finally
            {
                Time.timeScale = 1f;
            }
        }
    }
}
