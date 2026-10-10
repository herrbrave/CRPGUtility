using CRPG.Abilities;
using CRPG.Actors;
using CRPG.AI;
using CRPG.Core;
using CRPG.Rules;
using NUnit.Framework;
using UnityEngine;

namespace CRPG.Tests.EditMode
{
    public class UtilityAIDecisionProviderTests
    {
        private static readonly DefinitionId HpResourceId = new("resource-hp");

        private static readonly DefinitionId MovementResourceId = new("resource-movement");

        private RulesDatabase rulesDatabase;
        private CharacterRegistry characters;
        private FakePositionProvider positions;
        private RuntimeEntityId self;
        private CharacterState selfCharacter;

        [SetUp]
        public void SetUp()
        {
            rulesDatabase = new RulesDatabase();
            characters = new CharacterRegistry();
            positions = new FakePositionProvider();
            self = RuntimeEntityId.NewId();

            selfCharacter = new CharacterState(self);
            characters.Register(selfCharacter);
            positions.Set(self, Vector3.zero);
        }

        private RuntimeEntityId CreateTarget(int currentHp, int maxHp, Vector3 position)
        {
            var id = RuntimeEntityId.NewId();
            var character = new CharacterState(id);
            character.AddResource(new ResourceState(HpResourceId, max: maxHp, current: currentHp));
            characters.Register(character);
            positions.Set(id, position);
            return id;
        }

        private UtilityAIDecisionProvider CreateProvider(BehaviorSetDefinition behaviorSet)
        {
            var abilityResolution = new AbilityResolutionService(characters, new DiceService(new SeededRandomSource(1)));
            return new UtilityAIDecisionProvider(behaviorSet, rulesDatabase, characters, abilityResolution, positions, new SeededRandomSource(1));
        }

        private UtilityAIDecisionProvider CreateProvider(BehaviorSetDefinition behaviorSet, float scoreFloor)
        {
            var abilityResolution = new AbilityResolutionService(characters, new DiceService(new SeededRandomSource(1)));
            return new UtilityAIDecisionProvider(behaviorSet, rulesDatabase, characters, abilityResolution, positions, new SeededRandomSource(1), scoreFloor: scoreFloor);
        }

        /// <summary>One legal attack on a target the consideration scores 0 for (the "target is already down" veto), as a behaviour set.</summary>
        private (BehaviorSetDefinition Set, RuntimeEntityId Target) VetoedAttackSet()
        {
            var ability = TestDefinitionFactory.CreateAbilityDefinition("ability-attack", "Attack", TargetRelation.Enemy, range: 100f);
            rulesDatabase.Register(ability);
            var veto = TestDefinitionFactory.CreateConsideration("TargetResourcePercent:resource-hp", AnimationCurve.Linear(0f, 0f, 1f, 1f)); // 0% HP -> 0
            var behavior = TestDefinitionFactory.CreateBehaviorDefinition("behavior-attack", "Attack", ability.Id, veto);
            rulesDatabase.Register(behavior);
            var set = TestDefinitionFactory.CreateBehaviorSetDefinition("behaviorset-goblin", "Goblin", behavior.Id);
            rulesDatabase.Register(set);
            return (set, CreateTarget(currentHp: 0, maxHp: 20, new Vector3(1f, 0f, 0f)));
        }

        [Test]
        public void ByDefaultAZeroScoredDecisionIsStillReturnedWhenItIsTheOnlyLegalOne()
        {
            var (set, target) = VetoedAttackSet();

            var decision = CreateProvider(set).GetDecision(new TurnContext(self, new[] { target }, System.Array.Empty<RuntimeEntityId>()));

            Assert.IsNotNull(decision, "today's behaviour, kept so authored behaviours with no considerations (score 0) still act");
            Assert.AreEqual(0f, decision.Score);
        }

        [Test]
        public void AScoreFloorOfZeroTurnsTheOnlyLegalVetoedAttackIntoNoDecision()
        {
            var (set, target) = VetoedAttackSet();

            var decision = CreateProvider(set, scoreFloor: 0f).GetDecision(new TurnContext(self, new[] { target }, System.Array.Empty<RuntimeEntityId>()));

            Assert.IsNull(decision, "a downed target the only consideration vetoed must not be attacked just because nothing else was legal");
        }

        [Test]
        public void AScoreFloorOfZeroStillLetsAPositiveCandidateWinOverAVetoedOne()
        {
            var ability = TestDefinitionFactory.CreateAbilityDefinition("ability-attack", "Attack", TargetRelation.Enemy, range: 100f);
            rulesDatabase.Register(ability);
            var favourHp = TestDefinitionFactory.CreateConsideration("TargetResourcePercent:resource-hp", AnimationCurve.Linear(0f, 0f, 1f, 1f));
            var behavior = TestDefinitionFactory.CreateBehaviorDefinition("behavior-attack", "Attack", ability.Id, favourHp);
            rulesDatabase.Register(behavior);
            var set = TestDefinitionFactory.CreateBehaviorSetDefinition("behaviorset-goblin", "Goblin", behavior.Id);
            rulesDatabase.Register(set);
            var downed = CreateTarget(currentHp: 0, maxHp: 20, new Vector3(1f, 0f, 0f));
            var standing = CreateTarget(currentHp: 10, maxHp: 20, new Vector3(2f, 0f, 0f));

            var decision = CreateProvider(set, scoreFloor: 0f).GetDecision(new TurnContext(self, new[] { downed, standing }, System.Array.Empty<RuntimeEntityId>()));

            Assert.IsNotNull(decision);
            Assert.AreEqual(standing, decision.Targets[0]);
            Assert.Greater(decision.Score, 0f);
        }

        [Test]
        public void AScoreFloorAlsoVetoesAMoveThatScoresZero()
        {
            selfCharacter.AddResource(new ResourceState(MovementResourceId, max: 6));
            var veto = TestDefinitionFactory.CreateConsideration("Distance", AnimationCurve.Linear(0f, 0f, 10f, 0f)); // always 0
            var move = TestDefinitionFactory.CreateMoveBehaviorDefinition("behavior-move", "Move", MovementResourceId, TargetRelation.Enemy, stopDistance: 1f, veto);
            rulesDatabase.Register(move);
            var set = TestDefinitionFactory.CreateBehaviorSetDefinition("behaviorset-mover", "Mover", move.Id);
            rulesDatabase.Register(set);
            var target = CreateTarget(currentHp: 20, maxHp: 20, new Vector3(5f, 0f, 0f));
            var context = new TurnContext(self, new[] { target }, System.Array.Empty<RuntimeEntityId>());

            Assert.IsNotNull(CreateProvider(set).GetDecision(context), "default: a zero-scored move is still chosen");
            Assert.IsNull(CreateProvider(set, scoreFloor: 0f).GetDecision(context));
        }

        [Test]
        public void ADecisionExactlyAtTheFloorIsVetoedAndOneAboveItIsNot()
        {
            var ability = TestDefinitionFactory.CreateAbilityDefinition("ability-attack", "Attack", TargetRelation.Enemy, range: 100f);
            rulesDatabase.Register(ability);
            var half = TestDefinitionFactory.CreateConsideration("TargetResourcePercent:resource-hp", AnimationCurve.Linear(0f, 0.5f, 1f, 0.5f)); // always 0.5
            var behavior = TestDefinitionFactory.CreateBehaviorDefinition("behavior-attack", "Attack", ability.Id, half);
            rulesDatabase.Register(behavior);
            var set = TestDefinitionFactory.CreateBehaviorSetDefinition("behaviorset-goblin", "Goblin", behavior.Id);
            rulesDatabase.Register(set);
            var target = CreateTarget(currentHp: 10, maxHp: 20, new Vector3(1f, 0f, 0f));
            var context = new TurnContext(self, new[] { target }, System.Array.Empty<RuntimeEntityId>());

            Assert.IsNull(CreateProvider(set, scoreFloor: 0.5f).GetDecision(context), "strictly above the floor");
            Assert.IsNotNull(CreateProvider(set, scoreFloor: 0.49f).GetDecision(context));
        }

        [Test]
        public void PicksTheHigherScoringTargetWhenFavoringLowTargetHealth()
        {
            var ability = TestDefinitionFactory.CreateAbilityDefinition("ability-attack", "Attack", TargetRelation.Enemy);
            rulesDatabase.Register(ability);

            // Favor low target HP%: 0% HP -> score 1, 100% HP -> score 0.
            var considerFavoringLowHp = TestDefinitionFactory.CreateConsideration(
                "TargetResourcePercent:resource-hp", AnimationCurve.Linear(0f, 1f, 1f, 0f));
            var behavior = TestDefinitionFactory.CreateBehaviorDefinition("behavior-attack", "Attack", ability.Id, considerFavoringLowHp);
            rulesDatabase.Register(behavior);

            var behaviorSet = TestDefinitionFactory.CreateBehaviorSetDefinition("behaviorset-goblin", "Goblin", behavior.Id);
            rulesDatabase.Register(behaviorSet);

            var lowHpTarget = CreateTarget(currentHp: 5, maxHp: 20, new Vector3(3f, 0f, 0f)); // 25% HP
            var highHpTarget = CreateTarget(currentHp: 20, maxHp: 20, new Vector3(4f, 0f, 0f)); // 100% HP

            var provider = CreateProvider(behaviorSet);
            var context = new TurnContext(self, new[] { lowHpTarget, highHpTarget }, System.Array.Empty<RuntimeEntityId>());

            var decision = provider.GetDecision(context);

            Assert.IsNotNull(decision);
            Assert.AreEqual(lowHpTarget, decision.Targets[0]);
        }

        [Test]
        public void AZeroScoringConsiderationVetoesThatTargetEvenIfOtherAxesAreGood()
        {
            var ability = TestDefinitionFactory.CreateAbilityDefinition("ability-attack", "Attack", TargetRelation.Enemy, range: 100f);
            rulesDatabase.Register(ability);

            // Both targets score identically on HP (tied); Distance only scores positively within 10 units.
            var considerHp = TestDefinitionFactory.CreateConsideration("TargetResourcePercent:resource-hp", AnimationCurve.Linear(0f, 1f, 1f, 0f));
            var considerDistance = TestDefinitionFactory.CreateConsideration("Distance", AnimationCurve.Linear(0f, 1f, 10f, 0f));
            var behavior = TestDefinitionFactory.CreateBehaviorDefinition("behavior-attack", "Attack", ability.Id, considerHp, considerDistance);
            rulesDatabase.Register(behavior);

            var behaviorSet = TestDefinitionFactory.CreateBehaviorSetDefinition("behaviorset-goblin", "Goblin", behavior.Id);
            rulesDatabase.Register(behaviorSet);

            var closeTarget = CreateTarget(currentHp: 10, maxHp: 20, new Vector3(5f, 0f, 0f)); // within 10 units
            var farTarget = CreateTarget(currentHp: 10, maxHp: 20, new Vector3(20f, 0f, 0f)); // beyond 10 units -> Distance vetoes it

            var provider = CreateProvider(behaviorSet);
            var context = new TurnContext(self, new[] { closeTarget, farTarget }, System.Array.Empty<RuntimeEntityId>());

            var decision = provider.GetDecision(context);

            Assert.IsNotNull(decision);
            Assert.AreEqual(closeTarget, decision.Targets[0]);
        }

        [Test]
        public void AnInfluenceMapConsiderationShiftsTheWinnerWhenASourceIsAdded()
        {
            var ability = TestDefinitionFactory.CreateAbilityDefinition("ability-attack", "Attack", TargetRelation.Enemy, range: 100f);
            rulesDatabase.Register(ability);

            // Prefer NOT to attack a target standing in danger: 0 danger -> score 1, full danger -> score 0.
            var considerDanger = TestDefinitionFactory.CreateConsideration("InfluenceMap:Danger:Target", AnimationCurve.Linear(0f, 1f, 1f, 0f));
            var behavior = TestDefinitionFactory.CreateBehaviorDefinition("behavior-attack", "Attack", ability.Id, considerDanger);
            rulesDatabase.Register(behavior);

            var behaviorSet = TestDefinitionFactory.CreateBehaviorSetDefinition("behaviorset-goblin", "Goblin", behavior.Id);
            rulesDatabase.Register(behaviorSet);

            // Identical HP/distance -- a tie without the influence map.
            var targetInDanger = CreateTarget(currentHp: 10, maxHp: 20, new Vector3(5f, 0f, 0f));
            var safeTarget = CreateTarget(currentHp: 10, maxHp: 20, new Vector3(-5f, 0f, 0f));

            var influenceMap = new InfluenceMap();
            influenceMap.AddSource(new InfluenceSource(
                RuntimeEntityId.NewId(), "Danger", positions.GetPosition(targetInDanger), radius: 1f, magnitude: 1f, AnimationCurve.Linear(0f, 1f, 1f, 0f)));

            var provider = CreateProvider(behaviorSet);
            var context = new TurnContext(self, new[] { targetInDanger, safeTarget }, System.Array.Empty<RuntimeEntityId>(), influenceMap);

            var decision = provider.GetDecision(context);

            Assert.IsNotNull(decision);
            Assert.AreEqual(safeTarget, decision.Targets[0]);
        }

        [Test]
        public void ReturnsNullWhenNoBehaviorCanBeUsed()
        {
            var behaviorSet = TestDefinitionFactory.CreateBehaviorSetDefinition("behaviorset-empty", "Empty");
            var provider = CreateProvider(behaviorSet);
            var context = new TurnContext(self, System.Array.Empty<RuntimeEntityId>(), System.Array.Empty<RuntimeEntityId>());

            Assert.IsNull(provider.GetDecision(context));
        }

        [Test]
        public void AMoveBehaviorPicksTheFarthestTargetWhenFavoringDistance()
        {
            selfCharacter.AddResource(new ResourceState(MovementResourceId, max: 6));

            // Favor a far target: 0 distance -> score 0, 10 distance -> score 1.
            var considerFavoringDistance = TestDefinitionFactory.CreateConsideration("Distance", AnimationCurve.Linear(0f, 0f, 10f, 1f));
            var moveBehavior = TestDefinitionFactory.CreateMoveBehaviorDefinition(
                "behavior-close-distance", "Close Distance", MovementResourceId, TargetRelation.Enemy, stopDistance: 1f, considerFavoringDistance);
            rulesDatabase.Register(moveBehavior);

            var behaviorSet = TestDefinitionFactory.CreateBehaviorSetDefinition("behaviorset-mover", "Mover", moveBehavior.Id);
            rulesDatabase.Register(behaviorSet);

            var nearTarget = CreateTarget(currentHp: 20, maxHp: 20, new Vector3(2f, 0f, 0f));
            var farTarget = CreateTarget(currentHp: 20, maxHp: 20, new Vector3(8f, 0f, 0f));

            var provider = CreateProvider(behaviorSet);
            var context = new TurnContext(self, new[] { nearTarget, farTarget }, System.Array.Empty<RuntimeEntityId>());

            var decision = provider.GetDecision(context);

            Assert.IsNotNull(decision);
            Assert.AreEqual(BehaviorKind.Move, decision.Kind);
            Assert.IsNull(decision.Ability);
            Assert.AreEqual(MovementResourceId, decision.MovementResourceId);
            Assert.AreEqual(1f, decision.StopDistance);
            Assert.AreEqual(farTarget, decision.Targets[0]);
        }

        [Test]
        public void AMoveBehaviorIsSkippedWhenNoMovementResourceIsLeft()
        {
            selfCharacter.AddResource(new ResourceState(MovementResourceId, max: 6, current: 0));

            var moveBehavior = TestDefinitionFactory.CreateMoveBehaviorDefinition(
                "behavior-close-distance", "Close Distance", MovementResourceId, TargetRelation.Enemy, stopDistance: 1f,
                TestDefinitionFactory.CreateConsideration("Distance", AnimationCurve.Linear(0f, 0f, 10f, 1f)));
            rulesDatabase.Register(moveBehavior);

            var behaviorSet = TestDefinitionFactory.CreateBehaviorSetDefinition("behaviorset-mover", "Mover", moveBehavior.Id);
            rulesDatabase.Register(behaviorSet);

            var target = CreateTarget(currentHp: 20, maxHp: 20, new Vector3(8f, 0f, 0f));

            var provider = CreateProvider(behaviorSet);
            var context = new TurnContext(self, new[] { target }, System.Array.Empty<RuntimeEntityId>());

            Assert.IsNull(provider.GetDecision(context));
        }

        [Test]
        public void AttackAndMoveBehaviorsCompeteOnTheSameScoreAndAttackWinsWhenInRange()
        {
            selfCharacter.AddResource(new ResourceState(MovementResourceId, max: 6));

            var ability = TestDefinitionFactory.CreateAbilityDefinition("ability-attack", "Attack", TargetRelation.Enemy, range: 2f);
            rulesDatabase.Register(ability);

            // Attack favors being close (0 distance -> score 1, 2 -> score 0); Move favors being far (mirror image).
            var attackBehavior = TestDefinitionFactory.CreateBehaviorDefinition(
                "behavior-attack", "Attack", ability.Id, TestDefinitionFactory.CreateConsideration("Distance", AnimationCurve.Linear(0f, 1f, 2f, 0f)));
            rulesDatabase.Register(attackBehavior);

            var moveBehavior = TestDefinitionFactory.CreateMoveBehaviorDefinition(
                "behavior-close-distance", "Close Distance", MovementResourceId, TargetRelation.Enemy, stopDistance: 1f,
                TestDefinitionFactory.CreateConsideration("Distance", AnimationCurve.Linear(0f, 0f, 10f, 1f)));
            rulesDatabase.Register(moveBehavior);

            var behaviorSet = TestDefinitionFactory.CreateBehaviorSetDefinition("behaviorset-fighter", "Fighter", attackBehavior.Id, moveBehavior.Id);
            rulesDatabase.Register(behaviorSet);

            // Adjacent and in the ability's range -- attacking should win over closing distance further.
            var adjacentTarget = CreateTarget(currentHp: 20, maxHp: 20, new Vector3(1f, 0f, 0f));

            var provider = CreateProvider(behaviorSet);
            var context = new TurnContext(self, new[] { adjacentTarget }, System.Array.Empty<RuntimeEntityId>());

            var decision = provider.GetDecision(context);

            Assert.IsNotNull(decision);
            Assert.AreEqual(BehaviorKind.UseAbility, decision.Kind);
            Assert.AreEqual(ability, decision.Ability);
        }

        [Test]
        public void AttackAndMoveBehaviorsCompeteOnTheSameScoreAndMoveWinsWhenOutOfRange()
        {
            selfCharacter.AddResource(new ResourceState(MovementResourceId, max: 6));

            var ability = TestDefinitionFactory.CreateAbilityDefinition("ability-attack", "Attack", TargetRelation.Enemy, range: 2f);
            rulesDatabase.Register(ability);

            var attackBehavior = TestDefinitionFactory.CreateBehaviorDefinition(
                "behavior-attack", "Attack", ability.Id, TestDefinitionFactory.CreateConsideration("Distance", AnimationCurve.Linear(0f, 1f, 2f, 0f)));
            rulesDatabase.Register(attackBehavior);

            var moveBehavior = TestDefinitionFactory.CreateMoveBehaviorDefinition(
                "behavior-close-distance", "Close Distance", MovementResourceId, TargetRelation.Enemy, stopDistance: 1f,
                TestDefinitionFactory.CreateConsideration("Distance", AnimationCurve.Linear(0f, 0f, 10f, 1f)));
            rulesDatabase.Register(moveBehavior);

            var behaviorSet = TestDefinitionFactory.CreateBehaviorSetDefinition("behaviorset-fighter", "Fighter", attackBehavior.Id, moveBehavior.Id);
            rulesDatabase.Register(behaviorSet);

            // Out of the ability's range (2) -- Attack isn't even a candidate, so Move should win by default.
            var farTarget = CreateTarget(currentHp: 20, maxHp: 20, new Vector3(8f, 0f, 0f));

            var provider = CreateProvider(behaviorSet);
            var context = new TurnContext(self, new[] { farTarget }, System.Array.Empty<RuntimeEntityId>());

            var decision = provider.GetDecision(context);

            Assert.IsNotNull(decision);
            Assert.AreEqual(BehaviorKind.Move, decision.Kind);
            Assert.AreEqual(farTarget, decision.Targets[0]);
        }
    }
}
