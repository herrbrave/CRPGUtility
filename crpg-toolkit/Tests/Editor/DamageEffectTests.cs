using CRPG.Abilities;
using CRPG.Actors;
using CRPG.Core;
using NUnit.Framework;

namespace CRPG.Tests.EditMode
{
    public class DamageEffectTests
    {
        private static readonly DefinitionId HitPoints = new("resource-hp");
        private static readonly DefinitionId Fire = new("damage-fire");

        private sealed class FixedDamageResistanceRule : IDamageResistanceRule
        {
            private readonly int result;
            public FixedDamageResistanceRule(int result) => this.result = result;
            public DefinitionId LastDamageTypeId { get; private set; }
            public int ApplyResistance(RuntimeEntityId target, DefinitionId damageTypeId, int rawAmount) { LastDamageTypeId = damageTypeId; return result; }
        }

        private static DamageEffect CreateEffect(int count, int sides, int modifier, DefinitionId damageTypeId = default)
        {
            var effect = new DamageEffect();
            var hp = TestDefinitionFactory.CreateResourceDefinition("resource-hp", "Hit Points");
            TestDefinitionFactory.SetPrivateField(effect, "hitPointResource", hp);
            TestDefinitionFactory.SetPrivateField(effect, "amount", new DiceExpression(count, sides, modifier));
            TestDefinitionFactory.SetPrivateField(effect, "damageTypeId", damageTypeId);
            return effect;
        }

        private static (CharacterRegistry Registry, RuntimeEntityId Source, RuntimeEntityId Target) SetUpTarget()
        {
            var registry = new CharacterRegistry();
            var source = new CharacterState(RuntimeEntityId.NewId());
            registry.Register(source);

            var target = new CharacterState(RuntimeEntityId.NewId());
            target.AddResource(new ResourceState(HitPoints, max: 20));
            registry.Register(target);

            return (registry, source.Id, target.Id);
        }

        [Test]
        public void AppliesResistanceRuleWhenDamageIsTyped()
        {
            var (registry, source, target) = SetUpTarget();
            var random = new TestRandomSource();
            random.EnqueueResult(5); // raw roll irrelevant (must still be a legal d6 face) -- rule overrides it
            var dice = new DiceService(random);

            var effect = CreateEffect(1, 6, 0, Fire);
            var resistanceRule = new FixedDamageResistanceRule(3);
            var context = new AbilityExecutionContext(
                source, new[] { target }, registry, dice, new CombatLog(), damageResistanceRule: resistanceRule);

            effect.Apply(context);

            Assert.AreEqual(Fire, resistanceRule.LastDamageTypeId);
            registry.TryGetCharacter(target, out var targetCharacter);
            targetCharacter.TryGetResource(HitPoints, out var hp);
            Assert.AreEqual(17, hp.Current); // 20 - 3 (the rule's overridden amount, not the raw roll)
        }

        [Test]
        public void SkipsResistanceRuleWhenDamageIsUntyped()
        {
            var (registry, source, target) = SetUpTarget();
            var random = new TestRandomSource();
            random.EnqueueResult(4);
            var dice = new DiceService(random);

            var effect = CreateEffect(1, 6, 0); // damageTypeId left as DefinitionId.None
            var resistanceRule = new FixedDamageResistanceRule(999);
            var context = new AbilityExecutionContext(
                source, new[] { target }, registry, dice, new CombatLog(), damageResistanceRule: resistanceRule);

            effect.Apply(context);

            registry.TryGetCharacter(target, out var targetCharacter);
            targetCharacter.TryGetResource(HitPoints, out var hp);
            Assert.AreEqual(16, hp.Current); // 20 - 4 (raw roll, rule never consulted)
        }

        [Test]
        public void ScalesWithCasterLevelAddsExtraDiceAtBreakpoints()
        {
            var (registry, source, target) = SetUpTarget();
            registry.TryGetCharacter(source, out var caster);
            caster.Level = 11; // +2 extra dice

            var random = new TestRandomSource();
            random.EnqueueResults(4, 4, 4); // base 1d6 + 2 scaled dice = 3d6
            var dice = new DiceService(random);

            var effect = CreateEffect(1, 6, 0);
            TestDefinitionFactory.SetPrivateField(effect, "scalesWithCasterLevel", true);
            var context = new AbilityExecutionContext(source, new[] { target }, registry, dice, new CombatLog());

            effect.Apply(context);

            registry.TryGetCharacter(target, out var targetCharacter);
            targetCharacter.TryGetResource(HitPoints, out var hp);
            Assert.AreEqual(8, hp.Current); // 20 - (4+4+4)
        }

        [Test]
        public void DoesNotScaleBelowLevelFiveEvenWhenFlagIsSet()
        {
            var (registry, source, target) = SetUpTarget();
            registry.TryGetCharacter(source, out var caster);
            caster.Level = 4;

            var random = new TestRandomSource();
            random.EnqueueResult(4); // just the base 1d6
            var dice = new DiceService(random);

            var effect = CreateEffect(1, 6, 0);
            TestDefinitionFactory.SetPrivateField(effect, "scalesWithCasterLevel", true);
            var context = new AbilityExecutionContext(source, new[] { target }, registry, dice, new CombatLog());

            effect.Apply(context);

            registry.TryGetCharacter(target, out var targetCharacter);
            targetCharacter.TryGetResource(HitPoints, out var hp);
            Assert.AreEqual(16, hp.Current); // 20 - 4
        }

        [Test]
        public void UpcastLevelsAddExtraDicePerConfiguredCount()
        {
            var (registry, source, target) = SetUpTarget();
            var random = new TestRandomSource();
            random.EnqueueResults(4, 4, 4); // base 1d6 + 2 upcast levels * 1 extra die = 3d6
            var dice = new DiceService(random);

            var effect = CreateEffect(1, 6, 0);
            TestDefinitionFactory.SetPrivateField(effect, "extraDiceCountPerSlotLevel", 1);
            var context = new AbilityExecutionContext(
                source, new[] { target }, registry, dice, new CombatLog(), upcastLevels: 2);

            effect.Apply(context);

            registry.TryGetCharacter(target, out var targetCharacter);
            targetCharacter.TryGetResource(HitPoints, out var hp);
            Assert.AreEqual(8, hp.Current); // 20 - (4+4+4)
        }

        [Test]
        public void UpcastLevelsDoNothingWhenNoExtraDiceAreConfigured()
        {
            var (registry, source, target) = SetUpTarget();
            var random = new TestRandomSource();
            random.EnqueueResult(4); // just the base 1d6
            var dice = new DiceService(random);

            var effect = CreateEffect(1, 6, 0); // extraDiceCountPerSlotLevel left at 0
            var context = new AbilityExecutionContext(
                source, new[] { target }, registry, dice, new CombatLog(), upcastLevels: 3);

            effect.Apply(context);

            registry.TryGetCharacter(target, out var targetCharacter);
            targetCharacter.TryGetResource(HitPoints, out var hp);
            Assert.AreEqual(16, hp.Current); // 20 - 4
        }

        [Test]
        public void DoesNotThrowWhenTypedButNoResistanceRuleIsWiredUp()
        {
            var (registry, source, target) = SetUpTarget();
            var random = new TestRandomSource();
            random.EnqueueResult(4);
            var dice = new DiceService(random);

            var effect = CreateEffect(1, 6, 0, Fire);
            var context = new AbilityExecutionContext(source, new[] { target }, registry, dice, new CombatLog());

            Assert.DoesNotThrow(() => effect.Apply(context));
            registry.TryGetCharacter(target, out var targetCharacter);
            targetCharacter.TryGetResource(HitPoints, out var hp);
            Assert.AreEqual(16, hp.Current);
        }
    }
}
