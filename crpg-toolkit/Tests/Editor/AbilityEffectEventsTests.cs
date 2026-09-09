using CRPG.Abilities;
using CRPG.Actors;
using CRPG.Core;
using NUnit.Framework;

namespace CRPG.Tests.EditMode
{
    public class AbilityEffectEventsTests
    {
        [Test]
        public void BeforeDamageHandlerCanRewriteTheFinalAmount()
        {
            var hp = TestDefinitionFactory.CreateResourceDefinition("resource-hp", "Hit Points");
            var registry = new CharacterRegistry();
            var target = new CharacterState(RuntimeEntityId.NewId());
            target.AddResource(new ResourceState(hp.Id, max: 20));
            registry.Register(target);

            var effect = new DamageEffect();
            TestDefinitionFactory.SetPrivateField(effect, "hitPointResource", hp);
            TestDefinitionFactory.SetPrivateField(effect, "amount", new DiceExpression(1, 6));

            var random = new TestRandomSource();
            random.EnqueueResult(6); // rolled 6, a "resistance" handler below halves it to 3
            var eventBus = new GameEventBus();
            eventBus.Subscribe(GameEvent.BeforeDamage, ctx => ctx.Data["Amount"] = (int)ctx.Data["Amount"] / 2);

            var context = new AbilityExecutionContext(
                RuntimeEntityId.NewId(), new[] { target.Id }, registry, new DiceService(random), null, eventBus);

            effect.Apply(context);

            target.TryGetResource(hp.Id, out var hpState);
            Assert.AreEqual(17, hpState.Current); // 20 - 3, not 20 - 6
        }

        [Test]
        public void DamageEffectPublishesCreatureDroppedToZeroHPWhenItKills()
        {
            var hp = TestDefinitionFactory.CreateResourceDefinition("resource-hp", "Hit Points");
            var registry = new CharacterRegistry();
            var target = new CharacterState(RuntimeEntityId.NewId());
            target.AddResource(new ResourceState(hp.Id, max: 10, current: 5));
            registry.Register(target);

            var effect = new DamageEffect();
            TestDefinitionFactory.SetPrivateField(effect, "hitPointResource", hp);
            TestDefinitionFactory.SetPrivateField(effect, "amount", new DiceExpression(1, 6));

            var random = new TestRandomSource();
            random.EnqueueResult(6);
            var eventBus = new GameEventBus();
            var died = false;
            eventBus.Subscribe(GameEvent.CreatureDroppedToZeroHP, _ => died = true);

            var context = new AbilityExecutionContext(
                RuntimeEntityId.NewId(), new[] { target.Id }, registry, new DiceService(random), null, eventBus);

            effect.Apply(context);

            Assert.IsTrue(died);
        }

        [Test]
        public void ApplyAndRemoveConditionEffectsPublishTheirEvents()
        {
            var condition = TestDefinitionFactory.CreateConditionDefinition("condition-poisoned", "Poisoned");
            var registry = new CharacterRegistry();
            var target = new CharacterState(RuntimeEntityId.NewId());
            registry.Register(target);

            var eventBus = new GameEventBus();
            var applied = false;
            var removed = false;
            eventBus.Subscribe(GameEvent.ConditionApplied, _ => applied = true);
            eventBus.Subscribe(GameEvent.ConditionRemoved, _ => removed = true);

            var apply = new ApplyConditionEffect();
            TestDefinitionFactory.SetPrivateField(apply, "condition", condition);
            TestDefinitionFactory.SetPrivateField(apply, "duration", -1);
            var context = new AbilityExecutionContext(
                RuntimeEntityId.NewId(), new[] { target.Id }, registry, new DiceService(new SeededRandomSource(1)), null, eventBus);
            apply.Apply(context);

            var remove = new RemoveConditionEffect();
            TestDefinitionFactory.SetPrivateField(remove, "condition", condition);
            remove.Apply(context);

            Assert.IsTrue(applied);
            Assert.IsTrue(removed);
        }
    }
}
