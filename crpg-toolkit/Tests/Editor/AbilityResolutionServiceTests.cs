using System;
using System.Collections.Generic;
using CRPG.Abilities;
using CRPG.Actors;
using CRPG.Core;
using NUnit.Framework;
using UnityEngine;

namespace CRPG.Tests.EditMode
{
    public class AbilityResolutionServiceTests
    {
        private static AbilityDefinition CreateAbility(
            ResourceDefinition costResource, int costAmount, int minTargets, int maxTargets, params AbilityEffect[] effects)
        {
            var ability = ScriptableObject.CreateInstance<AbilityDefinition>();
            TestDefinitionFactory.SetPrivateField(ability, "id", "ability-test");
            TestDefinitionFactory.SetPrivateField(ability, "displayName", "Test Ability");

            object boxedCost = new AbilityCost();
            TestDefinitionFactory.SetPrivateField(boxedCost, "resource", costResource);
            TestDefinitionFactory.SetPrivateField(boxedCost, "amount", costAmount);
            TestDefinitionFactory.SetPrivateField(ability, "costs", new List<AbilityCost> { (AbilityCost)boxedCost });

            var targeting = new TargetingDefinition();
            TestDefinitionFactory.SetPrivateField(targeting, "minTargets", minTargets);
            TestDefinitionFactory.SetPrivateField(targeting, "maxTargets", maxTargets);
            TestDefinitionFactory.SetPrivateField(ability, "targeting", targeting);

            TestDefinitionFactory.SetPrivateField(ability, "effects", new List<AbilityEffect>(effects));

            return ability;
        }

        [Test]
        public void CanUseIsFalseWhenResourceIsInsufficient()
        {
            var resource = TestDefinitionFactory.CreateResourceDefinition("resource-action", "Action");
            var registry = new CharacterRegistry();
            var caster = new CharacterState(RuntimeEntityId.NewId());
            caster.AddResource(new ResourceState(resource.Id, max: 1, current: 0));
            registry.Register(caster);

            var ability = CreateAbility(resource, 1, 1, 1);
            var service = new AbilityResolutionService(registry, new DiceService(new SeededRandomSource(1)));

            Assert.IsFalse(service.CanUse(caster.Id, ability));
        }

        [Test]
        public void CanUseIsTrueWhenResourceIsAvailable()
        {
            var resource = TestDefinitionFactory.CreateResourceDefinition("resource-action", "Action");
            var registry = new CharacterRegistry();
            var caster = new CharacterState(RuntimeEntityId.NewId());
            caster.AddResource(new ResourceState(resource.Id, max: 1));
            registry.Register(caster);

            var ability = CreateAbility(resource, 1, 1, 1);
            var service = new AbilityResolutionService(registry, new DiceService(new SeededRandomSource(1)));

            Assert.IsTrue(service.CanUse(caster.Id, ability));
        }

        [Test]
        public void CanUseIsFalseWhenCharacterIsMakingDeathSaves()
        {
            var resource = TestDefinitionFactory.CreateResourceDefinition("resource-action", "Action");
            var registry = new CharacterRegistry();
            var caster = new CharacterState(RuntimeEntityId.NewId()) { DeathSaves = new DeathSaveState() };
            caster.AddResource(new ResourceState(resource.Id, max: 1)); // resource is available -- only DeathSaves should block this
            registry.Register(caster);

            var ability = CreateAbility(resource, 1, 1, 1);
            var service = new AbilityResolutionService(registry, new DiceService(new SeededRandomSource(1)));

            Assert.IsFalse(service.CanUse(caster.Id, ability));
        }

        [Test]
        public void IsValidTargetSelectionRespectsMinAndMax()
        {
            var resource = TestDefinitionFactory.CreateResourceDefinition("resource-action", "Action");
            var ability = CreateAbility(resource, 0, 1, 2);
            var service = new AbilityResolutionService(new CharacterRegistry(), new DiceService(new SeededRandomSource(1)));

            Assert.IsFalse(service.IsValidTargetSelection(ability, Array.Empty<RuntimeEntityId>()));
            Assert.IsTrue(service.IsValidTargetSelection(ability, new[] { RuntimeEntityId.NewId() }));
            Assert.IsTrue(service.IsValidTargetSelection(ability, new[] { RuntimeEntityId.NewId(), RuntimeEntityId.NewId() }));
            Assert.IsFalse(service.IsValidTargetSelection(ability, new[] { RuntimeEntityId.NewId(), RuntimeEntityId.NewId(), RuntimeEntityId.NewId() }));
        }

        [Test]
        public void ExecuteConsumesResourcesAppliesEffectsAndPublishesEvents()
        {
            var actionResource = TestDefinitionFactory.CreateResourceDefinition("resource-action", "Action");
            var hpResource = TestDefinitionFactory.CreateResourceDefinition("resource-hp", "Hit Points");
            var feature = TestDefinitionFactory.CreateFeatureDefinition("feature-marked", "Marked");

            var registry = new CharacterRegistry();
            var caster = new CharacterState(RuntimeEntityId.NewId());
            caster.AddResource(new ResourceState(actionResource.Id, max: 1));
            registry.Register(caster);

            var target = new CharacterState(RuntimeEntityId.NewId());
            target.AddResource(new ResourceState(hpResource.Id, max: 10));
            registry.Register(target);

            var grantFeature = new GrantFeatureEffect();
            TestDefinitionFactory.SetPrivateField(grantFeature, "feature", feature);
            TestDefinitionFactory.SetPrivateField(grantFeature, "applyToSelf", false);

            var ability = CreateAbility(actionResource, 1, 1, 1, grantFeature);

            var events = new List<GameEvent>();
            var eventBus = new GameEventBus();
            eventBus.Subscribe(GameEvent.AbilityStarted, ctx => events.Add(ctx.Event));
            eventBus.Subscribe(GameEvent.AbilityResolved, ctx => events.Add(ctx.Event));

            var service = new AbilityResolutionService(registry, new DiceService(new SeededRandomSource(1)), eventBus: eventBus);

            service.Execute(caster.Id, ability, new[] { target.Id });

            caster.TryGetResource(actionResource.Id, out var actionState);
            Assert.AreEqual(0, actionState.Current);
            Assert.IsTrue(target.HasFeature(feature.Id));
            CollectionAssert.AreEqual(new[] { GameEvent.AbilityStarted, GameEvent.AbilityResolved }, events);
        }

        [Test]
        public void CanUseIsFalseWhenAConditionPreventsActions()
        {
            var resource = TestDefinitionFactory.CreateResourceDefinition("resource-action", "Action");
            var rulesDatabase = new RulesDatabase();
            var stunned = TestDefinitionFactory.CreateConditionDefinition("condition-stunned", "Stunned");
            TestDefinitionFactory.SetPrivateField(stunned, "preventsActions", true);
            rulesDatabase.Register(stunned);

            var registry = new CharacterRegistry();
            var caster = new CharacterState(RuntimeEntityId.NewId());
            caster.AddResource(new ResourceState(resource.Id, max: 1));
            caster.ApplyCondition(new ConditionInstance(stunned.Id, RuntimeEntityId.None, caster.Id));
            registry.Register(caster);

            var ability = CreateAbility(resource, 1, 1, 1);
            var service = new AbilityResolutionService(registry, new DiceService(new SeededRandomSource(1)), rulesDatabase: rulesDatabase);

            Assert.IsFalse(service.CanUse(caster.Id, ability));
        }

        [Test]
        public void CanUseIgnoresConditionsThatDoNotPreventActions()
        {
            var resource = TestDefinitionFactory.CreateResourceDefinition("resource-action", "Action");
            var rulesDatabase = new RulesDatabase();
            var prone = TestDefinitionFactory.CreateConditionDefinition("condition-prone", "Prone"); // preventsActions left false

            var registry = new CharacterRegistry();
            var caster = new CharacterState(RuntimeEntityId.NewId());
            caster.AddResource(new ResourceState(resource.Id, max: 1));
            caster.ApplyCondition(new ConditionInstance(prone.Id, RuntimeEntityId.None, caster.Id));
            registry.Register(caster);
            rulesDatabase.Register(prone);

            var ability = CreateAbility(resource, 1, 1, 1);
            var service = new AbilityResolutionService(registry, new DiceService(new SeededRandomSource(1)), rulesDatabase: rulesDatabase);

            Assert.IsTrue(service.CanUse(caster.Id, ability));
        }

        [Test]
        public void RitualCastSkipsResourceCostsAndRequiresTheRitualTag()
        {
            var resource = TestDefinitionFactory.CreateResourceDefinition("resource-spell-slot-1", "Spell Slot Level 1");
            var registry = new CharacterRegistry();
            var caster = new CharacterState(RuntimeEntityId.NewId());
            caster.AddResource(new ResourceState(resource.Id, max: 0)); // no slots at all -- a normal cast would fail CanUse
            registry.Register(caster);

            var ability = CreateAbility(resource, 1, 1, 1);
            TestDefinitionFactory.SetPrivateField(ability, "tags", AbilityTag.Ritual);
            var service = new AbilityResolutionService(registry, new DiceService(new SeededRandomSource(1)));

            Assert.IsFalse(service.CanUse(caster.Id, ability)); // no slots -- can't cast normally
            Assert.IsTrue(service.CanUseAsRitual(caster.Id, ability)); // but can as a ritual
            Assert.DoesNotThrow(() => service.Execute(caster.Id, ability, new[] { RuntimeEntityId.NewId() }, asRitual: true));

            caster.TryGetResource(resource.Id, out var slot);
            Assert.AreEqual(0, slot.Current); // untouched -- ritual casting spends no resources
        }

        [Test]
        public void ExecuteThrowsWhenCastingAsRitualWithoutTheRitualTag()
        {
            var resource = TestDefinitionFactory.CreateResourceDefinition("resource-spell-slot-1", "Spell Slot Level 1");
            var registry = new CharacterRegistry();
            var caster = new CharacterState(RuntimeEntityId.NewId());
            caster.AddResource(new ResourceState(resource.Id, max: 1));
            registry.Register(caster);

            var ability = CreateAbility(resource, 1, 1, 1); // no Ritual tag
            var service = new AbilityResolutionService(registry, new DiceService(new SeededRandomSource(1)));

            Assert.Throws<InvalidOperationException>(() => service.Execute(caster.Id, ability, new[] { RuntimeEntityId.NewId() }, asRitual: true));
        }

        [Test]
        public void ExecutingAConcentrationAbilitySetsConcentratingOnAbilityId()
        {
            var resource = TestDefinitionFactory.CreateResourceDefinition("resource-action", "Action");
            var registry = new CharacterRegistry();
            var caster = new CharacterState(RuntimeEntityId.NewId());
            caster.AddResource(new ResourceState(resource.Id, max: 1));
            registry.Register(caster);

            var ability = CreateAbility(resource, 1, 1, 1);
            TestDefinitionFactory.SetPrivateField(ability, "tags", AbilityTag.Concentration);
            var service = new AbilityResolutionService(registry, new DiceService(new SeededRandomSource(1)));

            service.Execute(caster.Id, ability, new[] { RuntimeEntityId.NewId() });

            Assert.AreEqual(ability.Id, caster.ConcentratingOnAbilityId);
        }

        [Test]
        public void CastingANewConcentrationAbilityReplacesTheOldOne()
        {
            var resource = TestDefinitionFactory.CreateResourceDefinition("resource-action", "Action", RestTrigger.None);
            var registry = new CharacterRegistry();
            var caster = new CharacterState(RuntimeEntityId.NewId()) { ConcentratingOnAbilityId = new DefinitionId("spell-old") };
            caster.AddResource(new ResourceState(resource.Id, max: 2));
            registry.Register(caster);

            var ability = CreateAbility(resource, 1, 1, 1);
            TestDefinitionFactory.SetPrivateField(ability, "id", "spell-new");
            TestDefinitionFactory.SetPrivateField(ability, "tags", AbilityTag.Concentration);

            var events = new List<GameEvent>();
            var eventBus = new GameEventBus();
            eventBus.Subscribe(GameEvent.ConcentrationBroken, ctx => events.Add(ctx.Event));
            eventBus.Subscribe(GameEvent.ConcentrationStarted, ctx => events.Add(ctx.Event));
            var service = new AbilityResolutionService(registry, new DiceService(new SeededRandomSource(1)), eventBus: eventBus);

            service.Execute(caster.Id, ability, new[] { RuntimeEntityId.NewId() });

            Assert.AreEqual(new DefinitionId("spell-new"), caster.ConcentratingOnAbilityId);
            CollectionAssert.AreEqual(new[] { GameEvent.ConcentrationBroken, GameEvent.ConcentrationStarted }, events);
        }

        [Test]
        public void UpcastingSpendsTheChosenSlotInsteadOfTheAbilitysOwnCost()
        {
            var baseSlot = TestDefinitionFactory.CreateResourceDefinition("resource-spell-slot-1", "Spell Slot Level 1");
            var higherSlot = TestDefinitionFactory.CreateResourceDefinition("resource-spell-slot-3", "Spell Slot Level 3");
            TestDefinitionFactory.SetPrivateField(higherSlot, "spellSlotLevel", 3);
            TestDefinitionFactory.SetPrivateField(baseSlot, "spellSlotLevel", 1);

            var registry = new CharacterRegistry();
            var caster = new CharacterState(RuntimeEntityId.NewId());
            caster.AddResource(new ResourceState(baseSlot.Id, max: 1)); // has a 1st-level slot but won't be spent
            caster.AddResource(new ResourceState(higherSlot.Id, max: 1));
            registry.Register(caster);

            var ability = CreateAbility(baseSlot, 1, 1, 1); // authored cost names the base 1st-level slot
            TestDefinitionFactory.SetPrivateField(ability, "spellLevel", 1);
            var service = new AbilityResolutionService(registry, new DiceService(new SeededRandomSource(1)));

            Assert.IsTrue(service.CanUseAtSlotLevel(caster.Id, ability, higherSlot));
            service.Execute(caster.Id, ability, new[] { RuntimeEntityId.NewId() }, upcastWithSlot: higherSlot);

            caster.TryGetResource(baseSlot.Id, out var baseState);
            caster.TryGetResource(higherSlot.Id, out var higherState);
            Assert.AreEqual(1, baseState.Current); // untouched
            Assert.AreEqual(0, higherState.Current); // spent instead
        }

        [Test]
        public void CanUseAtSlotLevelIsFalseWhenTheSlotIsBelowTheSpellsOwnLevel()
        {
            var lowSlot = TestDefinitionFactory.CreateResourceDefinition("resource-spell-slot-1", "Spell Slot Level 1");
            TestDefinitionFactory.SetPrivateField(lowSlot, "spellSlotLevel", 1);

            var registry = new CharacterRegistry();
            var caster = new CharacterState(RuntimeEntityId.NewId());
            caster.AddResource(new ResourceState(lowSlot.Id, max: 1));
            registry.Register(caster);

            var ability = CreateAbility(lowSlot, 1, 1, 1);
            TestDefinitionFactory.SetPrivateField(ability, "spellLevel", 2); // a 2nd-level spell
            var service = new AbilityResolutionService(registry, new DiceService(new SeededRandomSource(1)));

            Assert.IsFalse(service.CanUseAtSlotLevel(caster.Id, ability, lowSlot));
        }

        [Test]
        public void ExecuteThrowsWhenTargetCountIsInvalid()
        {
            var resource = TestDefinitionFactory.CreateResourceDefinition("resource-action", "Action");
            var registry = new CharacterRegistry();
            var caster = new CharacterState(RuntimeEntityId.NewId());
            caster.AddResource(new ResourceState(resource.Id, max: 1));
            registry.Register(caster);

            var ability = CreateAbility(resource, 1, 1, 1);
            var service = new AbilityResolutionService(registry, new DiceService(new SeededRandomSource(1)));

            Assert.Throws<InvalidOperationException>(() => service.Execute(caster.Id, ability, Array.Empty<RuntimeEntityId>()));
        }
    }
}
