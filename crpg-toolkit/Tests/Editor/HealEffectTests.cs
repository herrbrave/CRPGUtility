using CRPG.Abilities;
using CRPG.Actors;
using CRPG.Core;
using NUnit.Framework;

namespace CRPG.Tests.EditMode
{
    public class HealEffectTests
    {
        private static readonly DefinitionId HitPoints = new("resource-hp");

        private static HealEffect CreateEffect(int count, int sides, int modifier)
        {
            var effect = new HealEffect();
            var hp = TestDefinitionFactory.CreateResourceDefinition("resource-hp", "Hit Points");
            TestDefinitionFactory.SetPrivateField(effect, "hitPointResource", hp);
            TestDefinitionFactory.SetPrivateField(effect, "amount", new DiceExpression(count, sides, modifier));
            return effect;
        }

        private static (CharacterRegistry Registry, RuntimeEntityId Source, RuntimeEntityId Target) SetUpTarget()
        {
            var registry = new CharacterRegistry();
            var source = new CharacterState(RuntimeEntityId.NewId());
            registry.Register(source);

            var target = new CharacterState(RuntimeEntityId.NewId());
            target.AddResource(new ResourceState(HitPoints, max: 20, current: 5));
            registry.Register(target);

            return (registry, source.Id, target.Id);
        }

        [Test]
        public void RestoresTheRolledAmount()
        {
            var (registry, source, target) = SetUpTarget();
            var random = new TestRandomSource();
            random.EnqueueResult(6);
            var dice = new DiceService(random);

            var effect = CreateEffect(1, 8, 0);
            var context = new AbilityExecutionContext(source, new[] { target }, registry, dice, new CombatLog());

            effect.Apply(context);

            registry.TryGetCharacter(target, out var targetCharacter);
            targetCharacter.TryGetResource(HitPoints, out var hp);
            Assert.AreEqual(11, hp.Current); // 5 + 6
        }

        [Test]
        public void UpcastLevelsAddExtraDicePerConfiguredCount()
        {
            var (registry, source, target) = SetUpTarget();
            var random = new TestRandomSource();
            random.EnqueueResults(6, 6); // base 1d8 + 1 upcast level * 1 extra die = 2d8
            var dice = new DiceService(random);

            var effect = CreateEffect(1, 8, 0);
            TestDefinitionFactory.SetPrivateField(effect, "extraDiceCountPerSlotLevel", 1);
            var context = new AbilityExecutionContext(source, new[] { target }, registry, dice, new CombatLog(), upcastLevels: 1);

            effect.Apply(context);

            registry.TryGetCharacter(target, out var targetCharacter);
            targetCharacter.TryGetResource(HitPoints, out var hp);
            Assert.AreEqual(17, hp.Current); // 5 + 6 + 6
        }

        [Test]
        public void RestoreNeverExceedsMax()
        {
            var (registry, source, target) = SetUpTarget();
            var random = new TestRandomSource();
            random.EnqueueResults(8, 8);
            var dice = new DiceService(random);

            var effect = CreateEffect(1, 8, 0);
            var context = new AbilityExecutionContext(source, new[] { target }, registry, dice, new CombatLog());

            effect.Apply(context);
            effect.Apply(context); // heal again, past max

            registry.TryGetCharacter(target, out var targetCharacter);
            targetCharacter.TryGetResource(HitPoints, out var hp);
            Assert.AreEqual(20, hp.Current);
        }
    }
}
