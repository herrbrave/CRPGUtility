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

        // --- Task 0.2 (Production Foundation): the next two tests are characterization tests, not red
        // regressions -- they document TODAY's actual (intentionally-to-be-changed) behavior for
        // Docs/Design/Production-Foundation.md sec.6 / Task 4.2's unified validation and cost-plan work.
        // Both currently PASS. When 4.2 lands, each assertion is expected to flip (see per-test comments);
        // update the assertion and rename the test then -- do not delete it silently, since it's the
        // regression baseline proving 4.2 actually changed this behavior on purpose.
        [Test]
        public void IsValidTargetSelectionAcceptsDuplicateTargetIds()
        {
            var resource = TestDefinitionFactory.CreateResourceDefinition("resource-action", "Action");
            var ability = CreateAbility(resource, 0, 1, 2);
            var service = new AbilityResolutionService(new CharacterRegistry(), new DiceService(new SeededRandomSource(1)));

            var sameTargetTwice = RuntimeEntityId.NewId();

            // Today IsValidTargetSelection only checks Count against Min/Max -- it never checks distinctness,
            // so the same target listed twice passes as long as the count itself is in range. Task 4.2 is
            // expected to reject this (Assert.IsFalse) once duplicate-target validation exists.
            Assert.IsTrue(service.IsValidTargetSelection(ability, new[] { sameTargetTwice, sameTargetTwice }));
        }

        [Test]
        public void UpcastingBypassesTheAbilitysOtherAuthoredCosts()
        {
            var baseSlot = TestDefinitionFactory.CreateResourceDefinition("resource-spell-slot-1", "Spell Slot Level 1");
            var higherSlot = TestDefinitionFactory.CreateResourceDefinition("resource-spell-slot-3", "Spell Slot Level 3");
            TestDefinitionFactory.SetPrivateField(higherSlot, "spellSlotLevel", 3);
            TestDefinitionFactory.SetPrivateField(baseSlot, "spellSlotLevel", 1);
            var materialResource = TestDefinitionFactory.CreateResourceDefinition("resource-material-ruby-dust", "Ruby Dust");

            var registry = new CharacterRegistry();
            var caster = new CharacterState(RuntimeEntityId.NewId());
            caster.AddResource(new ResourceState(baseSlot.Id, max: 1));
            caster.AddResource(new ResourceState(higherSlot.Id, max: 1));
            caster.AddResource(new ResourceState(materialResource.Id, max: 1)); // this spell's one material component
            registry.Register(caster);

            // Built by hand (not via the single-cost CreateAbility helper) since this ability needs two
            // authored Costs entries: the spell-slot cost every other upcast test already exercises, plus a
            // second, unrelated material-component cost that upcasting should still charge.
            var ability = ScriptableObject.CreateInstance<AbilityDefinition>();
            TestDefinitionFactory.SetPrivateField(ability, "id", "ability-test-material-spell");
            TestDefinitionFactory.SetPrivateField(ability, "displayName", "Test Material Spell");
            TestDefinitionFactory.SetPrivateField(ability, "spellLevel", 1);

            object slotCost = new AbilityCost();
            TestDefinitionFactory.SetPrivateField(slotCost, "resource", baseSlot);
            TestDefinitionFactory.SetPrivateField(slotCost, "amount", 1);
            object materialCost = new AbilityCost();
            TestDefinitionFactory.SetPrivateField(materialCost, "resource", materialResource);
            TestDefinitionFactory.SetPrivateField(materialCost, "amount", 1);
            TestDefinitionFactory.SetPrivateField(ability, "costs", new List<AbilityCost> { (AbilityCost)slotCost, (AbilityCost)materialCost });

            var targeting = new TargetingDefinition();
            TestDefinitionFactory.SetPrivateField(targeting, "minTargets", 1);
            TestDefinitionFactory.SetPrivateField(targeting, "maxTargets", 1);
            TestDefinitionFactory.SetPrivateField(ability, "targeting", targeting);
            TestDefinitionFactory.SetPrivateField(ability, "effects", new List<AbilityEffect>());

            var service = new AbilityResolutionService(registry, new DiceService(new SeededRandomSource(1)));

            service.Execute(caster.Id, ability, new[] { RuntimeEntityId.NewId() }, upcastWithSlot: higherSlot);

            caster.TryGetResource(higherSlot.Id, out var higherState);
            caster.TryGetResource(materialResource.Id, out var materialState);
            Assert.AreEqual(0, higherState.Current); // the chosen slot is spent, as UpcastingSpendsTheChosenSlot... already proves

            // Today Execute's upcast branch spends *only* the chosen slot and returns without ever touching
            // ability.Costs -- so the material component is NOT spent, even though it's a real authored cost
            // unrelated to which slot the spell was cast from. Task 4.2's AbilityCostPlan is expected to
            // change this so alternate casting modes "transform a cost plan" rather than skip unrelated costs
            // outright (Production-Foundation.md sec.6) -- once fixed, this should read Assert.AreEqual(0, ...).
            Assert.AreEqual(1, materialState.Current, "Documents today's gap: upcasting currently skips unrelated authored costs entirely.");
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
