using System.Collections.Generic;
using CRPG.Abilities;
using CRPG.Actors;
using CRPG.Combat;
using CRPG.Core;
using NUnit.Framework;
using UnityEngine;

namespace CRPG.Tests.EditMode
{
    public class MovementResolutionServiceTests
    {
        private sealed class FakeMovementController : IMovementController
        {
            private readonly FakePositionProvider positions;
            public FakeMovementController(FakePositionProvider positions) => this.positions = positions;

            public float RequestedMaxDistance { get; private set; } = -1f;
            public float DistanceToReturn { get; set; }

            public float TryMoveTowards(RuntimeEntityId entity, Vector3 destination, float maxDistance)
            {
                RequestedMaxDistance = maxDistance;
                var current = positions.GetPosition(entity);
                positions.Set(entity, Vector3.MoveTowards(current, destination, DistanceToReturn));
                return DistanceToReturn;
            }
        }

        private sealed class FixedMovementSpeedRule : IMovementSpeedRule
        {
            private readonly float speed;
            public FixedMovementSpeedRule(float speed) => this.speed = speed;
            public float LastBaseSpeedSeen { get; private set; }
            public float ComputeEffectiveSpeed(RuntimeEntityId entity, float baseSpeed) { LastBaseSpeedSeen = baseSpeed; return speed; }
        }

        private static readonly DefinitionId MovementResourceId = new("resource-movement");

        private CharacterRegistry characters;
        private FakePositionProvider positions;
        private FakeMovementController movementController;
        private CombatLog combatLog;
        private MovementResolutionService service;
        private RuntimeEntityId self;
        private RuntimeEntityId target;
        private CharacterState selfCharacter;

        [SetUp]
        public void SetUp()
        {
            characters = new CharacterRegistry();
            positions = new FakePositionProvider();
            movementController = new FakeMovementController(positions);
            combatLog = new CombatLog();
            service = new MovementResolutionService(characters, positions, movementController, combatLog);

            self = RuntimeEntityId.NewId();
            target = RuntimeEntityId.NewId();
            selfCharacter = new CharacterState(self);
            characters.Register(selfCharacter);
            characters.Register(new CharacterState(target));

            positions.Set(self, Vector3.zero);
            positions.Set(target, new Vector3(10f, 0f, 0f));
        }

        [Test]
        public void ClampsRequestedDistanceToWhicheverIsSmallerOfBudgetAndDesiredDistance()
        {
            selfCharacter.AddResource(new ResourceState(MovementResourceId, max: 3));
            movementController.DistanceToReturn = 3f;

            // 10 units away, stop 1 short (desired 9) -- budget (3) is the smaller number.
            service.MoveToward(self, target, MovementResourceId, stopDistance: 1f);

            Assert.AreEqual(3f, movementController.RequestedMaxDistance);
        }

        [Test]
        public void SpeedRuleCapsMovementEvenWhenBudgetHasMoreLeft()
        {
            selfCharacter.AddResource(new ResourceState(MovementResourceId, max: 10));
            movementController.DistanceToReturn = 2f;
            var speedRule = new FixedMovementSpeedRule(2f); // e.g. Grappled/Exhaustion capping well below the 10 remaining
            var cappedService = new MovementResolutionService(characters, positions, movementController, combatLog, speedRule: speedRule);

            cappedService.MoveToward(self, target, MovementResourceId, stopDistance: 1f);

            Assert.AreEqual(2f, movementController.RequestedMaxDistance);
            Assert.AreEqual(10f, speedRule.LastBaseSpeedSeen); // seeded from the resource's Max, not its remaining Current
        }

        [Test]
        public void CanMoveIsFalseWhenSpeedRuleCapsToZero()
        {
            selfCharacter.AddResource(new ResourceState(MovementResourceId, max: 10));
            var speedRule = new FixedMovementSpeedRule(0f);
            var cappedService = new MovementResolutionService(characters, positions, movementController, combatLog, speedRule: speedRule);

            Assert.IsFalse(cappedService.CanMove(self, MovementResourceId));
        }

        [Test]
        public void SpendsExactlyWhatTheControllerReportsWasActuallyMoved()
        {
            selfCharacter.AddResource(new ResourceState(MovementResourceId, max: 6));
            movementController.DistanceToReturn = 4f; // controller only managed to move 4 of the requested distance

            service.MoveToward(self, target, MovementResourceId, stopDistance: 1f);

            selfCharacter.TryGetResource(MovementResourceId, out var movement);
            Assert.AreEqual(2, movement.Current); // 6 - ceil(4) = 2
        }

        [Test]
        public void DoesNothingWhenNoMovementResourceIsLeft()
        {
            selfCharacter.AddResource(new ResourceState(MovementResourceId, max: 6, current: 0));

            service.MoveToward(self, target, MovementResourceId, stopDistance: 1f);

            Assert.AreEqual(-1f, movementController.RequestedMaxDistance); // controller was never called
        }

        [Test]
        public void DoesNothingWhenAlreadyWithinStopDistance()
        {
            selfCharacter.AddResource(new ResourceState(MovementResourceId, max: 6));
            positions.Set(target, new Vector3(0.5f, 0f, 0f)); // closer than stopDistance

            service.MoveToward(self, target, MovementResourceId, stopDistance: 1f);

            Assert.AreEqual(-1f, movementController.RequestedMaxDistance);
        }

        [Test]
        public void LogsAMovementEntryWhenMovementOccurs()
        {
            selfCharacter.AddResource(new ResourceState(MovementResourceId, max: 6));
            movementController.DistanceToReturn = 5f;

            service.MoveToward(self, target, MovementResourceId, stopDistance: 1f);

            Assert.AreEqual(1, combatLog.Entries.Count);
            Assert.IsInstanceOf<MovementLogEntry>(combatLog.Entries[0]);
        }

        [Test]
        public void LeavingAThreatenedAreaTriggersAnOpportunityAttack()
        {
            var hp = TestDefinitionFactory.CreateResourceDefinition("resource-hp", "Hit Points");
            var reactionResource = TestDefinitionFactory.CreateResourceDefinition("resource-reaction", "Reaction");
            var rulesDatabase = new RulesDatabase();

            var damageEffect = new DamageEffect();
            TestDefinitionFactory.SetPrivateField(damageEffect, "hitPointResource", hp);
            TestDefinitionFactory.SetPrivateField(damageEffect, "amount", new DiceExpression(1, 2)); // 1d2, deterministic via TestRandomSource

            var opportunityAttack = ScriptableObject.CreateInstance<AbilityDefinition>();
            TestDefinitionFactory.SetPrivateField(opportunityAttack, "id", "ability-opportunity-attack");
            TestDefinitionFactory.SetPrivateField(opportunityAttack, "tags", AbilityTag.Reaction);
            object boxedCost = new AbilityCost();
            TestDefinitionFactory.SetPrivateField(boxedCost, "resource", reactionResource);
            TestDefinitionFactory.SetPrivateField(boxedCost, "amount", 1);
            TestDefinitionFactory.SetPrivateField(opportunityAttack, "costs", new List<AbilityCost> { (AbilityCost)boxedCost });
            var targeting = new TargetingDefinition();
            TestDefinitionFactory.SetPrivateField(targeting, "minTargets", 1);
            TestDefinitionFactory.SetPrivateField(targeting, "maxTargets", 1);
            TestDefinitionFactory.SetPrivateField(opportunityAttack, "targeting", targeting);
            TestDefinitionFactory.SetPrivateField(opportunityAttack, "effects", new List<AbilityEffect> { damageEffect });
            rulesDatabase.Register(opportunityAttack);

            var feature = TestDefinitionFactory.CreateFeatureDefinition("feature-opportunist", "Opportunist");
            TestDefinitionFactory.SetPrivateField(feature, "grantedAbilityIds", new List<DefinitionId> { opportunityAttack.Id });
            rulesDatabase.Register(feature);

            selfCharacter.AddResource(new ResourceState(hp.Id, max: 20));
            selfCharacter.AddResource(new ResourceState(MovementResourceId, max: 6));

            var reactorId = RuntimeEntityId.NewId();
            var reactor = new CharacterState(reactorId);
            reactor.AddResource(new ResourceState(reactionResource.Id, max: 1));
            reactor.GrantFeature(feature.Id);
            characters.Register(reactor);
            positions.Set(reactorId, new Vector3(1f, 0f, 0f)); // within default threatRange (1.5) of self's start at origin

            var damageRandom = new TestRandomSource();
            damageRandom.EnqueueResult(2);
            var reactionAbilityResolution = new AbilityResolutionService(characters, new DiceService(damageRandom));
            var reactionService = new ReactionService(characters, reactionAbilityResolution, new FirstEligibleReactionProvider(), reactionResource);
            var movingService = new MovementResolutionService(characters, positions, movementController, combatLog, reactionService, rulesDatabase);

            movementController.DistanceToReturn = 6f; // self moves from (0,0,0) to (6,0,0) -- 5 units from the reactor, past threatRange
            movingService.MoveToward(self, target, MovementResourceId, stopDistance: 1f, potentialReactors: new[] { reactorId });

            selfCharacter.TryGetResource(hp.Id, out var selfHp);
            Assert.AreEqual(18, selfHp.Current); // 20 - 2 damage from the opportunity attack
            reactor.TryGetResource(reactionResource.Id, out var reactionPool);
            Assert.AreEqual(0, reactionPool.Current); // spent
        }

        [Test]
        public void StayingWithinThreatRangeDoesNotTriggerAnOpportunityAttack()
        {
            var reactionResource = TestDefinitionFactory.CreateResourceDefinition("resource-reaction", "Reaction");
            var rulesDatabase = new RulesDatabase();

            var reactorId = RuntimeEntityId.NewId();
            var reactor = new CharacterState(reactorId);
            reactor.AddResource(new ResourceState(reactionResource.Id, max: 1));
            characters.Register(reactor);
            positions.Set(reactorId, new Vector3(20f, 0f, 0f)); // already far outside threatRange -- never "in range before"

            selfCharacter.AddResource(new ResourceState(MovementResourceId, max: 6));
            var reactionAbilityResolution = new AbilityResolutionService(characters, new DiceService(new TestRandomSource()));
            var reactionService = new ReactionService(characters, reactionAbilityResolution, new FirstEligibleReactionProvider(), reactionResource);
            var movingService = new MovementResolutionService(characters, positions, movementController, combatLog, reactionService, rulesDatabase);

            movementController.DistanceToReturn = 6f;

            Assert.DoesNotThrow(() => movingService.MoveToward(self, target, MovementResourceId, stopDistance: 1f, potentialReactors: new[] { reactorId }));
            reactor.TryGetResource(reactionResource.Id, out var reactionPool);
            Assert.AreEqual(1, reactionPool.Current); // untouched -- no reaction spent
        }

        [Test]
        public void MovingStillWorksWithoutReactionSupportWiredUp()
        {
            selfCharacter.AddResource(new ResourceState(MovementResourceId, max: 6));
            movementController.DistanceToReturn = 6f;

            // Base `service` (from SetUp) has no ReactionService/IRulesDatabase -- passing potentialReactors
            // should be silently ignored, not throw.
            Assert.DoesNotThrow(() => service.MoveToward(self, target, MovementResourceId, stopDistance: 1f, potentialReactors: new[] { RuntimeEntityId.NewId() }));

            selfCharacter.TryGetResource(MovementResourceId, out var movement);
            Assert.AreEqual(0, movement.Current); // 6 - ceil(6) -- moved normally
        }

        // Task 4.3: the small new public primitives CRPG.Gameplay.Actions.MoveActionExecution calls into for
        // real-navigation multi-segment moves -- MovementResolutionService itself never constructs or knows
        // about that Gameplay-layer type (see this task's own dependency-direction note).

        [Test]
        public void GetEffectiveRemainingBudgetReturnsCurrentWhenNoSpeedRule()
        {
            selfCharacter.AddResource(new ResourceState(MovementResourceId, max: 6, current: 4));

            Assert.AreEqual(4f, service.GetEffectiveRemainingBudget(self, MovementResourceId));
        }

        [Test]
        public void GetEffectiveRemainingBudgetIsCappedBySpeedRule()
        {
            selfCharacter.AddResource(new ResourceState(MovementResourceId, max: 10));
            var speedRule = new FixedMovementSpeedRule(2f);
            var cappedService = new MovementResolutionService(characters, positions, movementController, combatLog, speedRule: speedRule);

            Assert.AreEqual(2f, cappedService.GetEffectiveRemainingBudget(self, MovementResourceId));
        }

        [Test]
        public void GetEffectiveRemainingBudgetIsZeroWhenTheResourceDoesNotExist()
        {
            Assert.AreEqual(0f, service.GetEffectiveRemainingBudget(self, new DefinitionId("resource-nonexistent")));
        }

        [Test]
        public void SpendMovementDeductsFromTheResource()
        {
            selfCharacter.AddResource(new ResourceState(MovementResourceId, max: 6));

            service.SpendMovement(self, MovementResourceId, 2);

            selfCharacter.TryGetResource(MovementResourceId, out var movement);
            Assert.AreEqual(4, movement.Current);
        }

        [Test]
        public void SpendMovementIsANoOpForANonPositiveAmount()
        {
            selfCharacter.AddResource(new ResourceState(MovementResourceId, max: 6));

            service.SpendMovement(self, MovementResourceId, 0);
            service.SpendMovement(self, MovementResourceId, -1);

            selfCharacter.TryGetResource(MovementResourceId, out var movement);
            Assert.AreEqual(6, movement.Current);
        }

        [Test]
        public void FindThreatBoundaryCrossingsFindsEnteringThenLeavingInOrder()
        {
            var reactorId = RuntimeEntityId.NewId();
            positions.Set(reactorId, new Vector3(5f, 0f, 0f)); // default threatRange 1.5

            var crossings = service.FindThreatBoundaryCrossings(new Vector3(0f, 0f, 0f), new Vector3(10f, 0f, 0f), new[] { reactorId });

            Assert.AreEqual(2, crossings.Count);
            Assert.IsTrue(crossings[0].Entering);
            Assert.AreEqual(3.5f, crossings[0].DistanceAlongSegment, 0.001f);
            Assert.IsFalse(crossings[1].Entering);
            Assert.AreEqual(6.5f, crossings[1].DistanceAlongSegment, 0.001f);
        }

        [Test]
        public void FindThreatBoundaryCrossingsIsEmptyWhenTheSegmentNeverTouchesTheThreatCircle()
        {
            var reactorId = RuntimeEntityId.NewId();
            positions.Set(reactorId, new Vector3(50f, 0f, 0f));

            var crossings = service.FindThreatBoundaryCrossings(new Vector3(0f, 0f, 0f), new Vector3(10f, 0f, 0f), new[] { reactorId });

            Assert.AreEqual(0, crossings.Count);
        }

        [Test]
        public void FindThreatBoundaryCrossingsFindsOnlyLeavingWhenTheSegmentStartsAlreadyInsideRange()
        {
            var reactorId = RuntimeEntityId.NewId();
            positions.Set(reactorId, new Vector3(5f, 0f, 0f));

            // Starts exactly at the reactor's own position -- well inside its threat circle from the outset.
            var crossings = service.FindThreatBoundaryCrossings(new Vector3(5f, 0f, 0f), new Vector3(10f, 0f, 0f), new[] { reactorId });

            Assert.AreEqual(1, crossings.Count);
            Assert.IsFalse(crossings[0].Entering);
            Assert.AreEqual(1.5f, crossings[0].DistanceAlongSegment, 0.001f);
        }

        [Test]
        public void FindThreatBoundaryCrossingsFindsOnlyEnteringWhenTheSegmentEndsInsideRange()
        {
            var reactorId = RuntimeEntityId.NewId();
            positions.Set(reactorId, new Vector3(5f, 0f, 0f));

            var crossings = service.FindThreatBoundaryCrossings(new Vector3(0f, 0f, 0f), new Vector3(5f, 0f, 0f), new[] { reactorId });

            Assert.AreEqual(1, crossings.Count);
            Assert.IsTrue(crossings[0].Entering);
            Assert.AreEqual(3.5f, crossings[0].DistanceAlongSegment, 0.001f);
        }

        [Test]
        public void ResolveReactionForCrossingNeverTriggersOnAnEnteringCrossing()
        {
            var reactionResource = TestDefinitionFactory.CreateResourceDefinition("resource-reaction", "Reaction");
            var rulesDatabase = new RulesDatabase();
            var reactorId = RuntimeEntityId.NewId();
            var reactor = new CharacterState(reactorId);
            reactor.AddResource(new ResourceState(reactionResource.Id, max: 1));
            characters.Register(reactor);

            var reactionAbilityResolution = new AbilityResolutionService(characters, new DiceService(new TestRandomSource()));
            var reactionService = new ReactionService(characters, reactionAbilityResolution, new FirstEligibleReactionProvider(), reactionResource);
            var movingService = new MovementResolutionService(characters, positions, movementController, combatLog, reactionService, rulesDatabase);

            var triggered = movingService.ResolveReactionForCrossing(self, reactorId, entering: true);

            Assert.IsFalse(triggered);
            reactor.TryGetResource(reactionResource.Id, out var reactionPool);
            Assert.AreEqual(1, reactionPool.Current); // untouched
        }

        [Test]
        public void ResolveReactionForCrossingTriggersOnALeavingCrossingWithAnEligibleReactor()
        {
            var hp = TestDefinitionFactory.CreateResourceDefinition("resource-hp", "Hit Points");
            var reactionResource = TestDefinitionFactory.CreateResourceDefinition("resource-reaction", "Reaction");
            var rulesDatabase = new RulesDatabase();

            var damageEffect = new DamageEffect();
            TestDefinitionFactory.SetPrivateField(damageEffect, "hitPointResource", hp);
            TestDefinitionFactory.SetPrivateField(damageEffect, "amount", new DiceExpression(1, 2));

            var opportunityAttack = ScriptableObject.CreateInstance<AbilityDefinition>();
            TestDefinitionFactory.SetPrivateField(opportunityAttack, "id", "ability-opportunity-attack-crossing");
            TestDefinitionFactory.SetPrivateField(opportunityAttack, "tags", AbilityTag.Reaction);
            object boxedCost = new AbilityCost();
            TestDefinitionFactory.SetPrivateField(boxedCost, "resource", reactionResource);
            TestDefinitionFactory.SetPrivateField(boxedCost, "amount", 1);
            TestDefinitionFactory.SetPrivateField(opportunityAttack, "costs", new List<AbilityCost> { (AbilityCost)boxedCost });
            var targeting = new TargetingDefinition();
            TestDefinitionFactory.SetPrivateField(targeting, "minTargets", 1);
            TestDefinitionFactory.SetPrivateField(targeting, "maxTargets", 1);
            TestDefinitionFactory.SetPrivateField(opportunityAttack, "targeting", targeting);
            TestDefinitionFactory.SetPrivateField(opportunityAttack, "effects", new List<AbilityEffect> { damageEffect });
            rulesDatabase.Register(opportunityAttack);

            var feature = TestDefinitionFactory.CreateFeatureDefinition("feature-opportunist-crossing", "Opportunist");
            TestDefinitionFactory.SetPrivateField(feature, "grantedAbilityIds", new List<DefinitionId> { opportunityAttack.Id });
            rulesDatabase.Register(feature);

            selfCharacter.AddResource(new ResourceState(hp.Id, max: 20));

            var reactorId = RuntimeEntityId.NewId();
            var reactor = new CharacterState(reactorId);
            reactor.AddResource(new ResourceState(reactionResource.Id, max: 1));
            reactor.GrantFeature(feature.Id);
            characters.Register(reactor);

            var damageRandom = new TestRandomSource();
            damageRandom.EnqueueResult(2);
            var reactionAbilityResolution = new AbilityResolutionService(characters, new DiceService(damageRandom));
            var reactionService = new ReactionService(characters, reactionAbilityResolution, new FirstEligibleReactionProvider(), reactionResource);
            var movingService = new MovementResolutionService(characters, positions, movementController, combatLog, reactionService, rulesDatabase);

            var triggered = movingService.ResolveReactionForCrossing(self, reactorId, entering: false);

            Assert.IsTrue(triggered);
            selfCharacter.TryGetResource(hp.Id, out var selfHp);
            Assert.AreEqual(18, selfHp.Current);
            reactor.TryGetResource(reactionResource.Id, out var reactionPool);
            Assert.AreEqual(0, reactionPool.Current);
        }

        [Test]
        public void ResolveReactionForCrossingReturnsFalseWithoutReactionSupportWiredUp()
        {
            // Base `service` (from SetUp) has no ReactionService/IRulesDatabase.
            Assert.IsFalse(service.ResolveReactionForCrossing(self, target, entering: false));
        }
    }
}
