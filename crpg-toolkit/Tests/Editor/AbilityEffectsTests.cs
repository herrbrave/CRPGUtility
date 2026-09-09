using System.Collections.Generic;
using CRPG.Abilities;
using CRPG.Actors;
using CRPG.Core;
using NUnit.Framework;

namespace CRPG.Tests.EditMode
{
    public class AbilityEffectsTests
    {
        private static AbilityExecutionContext CreateContext(
            RuntimeEntityId source, IReadOnlyList<RuntimeEntityId> targets, CharacterRegistry registry, IDiceService dice = null) =>
            new(source, targets, registry, dice ?? new DiceService(new SeededRandomSource(1)), null);

        [Test]
        public void DamageEffectSpendsRolledAmountFromHitPointResource()
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
            random.EnqueueResult(4);
            var context = CreateContext(RuntimeEntityId.NewId(), new[] { target.Id }, registry, new DiceService(random));

            effect.Apply(context);

            target.TryGetResource(hp.Id, out var hpState);
            Assert.AreEqual(16, hpState.Current);
        }

        [Test]
        public void HealEffectRestoresRolledAmountToHitPointResource()
        {
            var hp = TestDefinitionFactory.CreateResourceDefinition("resource-hp", "Hit Points");
            var registry = new CharacterRegistry();
            var target = new CharacterState(RuntimeEntityId.NewId());
            target.AddResource(new ResourceState(hp.Id, max: 20, current: 5));
            registry.Register(target);

            var effect = new HealEffect();
            TestDefinitionFactory.SetPrivateField(effect, "hitPointResource", hp);
            TestDefinitionFactory.SetPrivateField(effect, "amount", new DiceExpression(1, 8));

            var random = new TestRandomSource();
            random.EnqueueResult(6);
            var context = CreateContext(RuntimeEntityId.NewId(), new[] { target.Id }, registry, new DiceService(random));

            effect.Apply(context);

            target.TryGetResource(hp.Id, out var hpState);
            Assert.AreEqual(11, hpState.Current);
        }

        [Test]
        public void ApplyConditionEffectAppliesToEachTarget()
        {
            var condition = TestDefinitionFactory.CreateConditionDefinition("condition-poisoned", "Poisoned");
            var registry = new CharacterRegistry();
            var target = new CharacterState(RuntimeEntityId.NewId());
            registry.Register(target);

            var effect = new ApplyConditionEffect();
            TestDefinitionFactory.SetPrivateField(effect, "condition", condition);
            TestDefinitionFactory.SetPrivateField(effect, "duration", -1);

            effect.Apply(CreateContext(RuntimeEntityId.NewId(), new[] { target.Id }, registry));

            Assert.IsTrue(target.HasCondition(condition.Id));
        }

        [Test]
        public void RemoveConditionEffectRemovesMatchingCondition()
        {
            var condition = TestDefinitionFactory.CreateConditionDefinition("condition-poisoned", "Poisoned");
            var registry = new CharacterRegistry();
            var target = new CharacterState(RuntimeEntityId.NewId());
            target.ApplyCondition(new ConditionInstance(condition.Id, RuntimeEntityId.NewId(), target.Id));
            registry.Register(target);

            var effect = new RemoveConditionEffect();
            TestDefinitionFactory.SetPrivateField(effect, "condition", condition);

            effect.Apply(CreateContext(RuntimeEntityId.NewId(), new[] { target.Id }, registry));

            Assert.IsFalse(target.HasCondition(condition.Id));
        }

        [Test]
        public void SpendResourceEffectDefaultsToApplyingToTheCaster()
        {
            var resource = TestDefinitionFactory.CreateResourceDefinition("resource-ki", "Ki");
            var registry = new CharacterRegistry();
            var caster = new CharacterState(RuntimeEntityId.NewId());
            caster.AddResource(new ResourceState(resource.Id, max: 5));
            registry.Register(caster);

            var effect = new SpendResourceEffect();
            TestDefinitionFactory.SetPrivateField(effect, "resource", resource);
            TestDefinitionFactory.SetPrivateField(effect, "amount", 2);
            TestDefinitionFactory.SetPrivateField(effect, "applyToSelf", true);

            effect.Apply(CreateContext(caster.Id, System.Array.Empty<RuntimeEntityId>(), registry));

            caster.TryGetResource(resource.Id, out var state);
            Assert.AreEqual(3, state.Current);
        }

        [Test]
        public void RestoreResourceEffectCanTargetOthersInsteadOfTheCaster()
        {
            var resource = TestDefinitionFactory.CreateResourceDefinition("resource-hp", "Hit Points");
            var registry = new CharacterRegistry();
            var target = new CharacterState(RuntimeEntityId.NewId());
            target.AddResource(new ResourceState(resource.Id, max: 10, current: 2));
            registry.Register(target);

            var effect = new RestoreResourceEffect();
            TestDefinitionFactory.SetPrivateField(effect, "resource", resource);
            TestDefinitionFactory.SetPrivateField(effect, "amount", 3);
            TestDefinitionFactory.SetPrivateField(effect, "applyToSelf", false);

            effect.Apply(CreateContext(RuntimeEntityId.NewId(), new[] { target.Id }, registry));

            target.TryGetResource(resource.Id, out var state);
            Assert.AreEqual(5, state.Current);
        }

        [Test]
        public void GrantAndRemoveFeatureEffectsToggleTheFeature()
        {
            var feature = TestDefinitionFactory.CreateFeatureDefinition("feature-rage", "Rage");
            var registry = new CharacterRegistry();
            var target = new CharacterState(RuntimeEntityId.NewId());
            registry.Register(target);

            var grant = new GrantFeatureEffect();
            TestDefinitionFactory.SetPrivateField(grant, "feature", feature);
            TestDefinitionFactory.SetPrivateField(grant, "applyToSelf", false);
            grant.Apply(CreateContext(RuntimeEntityId.NewId(), new[] { target.Id }, registry));
            Assert.IsTrue(target.HasFeature(feature.Id));

            var remove = new RemoveFeatureEffect();
            TestDefinitionFactory.SetPrivateField(remove, "feature", feature);
            TestDefinitionFactory.SetPrivateField(remove, "applyToSelf", false);
            remove.Apply(CreateContext(RuntimeEntityId.NewId(), new[] { target.Id }, registry));
            Assert.IsFalse(target.HasFeature(feature.Id));
        }

        [Test]
        public void SequenceEffectAppliesEveryChildEffect()
        {
            var feature = TestDefinitionFactory.CreateFeatureDefinition("feature-a", "A");
            var condition = TestDefinitionFactory.CreateConditionDefinition("condition-a", "A");
            var registry = new CharacterRegistry();
            var target = new CharacterState(RuntimeEntityId.NewId());
            registry.Register(target);

            var grantFeature = new GrantFeatureEffect();
            TestDefinitionFactory.SetPrivateField(grantFeature, "feature", feature);
            TestDefinitionFactory.SetPrivateField(grantFeature, "applyToSelf", false);

            var applyCondition = new ApplyConditionEffect();
            TestDefinitionFactory.SetPrivateField(applyCondition, "condition", condition);
            TestDefinitionFactory.SetPrivateField(applyCondition, "duration", -1);

            var sequence = new SequenceEffect();
            TestDefinitionFactory.SetPrivateField(sequence, "effects", new List<AbilityEffect> { grantFeature, applyCondition });

            sequence.Apply(CreateContext(RuntimeEntityId.NewId(), new[] { target.Id }, registry));

            Assert.IsTrue(target.HasFeature(feature.Id));
            Assert.IsTrue(target.HasCondition(condition.Id));
        }

        [Test]
        public void ConditionalEffectOnlyAppliesToTargetsMeetingTheCondition()
        {
            var requiredCondition = TestDefinitionFactory.CreateConditionDefinition("condition-prone", "Prone");
            var grantedFeature = TestDefinitionFactory.CreateFeatureDefinition("feature-vulnerable", "Vulnerable");
            var registry = new CharacterRegistry();
            var proneTarget = new CharacterState(RuntimeEntityId.NewId());
            var standingTarget = new CharacterState(RuntimeEntityId.NewId());
            proneTarget.ApplyCondition(new ConditionInstance(requiredCondition.Id, RuntimeEntityId.NewId(), proneTarget.Id));
            registry.Register(proneTarget);
            registry.Register(standingTarget);

            var grantFeature = new GrantFeatureEffect();
            TestDefinitionFactory.SetPrivateField(grantFeature, "feature", grantedFeature);
            TestDefinitionFactory.SetPrivateField(grantFeature, "applyToSelf", false);

            var conditional = new ConditionalEffect();
            TestDefinitionFactory.SetPrivateField(conditional, "requiredCondition", requiredCondition);
            TestDefinitionFactory.SetPrivateField(conditional, "requireAbsence", false);
            TestDefinitionFactory.SetPrivateField(conditional, "thenEffect", grantFeature);

            conditional.Apply(CreateContext(RuntimeEntityId.NewId(), new[] { proneTarget.Id, standingTarget.Id }, registry));

            Assert.IsTrue(proneTarget.HasFeature(grantedFeature.Id));
            Assert.IsFalse(standingTarget.HasFeature(grantedFeature.Id));
        }
    }
}
