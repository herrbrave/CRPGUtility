using System.Collections.Generic;
using CRPG.Abilities;
using CRPG.Actors;
using CRPG.Combat;
using CRPG.Core;
using CRPG.Rules;
using CRPG.Rules.SRD5;
using NUnit.Framework;
using UnityEngine;

namespace CRPG.Tests.EditMode
{
    /// <summary>Exercises the SRD policy through genuine ability, attack, damage and resource services.
    /// Crossing detection and turn resource refresh are the engine's responsibility, not this policy's.</summary>
    public class CommonOpportunityAttackTests
    {
        private static readonly DefinitionId Strength = new("stat-strength");
        private static readonly DefinitionId Dexterity = new("stat-dexterity");
        private readonly List<UnityEngine.Object> definitions = new();
        private CharacterRegistry characters;
        private RulesDatabase database;
        private CharacterState reactor;
        private CharacterState mover;
        private ResourceDefinition action;
        private ResourceDefinition reaction;
        private ResourceDefinition hitPoints;
        private FakePositionProvider positions;
        private Factions factions;
        private Sight sight;
        private TestRandomSource random;
        private OpportunityAttackRule rule;

        private sealed class Factions : IFactionProvider
        {
            public readonly Dictionary<RuntimeEntityId, DefinitionId> Values = new();
            public bool TryGetFaction(RuntimeEntityId entity, out DefinitionId faction) => Values.TryGetValue(entity, out faction);
        }

        private sealed class Sight : ILineOfSightProvider
        {
            public bool Clear = true;
            public bool HasLineOfSight(Vector3 from, Vector3 to) => Clear;
        }

        private sealed class InstantMovement : IMovementController
        {
            private readonly FakePositionProvider positions;
            public InstantMovement(FakePositionProvider positions) => this.positions = positions;
            public float TryMoveTowards(RuntimeEntityId entity, Vector3 destination, float maxDistance)
            {
                Vector3 before = positions.GetPosition(entity);
                Vector3 after = Vector3.MoveTowards(before, destination, maxDistance);
                positions.Set(entity, after); return Vector3.Distance(before, after);
            }
        }

        [TestCase(false)]
        [TestCase(true)]
        public void InstantMovementResolvesTheAttackInsideReachUnlessDisengaged(bool disengaged)
        {
            reactor.LearnAbility(CreateAttack("ability-sword").Id); random.EnqueueResults(15, 4);
            if (disengaged) mover.ApplyCondition(new ConditionInstance(new DefinitionId("condition-disengaging"), mover.Id, mover.Id));
            var destination = RuntimeEntityId.NewId(); positions.Set(destination, Vector3.right * 10f);
            var movementId = new DefinitionId("resource-movement"); mover.AddResource(new ResourceState(movementId, 5));
            var movement = new MovementResolutionService(characters, positions, new InstantMovement(positions), rulesDatabase: database) { OpportunityAttacks = rule };
            movement.MoveToward(mover.Id, destination, movementId, 0f, new[] { reactor.Id });
            Assert.That(Current(mover, hitPoints), Is.EqualTo(disengaged ? 20 : 16));
            Assert.That(Current(reactor, reaction), Is.EqualTo(disengaged ? 1 : 0));
            Assert.That(positions.GetPosition(mover.Id).x, Is.EqualTo(6f).Within(.001f));
            Assert.That(mover.TryGetResource(movementId, out var remaining), Is.True);
            Assert.That(remaining.Current, Is.Zero, "Segmentation spends the cumulative distance, not ceil(distance) once per segment.");
        }

        [SetUp]
        public void SetUp()
        {
            characters = new CharacterRegistry();
            database = new RulesDatabase();
            action = Own(TestDefinitionFactory.CreateResourceDefinition("resource-action", "Action"));
            reaction = Own(TestDefinitionFactory.CreateResourceDefinition("resource-reaction", "Reaction"));
            hitPoints = Own(TestDefinitionFactory.CreateResourceDefinition("resource-hp", "Hit Points"));
            reactor = new CharacterState(RuntimeEntityId.NewId());
            mover = new CharacterState(RuntimeEntityId.NewId());
            reactor.Stats.SetScore(Strength, 16);
            mover.Stats.SetScore(Dexterity, 10);
            reactor.AddResource(new ResourceState(action.Id, 1));
            reactor.AddResource(new ResourceState(reaction.Id, 1));
            reactor.AddResource(new ResourceState(hitPoints.Id, 20));
            mover.AddResource(new ResourceState(hitPoints.Id, 20));
            characters.Register(reactor);
            characters.Register(mover);
            positions = new FakePositionProvider();
            positions.Set(reactor.Id, Vector3.zero);
            positions.Set(mover.Id, Vector3.right);
            factions = new Factions();
            factions.Values[reactor.Id] = new DefinitionId("faction-enemy");
            factions.Values[mover.Id] = new DefinitionId("faction-party");
            sight = new Sight();
            random = new TestRandomSource();
            var service = new AbilityResolutionService(
                characters, new DiceService(random), positions: positions,
                attackBonusRule: new DefaultAttackBonusRule(characters, database),
                armorClassRule: new DefaultArmorClassRule(characters, database, Dexterity),
                rulesDatabase: database, factionProvider: factions, lineOfSightProvider: sight);
            service.IsCombatActive = () => true;
            rule = new OpportunityAttackRule(characters, database, service, factions, positions, sight, reaction);
        }

        [TearDown]
        public void TearDown()
        {
            rule?.Dispose();
            foreach (var definition in definitions)
            {
                if (definition != null) UnityEngine.Object.DestroyImmediate(definition);
            }

            definitions.Clear();
        }

        [Test]
        public void NormalMeleeAttackDealsRealDamageAndSpendsOnlyReaction()
        {
            var attack = CreateAttack("ability-sword");
            reactor.LearnAbility(attack.Id);
            random.EnqueueResults(15, 4);

            Assert.IsTrue(rule.CanReact(reactor.Id, mover.Id));
            Assert.IsTrue(rule.Resolve(reactor.Id, mover.Id));

            Assert.AreEqual(16, Current(mover, hitPoints));
            Assert.AreEqual(0, Current(reactor, reaction));
            Assert.AreEqual(1, Current(reactor, action));
            Assert.AreEqual(AbilityTag.Attack | AbilityTag.Melee | AbilityTag.Weapon, attack.Tags);
            Assert.AreEqual(1, attack.Costs.Count);
            Assert.AreSame(action, attack.Costs[0].Resource);
            Assert.AreEqual(1, attack.Costs[0].Amount);
            Assert.AreEqual(2f, attack.PrepareSeconds);
            Assert.AreEqual(3f, attack.RecoverSeconds);
        }

        [Test]
        public void SpentActionDoesNotBlockReactionAndNoActionIsConsumed()
        {
            reactor.LearnAbility(CreateAttack("ability-sword").Id);
            reactor.TryGetResource(action.Id, out var remainingAction);
            remainingAction.Spend(1);
            random.EnqueueResults(15, 4);

            Assert.IsTrue(rule.CanReact(reactor.Id, mover.Id));
            Assert.IsTrue(rule.Resolve(reactor.Id, mover.Id));
            Assert.AreEqual(0, remainingAction.Current);
            Assert.AreEqual(0, Current(reactor, reaction));
            Assert.AreEqual(16, Current(mover, hitPoints));
        }

        [Test]
        public void ReactionIsAvailableAgainOnlyAfterItsBudgetIsRestored()
        {
            reactor.LearnAbility(CreateAttack("ability-sword").Id);
            random.EnqueueResults(15, 4, 15, 3);
            Assert.IsTrue(rule.Resolve(reactor.Id, mover.Id));
            Assert.IsFalse(rule.CanReact(reactor.Id, mover.Id));
            Assert.IsFalse(rule.Resolve(reactor.Id, mover.Id));
            Assert.AreEqual(16, Current(mover, hitPoints));

            reactor.TryGetResource(reaction.Id, out var budget);
            budget.RestoreToFull(); // The combat engine does this when the reactor's next turn starts.
            Assert.IsTrue(rule.Resolve(reactor.Id, mover.Id));
            Assert.AreEqual(13, Current(mover, hitPoints));
        }

        [Test]
        public void ExtraAttackDoesNotMultiplyTheOpportunityAttack()
        {
            reactor.LearnAbility(CreateAttack("ability-sword").Id);
            var extraAttack = Own(TestDefinitionFactory.CreateFeatureDefinition(
                "feature-extra-attack", "Extra Attack",
                TestDefinitionFactory.CreateFeatureModifier("AttackCount", RuleModifierOperation.Add, 2)));
            database.Register(extraAttack);
            reactor.GrantFeature(extraAttack.Id);
            Assert.AreEqual(3, new DefaultAttackBonusRule(characters, database).ComputeAttackCount(reactor.Id));
            random.EnqueueResults(15, 4); // Any extra strike would exhaust the deterministic dice source.

            Assert.IsTrue(rule.Resolve(reactor.Id, mover.Id));
            Assert.AreEqual(16, Current(mover, hitPoints));
        }

        [Test]
        public void DisengagingMoverDoesNotProvokeOrSpendAReaction()
        {
            reactor.LearnAbility(CreateAttack("ability-sword").Id);
            Apply(mover, "condition-disengaging");
            Assert.IsFalse(rule.CanProvoke(mover.Id));
            AssertBlocked();
        }

        [Test]
        public void MissingMoverDoesNotProvoke()
        {
            Assert.IsFalse(rule.CanProvoke(RuntimeEntityId.NewId()));
        }

        [Test]
        public void NoSelfOrAllyOrUnknownFactionReactions()
        {
            reactor.LearnAbility(CreateAttack("ability-sword").Id);
            Assert.IsFalse(rule.CanReact(reactor.Id, reactor.Id));
            Assert.IsFalse(rule.Resolve(reactor.Id, reactor.Id));
            factions.Values[mover.Id] = factions.Values[reactor.Id];
            AssertBlocked();
            factions.Values.Remove(mover.Id);
            AssertBlocked();
        }

        [Test]
        public void RegisteredButUnownedAttackIsNotAnAvailableAttack()
        {
            CreateAttack("ability-unowned"); // No learned, prepared or feature grant, even for a legacy NPC.
            Assert.AreEqual(0f, rule.ReachOf(reactor.Id));
            AssertBlocked();
        }

        [TestCase(AbilityTag.Attack | AbilityTag.Ranged | AbilityTag.Weapon)]
        [TestCase(AbilityTag.Attack | AbilityTag.Melee | AbilityTag.Spell)]
        [TestCase(AbilityTag.Attack | AbilityTag.Melee | AbilityTag.Reaction)]
        [TestCase(AbilityTag.Melee | AbilityTag.Weapon)]
        public void RangedSpellsReactionsAndNonAttackAbilitiesAreExcluded(AbilityTag tags)
        {
            reactor.LearnAbility(CreateAttack("ability-excluded", tags).Id);
            Assert.AreEqual(0f, rule.ReachOf(reactor.Id));
            AssertBlocked();
        }

        [TestCase(0)]
        [TestCase(2)]
        public void OnlyTheNormalOneActionCostQualifies(int amount)
        {
            var attack = CreateAttack("ability-wrong-cost");
            TestDefinitionFactory.SetPrivateField(attack, "costs", new List<AbilityCost> { Cost(action, amount) });
            reactor.LearnAbility(attack.Id);
            AssertBlocked();
        }

        [Test]
        public void LimitedClassResourceActionCannotBeConvertedIntoAFreeReaction()
        {
            var charge = Own(TestDefinitionFactory.CreateResourceDefinition("resource-class-charge", "Class Charge"));
            var attack = CreateAttack("ability-class-action");
            TestDefinitionFactory.SetPrivateField(attack, "costs", new List<AbilityCost> { Cost(action, 1), Cost(charge, 1) });
            reactor.AddResource(new ResourceState(charge.Id, 1));
            reactor.LearnAbility(attack.Id);
            AssertBlocked();
            Assert.AreEqual(1, Current(reactor, charge));
        }

        [Test]
        public void FeatureGrantedAndPreparedAttacksContributeTheirActualReach()
        {
            var shortAttack = CreateAttack("ability-prepared", range: 1.5f);
            var longAttack = CreateAttack("ability-feature", range: 3f);
            var feature = Own(TestDefinitionFactory.CreateFeatureDefinition("feature-weapon", "Weapon"));
            TestDefinitionFactory.SetPrivateField(feature, "grantedAbilityIds", new List<DefinitionId> { longAttack.Id });
            database.Register(feature);
            reactor.PrepareSpells(new[] { shortAttack.Id });
            reactor.GrantFeature(feature.Id);
            positions.Set(mover.Id, Vector3.right * 2f);
            Assert.AreEqual(3f, rule.ReachOf(reactor.Id));
            random.EnqueueResults(15, 4);
            Assert.IsTrue(rule.Resolve(reactor.Id, mover.Id));
            Assert.AreEqual(16, Current(mover, hitPoints));
        }

        [Test]
        public void PreparedOnlyNormalAttackIsAvailable()
        {
            var attack = CreateAttack("ability-prepared-only");
            reactor.PrepareSpells(new[] { attack.Id });
            Assert.IsFalse(reactor.KnowsAbility(attack.Id));
            random.EnqueueResults(15, 4);
            Assert.IsTrue(rule.Resolve(reactor.Id, mover.Id));
            Assert.AreEqual(16, Current(mover, hitPoints));
        }

        [Test]
        public void CachedReactionDoesNotOutliveTheOriginalAbilityGrant()
        {
            var attack = CreateAttack("ability-forgotten");
            reactor.LearnAbility(attack.Id);
            Assert.IsTrue(rule.CanReact(reactor.Id, mover.Id));
            reactor.ForgetAbility(attack.Id);
            Assert.AreEqual(0f, rule.ReachOf(reactor.Id));
            AssertBlocked();
        }

        [Test]
        public void NestedOnHitEffectsRemainUsableButMultipleTopLevelAttackRollsAreExcluded()
        {
            var attack = CreateAttack("ability-nested-on-hit");
            var roll = (AttackRollEffect)attack.Effects[0];
            var sequence = new SequenceEffect();
            TestDefinitionFactory.SetPrivateField(sequence, "effects", new List<AbilityEffect> { roll.OnHit });
            TestDefinitionFactory.SetPrivateField(roll, "onHit", sequence);
            reactor.LearnAbility(attack.Id);
            random.EnqueueResults(15, 4);
            Assert.IsTrue(rule.Resolve(reactor.Id, mover.Id));
            Assert.AreEqual(16, Current(mover, hitPoints));

            reactor.TryGetResource(reaction.Id, out var budget);
            budget.RestoreToFull();
            TestDefinitionFactory.SetPrivateField(attack, "effects", new List<AbilityEffect> { roll, roll });
            Assert.IsFalse(rule.CanReact(reactor.Id, mover.Id));
            Assert.IsFalse(rule.Resolve(reactor.Id, mover.Id));
            Assert.AreEqual(1, budget.Current);
            Assert.AreEqual(16, Current(mover, hitPoints));
        }

        [Test]
        public void MeleeUnarmedAttackDoesNotRequireTheWeaponTag()
        {
            reactor.LearnAbility(CreateAttack("ability-unarmed", AbilityTag.Attack | AbilityTag.Melee).Id);
            random.EnqueueResults(15, 4);
            Assert.IsTrue(rule.Resolve(reactor.Id, mover.Id));
            Assert.AreEqual(16, Current(mover, hitPoints));
        }

        [Test]
        public void PlanningIsNotGatedByCurrentDistanceOrGeometryButResolveIs()
        {
            reactor.LearnAbility(CreateAttack("ability-sword").Id);
            positions.Set(mover.Id, Vector3.right * 100f);
            sight.Clear = false;
            Assert.IsTrue(rule.CanReact(reactor.Id, mover.Id));
            Assert.IsFalse(rule.Resolve(reactor.Id, mover.Id));
            sight.Clear = true;
            Assert.IsFalse(rule.Resolve(reactor.Id, mover.Id));
            Assert.AreEqual(1, Current(reactor, reaction));
            positions.Set(mover.Id, Vector3.right * 1.5f); // Exact reach: interrupt before leaving it.
            random.EnqueueResults(15, 4);
            Assert.IsTrue(rule.Resolve(reactor.Id, mover.Id));
        }

        [Test]
        public void GeometricLineOfSightIsRequiredAtTheCrossing()
        {
            var attack = CreateAttack("ability-sword");
            TestDefinitionFactory.SetPrivateField(attack.Targeting, "requireLineOfSight", false);
            reactor.LearnAbility(attack.Id);
            sight.Clear = false;
            Assert.IsTrue(rule.CanReact(reactor.Id, mover.Id));
            Assert.IsFalse(rule.Resolve(reactor.Id, mover.Id));
            Assert.AreEqual(1, Current(reactor, reaction));
        }

        [TestCase(true)]
        [TestCase(false)]
        public void InvisibleMoverOrBlindedReactorCannotBeSeenAtTheCrossing(bool invisibleMover)
        {
            reactor.LearnAbility(CreateAttack("ability-sword").Id);
            Apply(invisibleMover ? mover : reactor, invisibleMover ? "condition-invisible" : "condition-blinded");
            Assert.IsTrue(rule.CanReact(reactor.Id, mover.Id)); // May become visible later along the route.
            Assert.IsFalse(rule.Resolve(reactor.Id, mover.Id));
            Assert.AreEqual(1, Current(reactor, reaction));
        }

        [Test]
        public void ZeroHitPointsDeathSavesAndActionPreventingConditionsBlockTheReactor()
        {
            reactor.LearnAbility(CreateAttack("ability-sword").Id);
            reactor.TryGetResource(hitPoints.Id, out var hp);
            hp.Current = 0;
            AssertBlocked();
            hp.RestoreToFull();
            reactor.DeathSaves = new DeathSaveState();
            AssertBlocked();
            reactor.DeathSaves = null;
            var stunned = Own(TestDefinitionFactory.CreateConditionDefinition("condition-stunned", "Stunned"));
            TestDefinitionFactory.SetPrivateField(stunned, "preventsActions", true);
            database.Register(stunned);
            Apply(reactor, "condition-stunned");
            AssertBlocked();
        }

        [Test]
        public void DisposingIsIdempotentAndDoesNotDestroyTheOriginalAttackOrResource()
        {
            var attack = CreateAttack("ability-sword");
            reactor.LearnAbility(attack.Id);
            Assert.IsTrue(rule.CanReact(reactor.Id, mover.Id)); // Lazily creates the owned clone.
            rule.Dispose();
            Assert.DoesNotThrow(() => rule.Dispose());
            Assert.IsTrue(attack != null);
            Assert.IsTrue(reaction != null);
            Assert.IsFalse(rule.CanReact(reactor.Id, mover.Id));
            Assert.IsFalse(rule.Resolve(reactor.Id, mover.Id));
            Assert.AreEqual(0f, rule.ReachOf(reactor.Id));
        }

        private AbilityDefinition CreateAttack(
            string id, AbilityTag tags = AbilityTag.Attack | AbilityTag.Melee | AbilityTag.Weapon, float range = 1.5f)
        {
            var attack = Own(TestDefinitionFactory.CreateAbilityDefinition(id, id, TargetRelation.Enemy, range: range));
            TestDefinitionFactory.SetPrivateField(attack, "tags", tags);
            TestDefinitionFactory.SetPrivateField(attack, "costs", new List<AbilityCost> { Cost(action, 1) });
            TestDefinitionFactory.SetPrivateField(attack, "prepareSeconds", 2f);
            TestDefinitionFactory.SetPrivateField(attack, "recoverSeconds", 3f);
            var damage = new DamageEffect();
            TestDefinitionFactory.SetPrivateField(damage, "hitPointResource", hitPoints);
            TestDefinitionFactory.SetPrivateField(damage, "amount", new DiceExpression(1, 6));
            var roll = new AttackRollEffect();
            TestDefinitionFactory.SetPrivateField(roll, "governingStatId", Strength);
            TestDefinitionFactory.SetPrivateField(roll, "onHit", damage);
            TestDefinitionFactory.SetPrivateField(attack, "effects", new List<AbilityEffect> { roll });
            database.Register(attack);
            return attack;
        }

        private static AbilityCost Cost(ResourceDefinition resource, int amount)
        {
            object boxed = new AbilityCost();
            TestDefinitionFactory.SetPrivateField(boxed, "resource", resource);
            TestDefinitionFactory.SetPrivateField(boxed, "amount", amount);
            return (AbilityCost)boxed;
        }

        private T Own<T>(T definition) where T : UnityEngine.Object
        {
            definitions.Add(definition);
            return definition;
        }

        private static int Current(CharacterState character, ResourceDefinition resource)
        {
            Assert.IsTrue(character.TryGetResource(resource.Id, out var state));
            return state.Current;
        }

        private static void Apply(CharacterState character, string id) =>
            character.ApplyCondition(new ConditionInstance(new DefinitionId(id), RuntimeEntityId.None, character.Id));

        private void AssertBlocked()
        {
            Assert.IsFalse(rule.CanReact(reactor.Id, mover.Id));
            Assert.IsFalse(rule.Resolve(reactor.Id, mover.Id));
            Assert.AreEqual(1, Current(reactor, reaction));
            Assert.AreEqual(20, Current(mover, hitPoints));
        }
    }
}
