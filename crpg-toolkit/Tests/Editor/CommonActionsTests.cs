using System;
using System.Collections.Generic;
using System.Linq;
using CRPG.Abilities;
using CRPG.Actors;
using CRPG.Combat;
using CRPG.Core;
using CRPG.Gameplay;
using CRPG.Rules;
using CRPG.Rules.SRD5;
using CRPG.Saving;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace CRPG.Tests.EditMode
{
    public sealed class CommonActionsTests
    {
        private sealed class MinimumRandom : IRandomSource { public int Next(int minInclusive, int maxExclusive) => minInclusive; }
        private sealed class RecordingDice : IDiceService
        {
            private readonly IDiceService inner = new DiceService(new MinimumRandom());
            public readonly List<RollRequest> Requests = new();
            public RollResult Roll(RollRequest request) { Requests.Add(request); return inner.Roll(request); }
        }
        [Serializable] private sealed class NoHitEffect : AbilityEffect { public override void Apply(AbilityExecutionContext context) { } }
        private sealed class NoMove : IMovementController { public float TryMoveTowards(RuntimeEntityId id, Vector3 target, float max) => 0f; }

        private RulesDatabase rules;
        private CharacterRegistry characters;
        private CharacterState helper, ally, enemy, other;
        private FakePositionProvider positions;
        private FakeLineOfSightProvider sight;
        private RecordingDice dice;
        private GameEventBus events;
        private DefaultMovementSpeedRule speed;
        private DefaultAttackBonusRule attacks;
        private DefaultSavingThrowRule saves;
        private AbilityResolutionService resolution;
        private MovementResolutionService movement;
        private CombatEncounterService encounter;
        private ConditionDurationService durations;
        private CommonActionRules common;
        private readonly List<UnityEngine.Object> owned = new();
        private static DefinitionId Id(string value) => new(value);
        private AbilityDefinition Ability(string id) => rules.Resolve<AbilityDefinition>(Id(id));
        private static ResourceState Pool(CharacterState character, string id) { Assert.That(character.TryGetResource(Id(id), out var resource), Is.True); return resource; }

        [SetUp]
        public void SetUp()
        {
            rules = new RulesDatabase();
            var provider = AssetDatabase.LoadAssetAtPath<SRD5RulesetProvider>("Packages/com.crpgtoolkit.core/Rules/SRD5e2014/Content/SRD5RulesetProvider.asset");
            Assert.That(provider, Is.Not.Null);
            rules.RegisterRange(provider.Content);
            var moveResource = TestDefinitionFactory.CreateResourceDefinition("resource-movement", "Movement");
            owned.Add(moveResource); rules.Register(moveResource);
            characters = new CharacterRegistry(rules);
            helper = MakeCharacter(); ally = MakeCharacter(); enemy = MakeCharacter(); other = MakeCharacter();
            positions = new FakePositionProvider();
            positions.Set(helper.Id, Vector3.zero); positions.Set(enemy.Id, Vector3.right); positions.Set(ally.Id, Vector3.right * 10f); positions.Set(other.Id, Vector3.forward);
            sight = new FakeLineOfSightProvider(); dice = new RecordingDice(); events = new GameEventBus();
            speed = new DefaultMovementSpeedRule(characters, rules, Id("condition-exhaustion"));
            attacks = new DefaultAttackBonusRule(characters, rules); saves = new DefaultSavingThrowRule(characters, rules);
            var budgets = new TurnResourceSet(new[] { rules.Resolve<ResourceDefinition>(Id("resource-action")), rules.Resolve<ResourceDefinition>(Id("resource-reaction")), moveResource });
            encounter = new CombatEncounterService(characters, null, budgets, events);
            durations = new ConditionDurationService(encounter, characters, events);
            var ids = new[] { helper.Id, enemy.Id, ally.Id, other.Id };
            common = new CommonActionRules(characters, rules, events, positions, sight, speed, ids);
            attacks.CommonActions = common; saves.CommonActions = common;
            resolution = new AbilityResolutionService(characters, dice, eventBus: events, positions: positions,
                rulesDatabase: rules, attackBonusRule: attacks, armorClassRule: new DefaultArmorClassRule(characters, rules, Id("stat-dexterity")),
                savingThrowRule: saves, factionProvider: new EncounterFactionProvider(encounter), lineOfSightProvider: sight)
            { IsCombatActive = () => encounter.Encounter != null, MovementSpeedRule = speed };
            movement = new MovementResolutionService(characters, positions, new NoMove(), speedRule: speed);
            encounter.StartCombatWithExplicitOrder(new[] { (helper.Id, Id("friends")), (enemy.Id, Id("foes")), (ally.Id, Id("friends")), (other.Id, Id("foes")) });
        }

        private CharacterState MakeCharacter()
        {
            var character = new CharacterState(RuntimeEntityId.NewId());
            character.AddResource(new ResourceState(Id("resource-action"), 1));
            character.AddResource(new ResourceState(Id("resource-reaction"), 1));
            character.AddResource(new ResourceState(Id("resource-movement"), 10));
            characters.Register(character);
            return character;
        }

        [TearDown]
        public void TearDown()
        {
            common?.Dispose(); durations?.Dispose();
            foreach (var value in owned) UnityEngine.Object.DestroyImmediate(value);
            owned.Clear();
        }

        private void Use(string id, CharacterState source, params RuntimeEntityId[] targets)
        {
            var result = resolution.Execute(source.Id, Ability(id), targets.Length == 0 ? new[] { source.Id } : targets);
            Assert.That(result.Succeeded, Is.True, result.Message);
        }

        private void Apply(CharacterState target, string id, int stacks = 1)
        {
            target.ApplyCondition(new ConditionInstance(Id(id), target.Id, target.Id, stackCount: stacks));
            events.Publish(new GameEventContext(GameEvent.ConditionApplied, target.Id, target.Id));
        }

        [Test]
        public void DashAddsAnAllowanceWithoutRestoringSpentMovementOrChangingPermanentSpeed()
        {
            var pool = Pool(helper, "resource-movement"); pool.Spend(4);
            Use("ability-dash", helper);
            Assert.That(pool.Max, Is.EqualTo(10)); Assert.That(pool.Current, Is.EqualTo(16));
            Assert.That(movement.GetEffectiveRemainingBudget(helper.Id, pool.ResourceId), Is.EqualTo(16f));
            Assert.That(Pool(helper, "resource-action").Current, Is.Zero);
            Assert.That(resolution.Execute(helper.Id, Ability("ability-dash"), new[] { helper.Id }).Succeeded, Is.False);
            Assert.That(pool.Current, Is.EqualTo(16));
            encounter.EndTurn(); Assert.That(pool.TemporaryMaximum, Is.Zero);
            for (int i = 0; i < 3; i++) encounter.EndTurn();
            Assert.That(pool.Current, Is.EqualTo(10));
        }

        [Test]
        public void DashAndRepeatedMovesRespectReducedSpeedAcrossTheWholeTurn()
        {
            Apply(helper, "condition-exhaustion", 2);
            var pool = Pool(helper, "resource-movement"); pool.Spend(2);
            Assert.That(movement.GetEffectiveRemainingBudget(helper.Id, pool.ResourceId), Is.EqualTo(3f));
            Use("ability-dash", helper);
            Assert.That(movement.GetEffectiveRemainingBudget(helper.Id, pool.ResourceId), Is.EqualTo(8f));
            pool.Spend(3);
            Assert.That(movement.GetEffectiveRemainingBudget(helper.Id, pool.ResourceId), Is.EqualTo(5f));
        }

        [TestCase("ability-dash")]
        [TestCase("ability-dodge")]
        public void ZeroSpeedRejectsBeforeSpendingAnAction(string id)
        {
            Apply(helper, "condition-grappled");
            Assert.That(resolution.CanAttempt(helper.Id, Ability(id)).IsValid, Is.False);
            Assert.That(resolution.Execute(helper.Id, Ability(id), new[] { helper.Id }).Succeeded, Is.False);
            Assert.That(Pool(helper, "resource-action").Current, Is.EqualTo(1));
        }

        private void IncreaseSpeed(int amount)
        {
            var boost = TestDefinitionFactory.CreateConditionDefinition("test-speed-boost", "Speed", TestDefinitionFactory.CreateFeatureModifier("Speed", RuleModifierOperation.Add, amount));
            owned.Add(boost); rules.Register(boost); Apply(helper, boost.Id.Value);
        }

        [Test]
        public void SpeedIncreaseAddsAvailableMovementEvenAfterTheBaseBudgetWasSpent()
        {
            var pool = Pool(helper, "resource-movement"); pool.Spend(10); IncreaseSpeed(5);
            Assert.That(movement.CanMove(helper.Id, pool.ResourceId), Is.True);
            Assert.That(movement.GetEffectiveRemainingBudget(helper.Id, pool.ResourceId), Is.EqualTo(5f));
            movement.SpendMovement(helper.Id, pool.ResourceId, 3);
            Assert.That(movement.GetEffectiveRemainingBudget(helper.Id, pool.ResourceId), Is.EqualTo(2f));
            Assert.That(pool.Max, Is.EqualTo(10));
        }

        [Test]
        public void DashAndAdditionalDashesUseIncreasedSpeedWithoutRefundingDistanceSpent()
        {
            var pool = Pool(helper, "resource-movement"); pool.Spend(4); IncreaseSpeed(5);
            Assert.That(movement.GetEffectiveRemainingBudget(helper.Id, pool.ResourceId), Is.EqualTo(15f - 4f));
            Use("ability-dash", helper);
            Assert.That(movement.GetEffectiveRemainingBudget(helper.Id, pool.ResourceId), Is.EqualTo(2f * 15f - 4f));
            movement.SpendMovement(helper.Id, pool.ResourceId, 12);
            Pool(helper, "resource-action").RestoreToFull(); Use("ability-dash", helper);
            Assert.That(movement.GetEffectiveRemainingBudget(helper.Id, pool.ResourceId), Is.EqualTo(3f * 15f - 4f - 12f));
            pool.ClearTemporaryMaximum();
            Assert.That(pool.Current, Is.Zero, "Clearing turn capacity must not erase distance already spent.");
        }

        [Test]
        public void LosingAndRegainingSpeedDoesNotEraseMovementAlreadySpent()
        {
            var pool = Pool(helper, "resource-movement"); pool.Spend(8);
            Apply(helper, "condition-exhaustion", 2);
            Assert.That(movement.GetEffectiveRemainingBudget(helper.Id, pool.ResourceId), Is.Zero);
            helper.RemoveConditions(c => c.ConditionId == Id("condition-exhaustion"));
            Assert.That(movement.GetEffectiveRemainingBudget(helper.Id, pool.ResourceId), Is.EqualTo(2f));
            IncreaseSpeed(5);
            Assert.That(movement.GetEffectiveRemainingBudget(helper.Id, pool.ResourceId), Is.EqualTo(7f));
            pool.RestoreToFull();
            Assert.That(movement.GetEffectiveRemainingBudget(helper.Id, pool.ResourceId), Is.EqualTo(15f));
        }

        [Test]
        public void DodgeProtectsAllVisibleAttacksAndDexteritySavesUntilTheNextOwnTurn()
        {
            Use("ability-dodge", helper);
            Assert.That(attacks.ComputeAdvantage(enemy.Id, helper.Id), Is.EqualTo(AdvantageState.Disadvantage));
            Assert.That(attacks.ComputeAdvantage(other.Id, helper.Id), Is.EqualTo(AdvantageState.Disadvantage));
            Assert.That(saves.ComputeAdvantage(helper.Id, Id("stat-dexterity")), Is.EqualTo(AdvantageState.Advantage));
            Assert.That(saves.ComputeAdvantage(helper.Id, Id("stat-strength")), Is.EqualTo(AdvantageState.Normal));
            encounter.EndTurn(); Assert.That(helper.HasCondition(CommonActions.DodgingId), Is.True);
            for (int i = 0; i < 3; i++) encounter.EndTurn();
            Assert.That(helper.HasCondition(CommonActions.DodgingId), Is.False);
            Assert.That(attacks.ComputeAdvantage(enemy.Id, helper.Id), Is.EqualTo(AdvantageState.Normal));
        }

        [Test]
        public void DodgeDoesNotProtectAgainstAnUnseenAttackerButStillAidsDexteritySaves()
        {
            Use("ability-dodge", helper); sight.AlwaysBlocked = true;
            Assert.That(common.DodgeAgainst(enemy.Id, helper.Id), Is.EqualTo(AdvantageState.Normal));
            Assert.That(saves.ComputeAdvantage(helper.Id, Id("stat-dexterity")), Is.EqualTo(AdvantageState.Advantage));
            sight.AlwaysBlocked = false; Apply(helper, "condition-blinded");
            Assert.That(common.DodgeAgainst(enemy.Id, helper.Id), Is.EqualTo(AdvantageState.Normal));
        }

        [TestCase("condition-grappled")]
        [TestCase("condition-incapacitated")]
        [TestCase("condition-stunned")]
        public void LosingDodgeEndsItRatherThanSuspendingIt(string condition)
        {
            Use("ability-dodge", helper); Apply(helper, condition);
            Assert.That(helper.HasCondition(CommonActions.DodgingId), Is.False);
            helper.RemoveConditions(c => c.ConditionId == Id(condition));
            Assert.That(common.DodgeAgainst(enemy.Id, helper.Id), Is.EqualTo(AdvantageState.Normal));
        }

        [Test]
        public void HelpSelectsANearbyEnemyThenADistantAllyAndCannotExecuteAPartialSelection()
        {
            var ability = Ability("ability-help");
            Assert.That(resolution.ValidateTargetPick(helper.Id, ability, new[] { enemy.Id }).IsValid, Is.True);
            Assert.That(resolution.Validate(helper.Id, ability, new[] { enemy.Id }).IsValid, Is.False);
            Assert.That(resolution.Execute(helper.Id, ability, new[] { enemy.Id }).Succeeded, Is.False);
            Assert.That(Pool(helper, "resource-action").Current, Is.EqualTo(1));
            Assert.That(resolution.Validate(helper.Id, ability, new[] { enemy.Id, ally.Id }).IsValid, Is.True);
            Use("ability-help", helper, enemy.Id, ally.Id);
            Assert.That(common.HelpAdvantage(ally.Id, enemy.Id), Is.EqualTo(AdvantageState.Advantage));
            Assert.That(common.HelpAdvantage(helper.Id, enemy.Id), Is.EqualTo(AdvantageState.Normal));
            Assert.That(common.HelpAdvantage(ally.Id, other.Id), Is.EqualTo(AdvantageState.Normal));
        }

        [Test]
        public void HelpRejectsWrongRolesSelfAndOutOfReachWithoutSpending()
        {
            var ability = Ability("ability-help");
            Assert.That(resolution.ValidateTargetPick(helper.Id, ability, new[] { ally.Id }).IsValid, Is.False);
            Assert.That(resolution.Validate(helper.Id, ability, new[] { enemy.Id, helper.Id }).IsValid, Is.False);
            Assert.That(resolution.Validate(helper.Id, ability, new[] { enemy.Id, other.Id }).IsValid, Is.False);
            positions.Set(enemy.Id, Vector3.right * 3f);
            Assert.That(resolution.Validate(helper.Id, ability, new[] { enemy.Id, ally.Id }).IsValid, Is.False);
            Assert.That(Pool(helper, "resource-action").Current, Is.EqualTo(1));
        }

        [Test]
        public void HelpSurvivesPreviewsAndOtherTargetsButIsConsumedOnTheFirstActualRollEvenAMiss()
        {
            Use("ability-help", helper, enemy.Id, ally.Id);
            for (int i = 0; i < 20; i++) Assert.That(attacks.ComputeAdvantage(ally.Id, enemy.Id), Is.EqualTo(AdvantageState.Advantage));
            attacks.OnAttackRolled(ally.Id, other.Id);
            Assert.That(ally.HasCondition(CommonActions.HelpedId), Is.True);
            var attack = TestDefinitionFactory.CreateAbilityDefinition("test-attack", "Test", TargetRelation.Enemy);
            owned.Add(attack);
            var roll = new AttackRollEffect();
            TestDefinitionFactory.SetPrivateField(roll, "governingStatId", Id("stat-strength"));
            TestDefinitionFactory.SetPrivateField(roll, "onHit", new NoHitEffect());
            TestDefinitionFactory.SetPrivateField(attack, "effects", new List<AbilityEffect> { roll });
            var extra = TestDefinitionFactory.CreateFeatureDefinition("test-extra-attack", "Extra Attack", TestDefinitionFactory.CreateFeatureModifier("AttackCount", RuleModifierOperation.Add, 1));
            owned.Add(extra); rules.Register(extra); ally.GrantFeature(extra.Id);
            Assert.That(resolution.Execute(ally.Id, attack, new[] { enemy.Id }).Succeeded, Is.True);
            CollectionAssert.AreEqual(new[] { AdvantageState.Advantage, AdvantageState.Normal }, dice.Requests.Select(r => r.Advantage));
            Assert.That(ally.HasCondition(CommonActions.HelpedId), Is.False);
            Assert.That(enemy.HasCondition(CommonActions.DistractedId), Is.False);
        }

        [Test]
        public void HelpExpiresAtTheHelpersNextTurnNotTheBeneficiarysTurn()
        {
            Use("ability-help", helper, enemy.Id, ally.Id);
            encounter.EndTurn(); encounter.EndTurn();
            Assert.That(ally.HasCondition(CommonActions.HelpedId), Is.True);
            encounter.EndTurn(); encounter.EndTurn();
            Assert.That(ally.HasCondition(CommonActions.HelpedId), Is.False);
            Assert.That(enemy.HasCondition(CommonActions.DistractedId), Is.False);
        }

        [Test]
        public void HelpAndDodgeCancelNormallyAndCombatEndClearsOnlyTheCommonTurnEffects()
        {
            Use("ability-help", helper, enemy.Id, ally.Id);
            Use("ability-dodge", enemy); Apply(ally, "condition-poisoned");
            Assert.That(attacks.ComputeAdvantage(ally.Id, enemy.Id), Is.EqualTo(AdvantageState.Normal));
            Pool(ally, "resource-movement").GrantTemporaryMaximum(10);
            encounter.EndCombat();
            Assert.That(enemy.HasCondition(CommonActions.DodgingId), Is.False);
            Assert.That(enemy.HasCondition(CommonActions.DistractedId), Is.False);
            Assert.That(ally.HasCondition(CommonActions.HelpedId), Is.False);
            Assert.That(ally.HasCondition(Id("condition-poisoned")), Is.True);
            Assert.That(Pool(ally, "resource-movement").TemporaryMaximum, Is.Zero);
        }

        [TestCase("ability-dash")]
        [TestCase("ability-disengage")]
        [TestCase("ability-dodge")]
        [TestCase("ability-help")]
        public void CommonActionsAreUnavailableOutsideCombatWithoutSpending(string id)
        {
            encounter.EndCombat();
            var result = resolution.CanAttempt(helper.Id, Ability(id));
            Assert.That(result.IsValid, Is.False); StringAssert.Contains("combat", result.Message);
            Assert.That(Pool(helper, "resource-action").Current, Is.EqualTo(1));
        }

        [Test]
        public void NewBuildAndOldSaveReceiveTheSameGrantWithoutChangingKnownSpellsOrDuplicatingIt()
        {
            var built = new CharacterBuildService(rules).Build(new CharacterBuildPlan(), RuntimeEntityId.NewId());
            Assert.That(built.HasFeature(CommonActions.GrantId), Is.True);
            using var original = new GameSession(rules, dice);
            var old = new CharacterState(RuntimeEntityId.NewId()); old.LearnAbility(Id("spell-guidance"));
            original.Characters.Register(old); original.Party.AddMember(old.Id);
            var data = SaveService.Capture(original.Party, original.Characters, original.Inventory, original.Timeline, original.Quests);
            data.Characters.Single().Features.Remove(CommonActions.GrantId.Value);
            using var loaded = new GameSession(rules, dice);
            SaveService.Restore(SaveService.FromJson(SaveService.ToJson(data)), loaded.Party, loaded.Characters, loaded.Inventory, (Timeline)loaded.Timeline, loaded.Quests);
            Assert.That(loaded.Characters.TryGetCharacter(old.Id, out var restored), Is.True);
            Assert.That(restored.HasFeature(CommonActions.GrantId), Is.True);
            Assert.That(restored.KnowsAbility(Id("spell-guidance")), Is.True);
            Assert.That(restored.KnowsAbility(Id("ability-dash")), Is.False, "common actions are a shared grant, not learned spells");
            loaded.Characters.Register(restored);
            Assert.That(restored.Features.Count(f => f == CommonActions.GrantId), Is.EqualTo(1));
        }

        [Test]
        public void ExplicitAndUniversalGrantsDoNotDuplicateTheSameFeature()
        {
            var race = TestDefinitionFactory.CreateRaceDefinition("test-common-race", "Test", CommonActions.GrantId); owned.Add(race);
            var built = new CharacterBuildService(rules).Build(new CharacterBuildPlan { Race = race }, RuntimeEntityId.NewId());
            Assert.That(built.Features.Count(f => f == CommonActions.GrantId), Is.EqualTo(1));
            var npc = TestDefinitionFactory.CreateNpcDefinition("test-common-npc", "Test"); owned.Add(npc);
            TestDefinitionFactory.SetPrivateField(npc, "featureIds", new List<DefinitionId> { CommonActions.GrantId });
            var spawned = NpcSpawnService.Spawn(npc, rules, RuntimeEntityId.NewId());
            Assert.That(spawned.Features.Count(f => f == CommonActions.GrantId), Is.EqualTo(1));
        }

        [Test]
        public void AuthoredMovementBaseDoesNotAddRaceSpeedAgainAndConditionDistancesAreConverted()
        {
            var raceSpeed = TestDefinitionFactory.CreateFeatureDefinition("test-race-speed", "Race", TestDefinitionFactory.CreateFeatureModifier("Speed", RuleModifierOperation.Add, 30));
            var slowing = TestDefinitionFactory.CreateConditionDefinition("test-slowing", "Slow", TestDefinitionFactory.CreateFeatureModifier("Speed", RuleModifierOperation.Subtract, 10));
            owned.Add(raceSpeed); owned.Add(slowing); rules.Register(raceSpeed); rules.Register(slowing); helper.GrantFeature(raceSpeed.Id);
            var gameSpeed = new DefaultMovementSpeedRule(characters, rules, baseIncludesFeatures: true, distanceScale: 0.2f);
            Assert.That(gameSpeed.ComputeEffectiveSpeed(helper.Id, 6f), Is.EqualTo(6f));
            helper.ApplyCondition(new ConditionInstance(slowing.Id, enemy.Id, helper.Id));
            Assert.That(gameSpeed.ComputeEffectiveSpeed(helper.Id, 6f), Is.EqualTo(4f));
        }

        [Test]
        public void HelpCannotBeOfferedWithoutItsTargetingServices()
        {
            var unwired = new AbilityResolutionService(characters, dice, rulesDatabase: rules) { IsCombatActive = () => true };
            Assert.That(unwired.CanAttempt(helper.Id, Ability("ability-help")).Reason, Is.EqualTo(AbilityRejectionReason.MissingRequiredEffectService));
            Assert.That(Pool(helper, "resource-action").Current, Is.EqualTo(1));
        }
    }
}
