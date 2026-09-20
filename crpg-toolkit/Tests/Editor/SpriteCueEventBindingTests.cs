using System.Collections.Generic;
using CRPG.Abilities;
using CRPG.Actors;
using CRPG.Combat;
using CRPG.Core;
using CRPG.Presentation.Sprites;
using CRPG.Rules.SRD5;
using NUnit.Framework;
using UnityEngine;

namespace CRPG.Tests.EditMode
{
    /// <summary>2D campaign Task 2.6: the event-bus mapping, including real-engine integration with DamageEffect and DeathSaveService.</summary>
    public class SpriteCueEventBindingTests
    {
        private static readonly DefinitionId HitPoints = new("resource-hp");
        private static readonly DefinitionId Unconscious = new("condition-unconscious");

        private CharacterRegistry registry;
        private GameEventBus eventBus;
        private CombatLog combatLog;
        private CharacterState character;
        private RuntimeEntityId id;
        private ResourceState hp;
        private SpriteCueState state;
        private SpriteCueEventBinding binding;

        [SetUp]
        public void SetUp()
        {
            registry = new CharacterRegistry();
            eventBus = new GameEventBus();
            combatLog = new CombatLog();
            character = new CharacterState(RuntimeEntityId.NewId());
            character.AddResource(new ResourceState(HitPoints, max: 20));
            registry.Register(character);
            id = character.Id;
            character.TryGetResource(HitPoints, out hp);
            state = new SpriteCueState();
            binding = new SpriteCueEventBinding(id, eventBus, registry, HitPoints, state);
        }

        [TearDown]
        public void TearDown() => binding?.Dispose();

        private void Publish(GameEvent e, RuntimeEntityId target) =>
            eventBus.Publish(new GameEventContext(e, RuntimeEntityId.NewId(), target));

        private static DamageEffect CreateDamageEffect()
        {
            var effect = new DamageEffect();
            var def = TestDefinitionFactory.CreateResourceDefinition("resource-hp", "Hit Points");
            TestDefinitionFactory.SetPrivateField(effect, "hitPointResource", def);
            TestDefinitionFactory.SetPrivateField(effect, "amount", new DiceExpression(1, 100, 0));
            return effect;
        }

        private void Damage(int amount, bool critical = false)
        {
            var random = new TestRandomSource();
            random.EnqueueResults(amount, amount);
            var context = critical
                ? new AbilityExecutionContext(RuntimeEntityId.NewId(), new[] { id }, registry, new DiceService(random), combatLog, eventBus, criticalTargets: new[] { id })
                : new AbilityExecutionContext(RuntimeEntityId.NewId(), new[] { id }, registry, new DiceService(random), combatLog, eventBus);
            CreateDamageEffect().Apply(context);
        }

        private DeathSaveService Service(params int[] rolls)
        {
            var random = new TestRandomSource();
            random.EnqueueResults(rolls);
            return new DeathSaveService(registry, new DiceService(random), eventBus, HitPoints, Unconscious, combatLog);
        }

        private void GoDown()
        {
            character.DeathSaves = new DeathSaveState();
            character.ApplyCondition(new ConditionInstance(Unconscious, RuntimeEntityId.None, id));
            hp.Current = 0;
            state.Down();
        }

        [Test]
        public void AfterDamageOnTheCharacterFlashes()
        {
            Publish(GameEvent.AfterDamage, id);
            Assert.AreEqual(new SpriteCueSettings().hitTint, state.Tint);
        }

        [Test]
        public void EventsForOtherCharactersAreIgnored()
        {
            var other = RuntimeEntityId.NewId();
            Publish(GameEvent.AfterDamage, other);
            Publish(GameEvent.CreatureDroppedToZeroHP, other);
            Publish(GameEvent.CreatureDied, other);
            Assert.AreEqual(Color.white, state.Tint);
            Assert.IsFalse(state.IsDown);
            Assert.IsFalse(state.IsDead);
        }

        [Test]
        public void DroppedToZeroGoesDownAndDiedIsDead()
        {
            Publish(GameEvent.CreatureDroppedToZeroHP, id);
            Assert.IsTrue(state.IsDown);
            Publish(GameEvent.CreatureDied, id);
            Assert.IsTrue(state.IsDead);
        }

        [Test]
        public void RealDamageThatDropsToZeroGoesDown()
        {
            Service();
            Damage(20);
            Assert.IsTrue(state.IsDown);
            Assert.IsFalse(state.IsDead);
        }

        [Test]
        public void RealMassiveDamagePublishesDiedBeforeDroppedToZeroAndEndsDead()
        {
            Service();
            var order = new List<GameEvent>();
            eventBus.Subscribe(GameEvent.CreatureDied, _ => order.Add(GameEvent.CreatureDied));
            eventBus.Subscribe(GameEvent.CreatureDroppedToZeroHP, _ => order.Add(GameEvent.CreatureDroppedToZeroHP));

            Damage(40);

            Assert.AreEqual(GameEvent.CreatureDied, order[0], "engine order this binding must survive");
            Assert.IsTrue(state.IsDead);
        }

        [Test]
        public void RealKillingBlowOnADownedCharacterPublishesDiedBeforeAfterDamageAndEndsDead()
        {
            Service();
            character.DeathSaves = new DeathSaveState { Failures = 2 };
            hp.Current = 0;
            state.Down();
            var order = new List<GameEvent>();
            eventBus.Subscribe(GameEvent.CreatureDied, _ => order.Add(GameEvent.CreatureDied));
            eventBus.Subscribe(GameEvent.AfterDamage, _ => order.Add(GameEvent.AfterDamage));

            Damage(5);

            Assert.AreEqual(GameEvent.CreatureDied, order[0]);
            Assert.IsTrue(state.IsDead);
        }

        [Test]
        public void RealCriticalOnADownedCharacterWithOneFailureKills()
        {
            Service();
            character.DeathSaves = new DeathSaveState { Failures = 1 };
            hp.Current = 0;
            state.Down();

            Damage(5, critical: true); // two failures -> three

            Assert.IsTrue(state.IsDead);
        }

        [Test]
        public void ThreeSuccessfulSavesLeaveTheCharacterDown()
        {
            var service = Service(15, 12, 10);
            GoDown();

            service.RollDeathSave(id);
            service.RollDeathSave(id);
            service.RollDeathSave(id);

            Assert.IsTrue(state.IsDown, "stabilized at 0 HP is still unconscious");
        }

        [Test]
        public void NaturalTwentyRecovers()
        {
            var service = Service(20);
            GoDown();

            service.RollDeathSave(id);

            Assert.IsFalse(state.IsDown);
            Assert.IsFalse(state.IsDead);
        }

        [Test]
        public void HealPlusRemovingUnconsciousRecoversViaConditionRemoved()
        {
            GoDown();
            hp.Current = 5;
            var remove = new RemoveConditionEffect();
            TestDefinitionFactory.SetPrivateField(remove, "condition", TestDefinitionFactory.CreateConditionDefinition("condition-unconscious", "Unconscious"));
            remove.Apply(new AbilityExecutionContext(RuntimeEntityId.NewId(), new[] { id }, registry, new DiceService(new TestRandomSource()), combatLog, eventBus));

            Assert.IsFalse(state.IsDown);
        }

        [Test]
        public void ConditionRemovedAtZeroHpDoesNotRecover()
        {
            GoDown();
            Publish(GameEvent.ConditionRemoved, id);
            Assert.IsTrue(state.IsDown);
        }

        [Test]
        public void NoHitPointIdConfiguredNeverRecovers()
        {
            binding.Dispose();
            state = new SpriteCueState();
            binding = new SpriteCueEventBinding(id, eventBus, registry, default, state);
            state.Down();
            hp.Current = 10;

            Publish(GameEvent.CreatureStabilized, id);
            Publish(GameEvent.ConditionRemoved, id);

            Assert.IsTrue(state.IsDown);
        }

        [Test]
        public void CharacterMissingFromTheRegistryNeverRecovers()
        {
            binding.Dispose();
            state = new SpriteCueState();
            var ghost = RuntimeEntityId.NewId();
            binding = new SpriteCueEventBinding(ghost, eventBus, registry, HitPoints, state);
            state.Down();
            Publish(GameEvent.CreatureStabilized, ghost);
            Assert.IsTrue(state.IsDown);
        }

        [Test]
        public void DisposedBindingIgnoresLaterEvents()
        {
            binding.Dispose();
            Publish(GameEvent.CreatureDied, id);
            Assert.IsFalse(state.IsDead);
            Assert.DoesNotThrow(() => binding.Dispose());
        }

        [Test]
        public void DisposeInsideAnEarlierHandlerOfTheSamePublishStopsTheBinding()
        {
            // The bus captured the invocation list before the earlier handler unsubscribed the binding; only the disposed flag stops it.
            binding.Dispose();
            state = new SpriteCueState();
            eventBus.Subscribe(GameEvent.CreatureDied, _ => binding.Dispose()); // subscribed first, so it runs first
            binding = new SpriteCueEventBinding(id, eventBus, registry, HitPoints, state);

            Publish(GameEvent.CreatureDied, id);

            Assert.IsFalse(state.IsDead);
        }

        [Test]
        public void IsDownAtBindReadsDeathSavesOrZeroHp()
        {
            Assert.IsFalse(SpriteCueEventBinding.IsDownAtBind(id, registry, HitPoints));

            character.DeathSaves = new DeathSaveState();
            Assert.IsTrue(SpriteCueEventBinding.IsDownAtBind(id, registry, HitPoints));
            Assert.IsTrue(SpriteCueEventBinding.IsDownAtBind(id, registry, default));

            character.DeathSaves = null;
            hp.Current = 0;
            Assert.IsTrue(SpriteCueEventBinding.IsDownAtBind(id, registry, HitPoints), "stable at 0 HP has no death-save state");
            Assert.IsFalse(SpriteCueEventBinding.IsDownAtBind(id, registry, default), "no hp id: cannot know");

            Assert.IsFalse(SpriteCueEventBinding.IsDownAtBind(RuntimeEntityId.NewId(), registry, HitPoints));
            Assert.IsFalse(SpriteCueEventBinding.IsDownAtBind(id, null, HitPoints));
        }
    }
}
