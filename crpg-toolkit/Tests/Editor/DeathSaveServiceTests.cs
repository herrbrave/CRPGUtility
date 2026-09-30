using CRPG.Abilities;
using CRPG.Actors;
using CRPG.Combat;
using CRPG.Core;
using CRPG.Rules.SRD5;
using NUnit.Framework;

namespace CRPG.Tests.EditMode
{
    public class DeathSaveServiceTests
    {
        private static readonly DefinitionId HitPoints = new("resource-hp");
        private static readonly DefinitionId Unconscious = new("condition-unconscious");

        private CharacterRegistry registry;
        private GameEventBus eventBus;
        private CombatLog combatLog;
        private CharacterState character;
        private RuntimeEntityId characterId;

        [SetUp]
        public void SetUp()
        {
            registry = new CharacterRegistry();
            eventBus = new GameEventBus();
            combatLog = new CombatLog();
            character = new CharacterState(RuntimeEntityId.NewId());
            character.AddResource(new ResourceState(HitPoints, max: 20));
            registry.Register(character);
            characterId = character.Id;
        }

        private DeathSaveService CreateService(TestRandomSource random) =>
            new(registry, new DiceService(random), eventBus, HitPoints, Unconscious, combatLog);

        private static DamageEffect CreateDamageEffect(int count, int sides, int modifier)
        {
            var effect = new DamageEffect();
            var hp = TestDefinitionFactory.CreateResourceDefinition("resource-hp", "Hit Points");
            TestDefinitionFactory.SetPrivateField(effect, "hitPointResource", hp);
            TestDefinitionFactory.SetPrivateField(effect, "amount", new DiceExpression(count, sides, modifier));
            return effect;
        }

        [Test]
        public void DroppingToZeroStartsDeathSavesInsteadOfInstantRemoval()
        {
            CreateService(new TestRandomSource());

            var damageRandom = new TestRandomSource();
            damageRandom.EnqueueResult(20); // exactly lethal, zero overkill
            var effect = CreateDamageEffect(1, 20, 0);
            var context = new AbilityExecutionContext(RuntimeEntityId.NewId(), new[] { characterId }, registry, new DiceService(damageRandom), combatLog, eventBus);

            effect.Apply(context);

            Assert.IsNotNull(character.DeathSaves);
            Assert.IsTrue(character.HasCondition(Unconscious));
        }

        [Test]
        public void MassiveOverkillCausesInstantDeathInsteadOfUnconsciousness()
        {
            CreateService(new TestRandomSource());
            var died = false;
            eventBus.Subscribe(GameEvent.CreatureDied, _ => died = true);

            var damageRandom = new TestRandomSource();
            damageRandom.EnqueueResult(40); // 40 damage vs 20 max HP -- 20 overkill >= max HP
            var effect = CreateDamageEffect(1, 40, 0);
            var context = new AbilityExecutionContext(RuntimeEntityId.NewId(), new[] { characterId }, registry, new DiceService(damageRandom), combatLog, eventBus);

            effect.Apply(context);

            Assert.IsTrue(died);
            Assert.IsNull(character.DeathSaves);
            Assert.IsFalse(character.HasCondition(Unconscious));
        }

        // Task 6.1 (2D-Campaign-Phase6-CampaignContent.md): the death-save loop is a player-character rule --
        // real hostile combat exposed that a monster was going Unconscious and rolling death saves at 0 HP like
        // a PC instead of dying outright. The usesDeathSaves predicate is the fix.
        [Test]
        public void ACharacterTheUsesDeathSavesPredicateExcludesDiesOutrightInsteadOfGoingUnconscious()
        {
            var died = false;
            eventBus.Subscribe(GameEvent.CreatureDied, _ => died = true);
            new DeathSaveService(registry, new DiceService(new TestRandomSource()), eventBus, HitPoints, Unconscious, combatLog,
                usesDeathSaves: _ => false); // nobody uses death saves in this test

            var damageRandom = new TestRandomSource();
            damageRandom.EnqueueResult(20); // exactly lethal, zero overkill -- would normally start death saves
            var effect = CreateDamageEffect(1, 20, 0);
            var context = new AbilityExecutionContext(RuntimeEntityId.NewId(), new[] { characterId }, registry, new DiceService(damageRandom), combatLog, eventBus);

            effect.Apply(context);

            Assert.IsTrue(died, "died outright, matching a monster's real SRD5.1 behavior");
            Assert.IsNull(character.DeathSaves);
            Assert.IsFalse(character.HasCondition(Unconscious));
        }

        [Test]
        public void ANullUsesDeathSavesPredicatePreservesTheOriginalEveryoneGetsDeathSavesBehavior()
        {
            // Default (no predicate supplied) -- exact copy of DroppingToZeroStartsDeathSavesInsteadOfInstantRemoval,
            // pinning that omitting the new parameter regresses nothing for every existing caller (e.g. the Gate
            // scene, where everyone who can reach 0 HP is a party member anyway).
            CreateService(new TestRandomSource());

            var damageRandom = new TestRandomSource();
            damageRandom.EnqueueResult(20);
            var effect = CreateDamageEffect(1, 20, 0);
            var context = new AbilityExecutionContext(RuntimeEntityId.NewId(), new[] { characterId }, registry, new DiceService(damageRandom), combatLog, eventBus);

            effect.Apply(context);

            Assert.IsNotNull(character.DeathSaves);
            Assert.IsTrue(character.HasCondition(Unconscious));
        }

        [Test]
        public void DamageWhileAlreadyDownCountsAsAFailureInsteadOfFurtherHpLoss()
        {
            CreateService(new TestRandomSource());
            character.DeathSaves = new DeathSaveState();
            character.TryGetResource(HitPoints, out var hp);
            hp.Current = 0;

            var damageRandom = new TestRandomSource();
            damageRandom.EnqueueResult(5);
            var effect = CreateDamageEffect(1, 6, 0);
            var context = new AbilityExecutionContext(RuntimeEntityId.NewId(), new[] { characterId }, registry, new DiceService(damageRandom), combatLog, eventBus);

            effect.Apply(context);

            Assert.AreEqual(1, character.DeathSaves.Failures);
            Assert.AreEqual(0, hp.Current);
        }

        [Test]
        public void CriticalHitWhileAlreadyDownCountsAsTwoFailures()
        {
            CreateService(new TestRandomSource());
            character.DeathSaves = new DeathSaveState();
            character.TryGetResource(HitPoints, out var hp);
            hp.Current = 0;

            var damageRandom = new TestRandomSource();
            damageRandom.EnqueueResults(3, 3); // crit doubles the dice (1d6 -> 2d6), irrelevant since Amount is rewritten to 0 anyway
            var effect = CreateDamageEffect(1, 6, 0);
            var context = new AbilityExecutionContext(
                RuntimeEntityId.NewId(), new[] { characterId }, registry, new DiceService(damageRandom), combatLog, eventBus,
                criticalTargets: new[] { characterId });

            effect.Apply(context);

            Assert.AreEqual(2, character.DeathSaves.Failures);
        }

        [Test]
        public void ThreeSuccessesStabilizesTheCharacter()
        {
            var random = new TestRandomSource();
            random.EnqueueResults(15, 12, 10); // three successes, none natural 1 or 20
            var service = CreateService(random);
            character.DeathSaves = new DeathSaveState();
            character.ApplyCondition(new ConditionInstance(Unconscious, RuntimeEntityId.None, characterId));

            var stabilized = false;
            eventBus.Subscribe(GameEvent.CreatureStabilized, _ => stabilized = true);

            service.RollDeathSave(characterId);
            service.RollDeathSave(characterId);
            service.RollDeathSave(characterId);

            Assert.IsTrue(stabilized);
            Assert.IsNull(character.DeathSaves);
            Assert.IsTrue(character.HasCondition(Unconscious)); // stays unconscious, just stable now
        }

        [Test]
        public void ThreeFailuresKillsTheCharacter()
        {
            var random = new TestRandomSource();
            random.EnqueueResults(5, 8, 3); // three failures, none natural 1 or 20
            var service = CreateService(random);
            character.DeathSaves = new DeathSaveState();
            character.ApplyCondition(new ConditionInstance(Unconscious, RuntimeEntityId.None, characterId));

            var died = false;
            eventBus.Subscribe(GameEvent.CreatureDied, _ => died = true);

            service.RollDeathSave(characterId);
            service.RollDeathSave(characterId);
            service.RollDeathSave(characterId);

            Assert.IsTrue(died);
            Assert.IsNull(character.DeathSaves);
            Assert.IsFalse(character.HasCondition(Unconscious)); // removed on death
        }

        [Test]
        public void NaturalTwentyHealsToOneHpAndStabilizes()
        {
            var random = new TestRandomSource();
            random.EnqueueResult(20);
            var service = CreateService(random);
            character.DeathSaves = new DeathSaveState();
            character.ApplyCondition(new ConditionInstance(Unconscious, RuntimeEntityId.None, characterId));
            character.TryGetResource(HitPoints, out var hp);
            hp.Current = 0;

            var stabilized = false;
            eventBus.Subscribe(GameEvent.CreatureStabilized, _ => stabilized = true);

            service.RollDeathSave(characterId);

            Assert.IsTrue(stabilized);
            Assert.IsNull(character.DeathSaves);
            Assert.IsFalse(character.HasCondition(Unconscious));
            Assert.AreEqual(1, hp.Current);
        }

        [Test]
        public void NaturalOneCountsAsTwoFailures()
        {
            var random = new TestRandomSource();
            random.EnqueueResult(1);
            var service = CreateService(random);
            character.DeathSaves = new DeathSaveState();

            service.RollDeathSave(characterId);

            Assert.AreEqual(2, character.DeathSaves.Failures);
        }

        [Test]
        public void RollDeathSaveDoesNothingWhenCharacterIsNotDying()
        {
            var service = CreateService(new TestRandomSource()); // never rolled -- would throw if it tried

            Assert.DoesNotThrow(() => service.RollDeathSave(characterId));
            Assert.IsNull(character.DeathSaves);
        }
    }
}
