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

        /// <summary>Every Validate/Execute call now needs real, resolvable targets (the new InvalidTarget
        /// check) -- this registers one and returns its id, for tests that don't care about the target's own
        /// state, only that it's a real character.</summary>
        private static RuntimeEntityId RegisterTarget(CharacterRegistry registry)
        {
            var target = new CharacterState(RuntimeEntityId.NewId());
            registry.Register(target);
            return target.Id;
        }

        [Test]
        public void ValidateRejectsWhenResourceIsInsufficient()
        {
            var resource = TestDefinitionFactory.CreateResourceDefinition("resource-action", "Action");
            var registry = new CharacterRegistry();
            var caster = new CharacterState(RuntimeEntityId.NewId());
            caster.AddResource(new ResourceState(resource.Id, max: 1, current: 0));
            registry.Register(caster);
            var target = RegisterTarget(registry);

            var ability = CreateAbility(resource, 1, 1, 1);
            var service = new AbilityResolutionService(registry, new DiceService(new SeededRandomSource(1)));

            var result = service.Validate(caster.Id, ability, new[] { target });

            Assert.IsFalse(result.IsValid);
            Assert.AreEqual(AbilityRejectionReason.InsufficientResources, result.Reason);
        }

        [Test]
        public void ValidateSucceedsWhenResourceIsAvailable()
        {
            var resource = TestDefinitionFactory.CreateResourceDefinition("resource-action", "Action");
            var registry = new CharacterRegistry();
            var caster = new CharacterState(RuntimeEntityId.NewId());
            caster.AddResource(new ResourceState(resource.Id, max: 1));
            registry.Register(caster);
            var target = RegisterTarget(registry);

            var ability = CreateAbility(resource, 1, 1, 1);
            var service = new AbilityResolutionService(registry, new DiceService(new SeededRandomSource(1)));

            Assert.IsTrue(service.Validate(caster.Id, ability, new[] { target }).IsValid);
        }

        [Test]
        public void ValidateRejectsWhenCharacterIsMakingDeathSaves()
        {
            var resource = TestDefinitionFactory.CreateResourceDefinition("resource-action", "Action");
            var registry = new CharacterRegistry();
            var caster = new CharacterState(RuntimeEntityId.NewId()) { DeathSaves = new DeathSaveState() };
            caster.AddResource(new ResourceState(resource.Id, max: 1)); // resource is available -- only DeathSaves should block this
            registry.Register(caster);
            var target = RegisterTarget(registry);

            var ability = CreateAbility(resource, 1, 1, 1);
            var service = new AbilityResolutionService(registry, new DiceService(new SeededRandomSource(1)));

            var result = service.Validate(caster.Id, ability, new[] { target });

            Assert.IsFalse(result.IsValid);
            Assert.AreEqual(AbilityRejectionReason.Incapacitated, result.Reason);
        }

        [Test]
        public void ValidateRespectsMinAndMaxTargetCount()
        {
            var resource = TestDefinitionFactory.CreateResourceDefinition("resource-action", "Action");
            var registry = new CharacterRegistry();
            var caster = new CharacterState(RuntimeEntityId.NewId());
            caster.AddResource(new ResourceState(resource.Id, max: 1));
            registry.Register(caster);
            var targetA = RegisterTarget(registry);
            var targetB = RegisterTarget(registry);
            var targetC = RegisterTarget(registry);

            var ability = CreateAbility(resource, 0, 1, 2);
            var service = new AbilityResolutionService(registry, new DiceService(new SeededRandomSource(1)));

            Assert.AreEqual(AbilityRejectionReason.InvalidTargetCount, service.Validate(caster.Id, ability, Array.Empty<RuntimeEntityId>()).Reason);
            Assert.IsTrue(service.Validate(caster.Id, ability, new[] { targetA }).IsValid);
            Assert.IsTrue(service.Validate(caster.Id, ability, new[] { targetA, targetB }).IsValid);
            Assert.AreEqual(AbilityRejectionReason.InvalidTargetCount, service.Validate(caster.Id, ability, new[] { targetA, targetB, targetC }).Reason);
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

            var result = service.Execute(caster.Id, ability, new[] { target.Id });

            Assert.IsTrue(result.Succeeded);
            caster.TryGetResource(actionResource.Id, out var actionState);
            Assert.AreEqual(0, actionState.Current);
            Assert.IsTrue(target.HasFeature(feature.Id));
            CollectionAssert.AreEqual(new[] { GameEvent.AbilityStarted, GameEvent.AbilityResolved }, events);
        }

        [Test]
        public void ValidateRejectsWhenAConditionPreventsActions()
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
            var target = RegisterTarget(registry);

            var ability = CreateAbility(resource, 1, 1, 1);
            var service = new AbilityResolutionService(registry, new DiceService(new SeededRandomSource(1)), rulesDatabase: rulesDatabase);

            Assert.AreEqual(AbilityRejectionReason.Incapacitated, service.Validate(caster.Id, ability, new[] { target }).Reason);
        }

        [Test]
        public void ValidateIgnoresConditionsThatDoNotPreventActions()
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
            var target = RegisterTarget(registry);

            var ability = CreateAbility(resource, 1, 1, 1);
            var service = new AbilityResolutionService(registry, new DiceService(new SeededRandomSource(1)), rulesDatabase: rulesDatabase);

            Assert.IsTrue(service.Validate(caster.Id, ability, new[] { target }).IsValid);
        }

        [Test]
        public void RitualCastSkipsResourceCostsAndRequiresTheRitualTag()
        {
            var resource = TestDefinitionFactory.CreateResourceDefinition("resource-spell-slot-1", "Spell Slot Level 1");
            TestDefinitionFactory.SetPrivateField(resource, "spellSlotLevel", 1);
            var registry = new CharacterRegistry();
            var caster = new CharacterState(RuntimeEntityId.NewId());
            caster.AddResource(new ResourceState(resource.Id, max: 0)); // no slots at all -- a normal cast would fail
            registry.Register(caster);
            var target = RegisterTarget(registry);

            var ability = CreateAbility(resource, 1, 1, 1);
            TestDefinitionFactory.SetPrivateField(ability, "tags", AbilityTag.Ritual);
            var service = new AbilityResolutionService(registry, new DiceService(new SeededRandomSource(1)));

            Assert.AreEqual(AbilityRejectionReason.InsufficientResources, service.Validate(caster.Id, ability, new[] { target }).Reason); // no slots -- can't cast normally
            Assert.IsTrue(service.Validate(caster.Id, ability, new[] { target }, asRitual: true).IsValid); // but can as a ritual

            var executeResult = service.Execute(caster.Id, ability, new[] { target }, asRitual: true);
            Assert.IsTrue(executeResult.Succeeded);

            caster.TryGetResource(resource.Id, out var slot);
            Assert.AreEqual(0, slot.Current); // untouched -- ritual casting spends no slot
        }

        [Test]
        public void RitualCastStillSpendsAnUnrelatedActionEconomyCost()
        {
            // Task 4.2: "alternate costs retaining action charges" -- a ritual cast skips only the slot-shaped
            // cost, not every authored cost.
            var slot = TestDefinitionFactory.CreateResourceDefinition("resource-spell-slot-1", "Spell Slot Level 1");
            TestDefinitionFactory.SetPrivateField(slot, "spellSlotLevel", 1);
            var actionResource = TestDefinitionFactory.CreateResourceDefinition("resource-action", "Action");

            var registry = new CharacterRegistry();
            var caster = new CharacterState(RuntimeEntityId.NewId());
            caster.AddResource(new ResourceState(slot.Id, max: 0));
            caster.AddResource(new ResourceState(actionResource.Id, max: 1));
            registry.Register(caster);
            var target = RegisterTarget(registry);

            var ability = ScriptableObject.CreateInstance<AbilityDefinition>();
            TestDefinitionFactory.SetPrivateField(ability, "id", "ability-ritual-with-action-cost");
            TestDefinitionFactory.SetPrivateField(ability, "tags", AbilityTag.Ritual);
            object slotCost = new AbilityCost();
            TestDefinitionFactory.SetPrivateField(slotCost, "resource", slot);
            TestDefinitionFactory.SetPrivateField(slotCost, "amount", 1);
            object actionCost = new AbilityCost();
            TestDefinitionFactory.SetPrivateField(actionCost, "resource", actionResource);
            TestDefinitionFactory.SetPrivateField(actionCost, "amount", 1);
            TestDefinitionFactory.SetPrivateField(ability, "costs", new List<AbilityCost> { (AbilityCost)slotCost, (AbilityCost)actionCost });
            var targeting = new TargetingDefinition();
            TestDefinitionFactory.SetPrivateField(targeting, "minTargets", 1);
            TestDefinitionFactory.SetPrivateField(targeting, "maxTargets", 1);
            TestDefinitionFactory.SetPrivateField(ability, "targeting", targeting);
            TestDefinitionFactory.SetPrivateField(ability, "effects", new List<AbilityEffect>());

            var service = new AbilityResolutionService(registry, new DiceService(new SeededRandomSource(1)));
            var result = service.Execute(caster.Id, ability, new[] { target }, asRitual: true);

            Assert.IsTrue(result.Succeeded);
            caster.TryGetResource(slot.Id, out var slotState);
            caster.TryGetResource(actionResource.Id, out var actionState);
            Assert.AreEqual(0, slotState.Current); // untouched -- no slot spent
            Assert.AreEqual(0, actionState.Current); // spent -- the action cost is unrelated to the slot
        }

        [Test]
        public void ExecuteRejectsCastingAsRitualWithoutTheRitualTag()
        {
            var resource = TestDefinitionFactory.CreateResourceDefinition("resource-spell-slot-1", "Spell Slot Level 1");
            var registry = new CharacterRegistry();
            var caster = new CharacterState(RuntimeEntityId.NewId());
            caster.AddResource(new ResourceState(resource.Id, max: 1));
            registry.Register(caster);
            var target = RegisterTarget(registry);

            var ability = CreateAbility(resource, 1, 1, 1); // no Ritual tag
            var service = new AbilityResolutionService(registry, new DiceService(new SeededRandomSource(1)));

            var result = service.Execute(caster.Id, ability, new[] { target }, asRitual: true);

            Assert.AreEqual(AbilityExecutionOutcome.Rejected, result.Outcome);
            Assert.AreEqual(AbilityRejectionReason.RitualTagRequired, result.RejectionReason);
        }

        [Test]
        public void ExecutingAConcentrationAbilitySetsConcentratingOnAbilityId()
        {
            var resource = TestDefinitionFactory.CreateResourceDefinition("resource-action", "Action");
            var registry = new CharacterRegistry();
            var caster = new CharacterState(RuntimeEntityId.NewId());
            caster.AddResource(new ResourceState(resource.Id, max: 1));
            registry.Register(caster);
            var target = RegisterTarget(registry);

            var ability = CreateAbility(resource, 1, 1, 1);
            TestDefinitionFactory.SetPrivateField(ability, "tags", AbilityTag.Concentration);
            var service = new AbilityResolutionService(registry, new DiceService(new SeededRandomSource(1)));

            service.Execute(caster.Id, ability, new[] { target });

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
            var target = RegisterTarget(registry);

            var ability = CreateAbility(resource, 1, 1, 1);
            TestDefinitionFactory.SetPrivateField(ability, "id", "spell-new");
            TestDefinitionFactory.SetPrivateField(ability, "tags", AbilityTag.Concentration);

            var events = new List<GameEvent>();
            var eventBus = new GameEventBus();
            eventBus.Subscribe(GameEvent.ConcentrationBroken, ctx => events.Add(ctx.Event));
            eventBus.Subscribe(GameEvent.ConcentrationStarted, ctx => events.Add(ctx.Event));
            var service = new AbilityResolutionService(registry, new DiceService(new SeededRandomSource(1)), eventBus: eventBus);

            service.Execute(caster.Id, ability, new[] { target });

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
            var target = RegisterTarget(registry);

            var ability = CreateAbility(baseSlot, 1, 1, 1); // authored cost names the base 1st-level slot
            TestDefinitionFactory.SetPrivateField(ability, "spellLevel", 1);
            var service = new AbilityResolutionService(registry, new DiceService(new SeededRandomSource(1)));

            Assert.IsTrue(service.Validate(caster.Id, ability, new[] { target }, upcastWithSlot: higherSlot).IsValid);
            service.Execute(caster.Id, ability, new[] { target }, upcastWithSlot: higherSlot);

            caster.TryGetResource(baseSlot.Id, out var baseState);
            caster.TryGetResource(higherSlot.Id, out var higherState);
            Assert.AreEqual(1, baseState.Current); // untouched
            Assert.AreEqual(0, higherState.Current); // spent instead
        }

        [Test]
        public void ValidateRejectsUpcastSlotBelowTheSpellsOwnLevel()
        {
            var lowSlot = TestDefinitionFactory.CreateResourceDefinition("resource-spell-slot-1", "Spell Slot Level 1");
            TestDefinitionFactory.SetPrivateField(lowSlot, "spellSlotLevel", 1);

            var registry = new CharacterRegistry();
            var caster = new CharacterState(RuntimeEntityId.NewId());
            caster.AddResource(new ResourceState(lowSlot.Id, max: 1));
            registry.Register(caster);
            var target = RegisterTarget(registry);

            var ability = CreateAbility(lowSlot, 1, 1, 1);
            TestDefinitionFactory.SetPrivateField(ability, "spellLevel", 2); // a 2nd-level spell
            var service = new AbilityResolutionService(registry, new DiceService(new SeededRandomSource(1)));

            Assert.AreEqual(AbilityRejectionReason.UpcastSlotBelowSpellLevel, service.Validate(caster.Id, ability, new[] { target }, upcastWithSlot: lowSlot).Reason);
        }

        // --- Task 4.2: these two used to be characterization tests documenting intentional pre-4.2 gaps.
        // Both now assert the fixed behavior -- kept in place (not deleted) as the regression proof that 4.2
        // actually changed this on purpose.
        [Test]
        public void ValidateRejectsDuplicateTargetIds()
        {
            var resource = TestDefinitionFactory.CreateResourceDefinition("resource-action", "Action");
            var ability = CreateAbility(resource, 0, 1, 2);
            var registry = new CharacterRegistry();
            var caster = new CharacterState(RuntimeEntityId.NewId());
            registry.Register(caster);
            var service = new AbilityResolutionService(registry, new DiceService(new SeededRandomSource(1)));

            var sameTargetTwice = RuntimeEntityId.NewId();

            var result = service.Validate(caster.Id, ability, new[] { sameTargetTwice, sameTargetTwice });

            Assert.IsFalse(result.IsValid);
            Assert.AreEqual(AbilityRejectionReason.DuplicateTarget, result.Reason);
        }

        [Test]
        public void UpcastingNowChargesTheAbilitysOtherAuthoredCostsToo()
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
            var target = RegisterTarget(registry);

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

            var result = service.Execute(caster.Id, ability, new[] { target }, upcastWithSlot: higherSlot);

            Assert.IsTrue(result.Succeeded);
            caster.TryGetResource(higherSlot.Id, out var higherState);
            caster.TryGetResource(materialResource.Id, out var materialState);
            Assert.AreEqual(0, higherState.Current); // the chosen slot is spent
            Assert.AreEqual(0, materialState.Current); // Task 4.2 fix: the unrelated material cost is now also charged
        }

        [Test]
        public void ExecuteRejectsWhenTargetCountIsInvalid()
        {
            var resource = TestDefinitionFactory.CreateResourceDefinition("resource-action", "Action");
            var registry = new CharacterRegistry();
            var caster = new CharacterState(RuntimeEntityId.NewId());
            caster.AddResource(new ResourceState(resource.Id, max: 1));
            registry.Register(caster);

            var ability = CreateAbility(resource, 1, 1, 1);
            var service = new AbilityResolutionService(registry, new DiceService(new SeededRandomSource(1)));

            var result = service.Execute(caster.Id, ability, Array.Empty<RuntimeEntityId>());

            Assert.AreEqual(AbilityExecutionOutcome.Rejected, result.Outcome);
            Assert.AreEqual(AbilityRejectionReason.InvalidTargetCount, result.RejectionReason);
        }

        // --- Task 4.2: grouped duplicate costs ---
        [Test]
        public void ValidateRejectsGroupedDuplicateCostsThatExceedTheCombinedAvailableAmount()
        {
            var resource = TestDefinitionFactory.CreateResourceDefinition("resource-mana", "Mana");
            var registry = new CharacterRegistry();
            var caster = new CharacterState(RuntimeEntityId.NewId());
            caster.AddResource(new ResourceState(resource.Id, max: 3, current: 3)); // 3 available
            registry.Register(caster);
            var target = RegisterTarget(registry);

            var ability = ScriptableObject.CreateInstance<AbilityDefinition>();
            TestDefinitionFactory.SetPrivateField(ability, "id", "ability-grouped-cost");
            object costA = new AbilityCost();
            TestDefinitionFactory.SetPrivateField(costA, "resource", resource);
            TestDefinitionFactory.SetPrivateField(costA, "amount", 2);
            object costB = new AbilityCost();
            TestDefinitionFactory.SetPrivateField(costB, "resource", resource);
            TestDefinitionFactory.SetPrivateField(costB, "amount", 2);
            TestDefinitionFactory.SetPrivateField(ability, "costs", new List<AbilityCost> { (AbilityCost)costA, (AbilityCost)costB }); // 2+2=4 combined, only 3 available
            var targeting = new TargetingDefinition();
            TestDefinitionFactory.SetPrivateField(targeting, "minTargets", 1);
            TestDefinitionFactory.SetPrivateField(targeting, "maxTargets", 1);
            TestDefinitionFactory.SetPrivateField(ability, "targeting", targeting);
            TestDefinitionFactory.SetPrivateField(ability, "effects", new List<AbilityEffect>());

            var service = new AbilityResolutionService(registry, new DiceService(new SeededRandomSource(1)));

            var result = service.Validate(caster.Id, ability, new[] { target });

            Assert.IsFalse(result.IsValid);
            Assert.AreEqual(AbilityRejectionReason.InsufficientResources, result.Reason);
            caster.TryGetResource(resource.Id, out var resourceState);
            Assert.AreEqual(3, resourceState.Current); // Validate never mutates
        }

        // --- Task 4.2: unavailable grants (known/prepared ability) ---
        [Test]
        public void ValidateRejectsAnAbilityTheCasterHasNotLearnedWhenItKnowsOthers()
        {
            var resource = TestDefinitionFactory.CreateResourceDefinition("resource-action", "Action");
            var registry = new CharacterRegistry();
            var caster = new CharacterState(RuntimeEntityId.NewId());
            caster.AddResource(new ResourceState(resource.Id, max: 1));
            caster.LearnAbility(new DefinitionId("some-other-ability"));
            registry.Register(caster);
            var target = RegisterTarget(registry);

            var ability = CreateAbility(resource, 1, 1, 1); // id "ability-test" -- never learned
            var service = new AbilityResolutionService(registry, new DiceService(new SeededRandomSource(1)));

            Assert.AreEqual(AbilityRejectionReason.AbilityNotKnown, service.Validate(caster.Id, ability, new[] { target }).Reason);
        }

        [Test]
        public void ValidateDoesNotGateOnKnownAbilitiesWhenTheCasterHasNeverLearnedAnything()
        {
            var resource = TestDefinitionFactory.CreateResourceDefinition("resource-action", "Action");
            var registry = new CharacterRegistry();
            var caster = new CharacterState(RuntimeEntityId.NewId());
            caster.AddResource(new ResourceState(resource.Id, max: 1)); // KnownAbilities left empty -- legacy/uninitialized mode
            registry.Register(caster);
            var target = RegisterTarget(registry);

            var ability = CreateAbility(resource, 1, 1, 1);
            var service = new AbilityResolutionService(registry, new DiceService(new SeededRandomSource(1)));

            Assert.IsTrue(service.Validate(caster.Id, ability, new[] { target }).IsValid);
        }

        // --- Task 4.2: reject missing required effect services before charging ---
        [Test]
        public void ValidateRejectsAnAttackRollEffectWhenAttackBonusRuleIsNotWiredUp()
        {
            var resource = TestDefinitionFactory.CreateResourceDefinition("resource-action", "Action");
            var registry = new CharacterRegistry();
            var caster = new CharacterState(RuntimeEntityId.NewId());
            caster.AddResource(new ResourceState(resource.Id, max: 1));
            registry.Register(caster);
            var target = RegisterTarget(registry);

            var ability = CreateAbility(resource, 1, 1, 1, new AttackRollEffect());
            // No attackBonusRule/armorClassRule wired -- this ability's only effect can never do anything.
            var service = new AbilityResolutionService(registry, new DiceService(new SeededRandomSource(1)));

            var result = service.Validate(caster.Id, ability, new[] { target });

            Assert.IsFalse(result.IsValid);
            Assert.AreEqual(AbilityRejectionReason.MissingRequiredEffectService, result.Reason);
        }

        [Test]
        public void ExecuteDoesNotChargeWhenARequiredEffectServiceIsMissing()
        {
            var resource = TestDefinitionFactory.CreateResourceDefinition("resource-action", "Action");
            var registry = new CharacterRegistry();
            var caster = new CharacterState(RuntimeEntityId.NewId());
            caster.AddResource(new ResourceState(resource.Id, max: 1));
            registry.Register(caster);
            var target = RegisterTarget(registry);

            var ability = CreateAbility(resource, 1, 1, 1, new AttackRollEffect());
            var service = new AbilityResolutionService(registry, new DiceService(new SeededRandomSource(1)));

            var result = service.Execute(caster.Id, ability, new[] { target });

            Assert.AreEqual(AbilityExecutionOutcome.Rejected, result.Outcome);
            Assert.AreEqual(AbilityRejectionReason.MissingRequiredEffectService, result.RejectionReason);
            caster.TryGetResource(resource.Id, out var resourceState);
            Assert.AreEqual(1, resourceState.Current); // untouched -- this is the bug the fix closes
        }

        // --- Task 4.2: validate-again-at-commit / spend-exactly-once ---
        [Test]
        public void ExecuteRejectsWhenStateBecameInvalidAfterAnEarlierValidateCall()
        {
            var resource = TestDefinitionFactory.CreateResourceDefinition("resource-action", "Action");
            var registry = new CharacterRegistry();
            var caster = new CharacterState(RuntimeEntityId.NewId());
            caster.AddResource(new ResourceState(resource.Id, max: 1, current: 1));
            registry.Register(caster);
            var target = RegisterTarget(registry);

            var ability = CreateAbility(resource, 1, 1, 1);
            var service = new AbilityResolutionService(registry, new DiceService(new SeededRandomSource(1)));

            Assert.IsTrue(service.Validate(caster.Id, ability, new[] { target }).IsValid); // valid at this point

            caster.TryGetResource(resource.Id, out var resourceState);
            resourceState.Spend(1); // simulate the resource being spent by something else in between

            var result = service.Execute(caster.Id, ability, new[] { target }); // re-validates internally

            Assert.AreEqual(AbilityExecutionOutcome.Rejected, result.Outcome);
            Assert.AreEqual(AbilityRejectionReason.InsufficientResources, result.RejectionReason);
            Assert.AreEqual(0, resourceState.Current); // still exactly 0 from the simulated spend -- Execute spent nothing further
        }

        // --- Task 4.2: faulted action ---
        private sealed class ThrowingEffect : AbilityEffect
        {
            public override void Apply(AbilityExecutionContext context) => throw new InvalidOperationException("Deliberate test fault.");
        }

        [Test]
        public void ExecuteReturnsFaultedAndPublishesAbilityFaultedWhenAnEffectThrows()
        {
            var resource = TestDefinitionFactory.CreateResourceDefinition("resource-action", "Action");
            var registry = new CharacterRegistry();
            var caster = new CharacterState(RuntimeEntityId.NewId());
            caster.AddResource(new ResourceState(resource.Id, max: 1));
            registry.Register(caster);
            var target = RegisterTarget(registry);

            var ability = CreateAbility(resource, 1, 1, 1, new ThrowingEffect());

            var events = new List<GameEvent>();
            var eventBus = new GameEventBus();
            eventBus.Subscribe(GameEvent.AbilityStarted, ctx => events.Add(ctx.Event));
            eventBus.Subscribe(GameEvent.AbilityFaulted, ctx => events.Add(ctx.Event));
            eventBus.Subscribe(GameEvent.AbilityResolved, ctx => events.Add(ctx.Event));
            var service = new AbilityResolutionService(registry, new DiceService(new SeededRandomSource(1)), eventBus: eventBus);

            var result = service.Execute(caster.Id, ability, new[] { target });

            Assert.AreEqual(AbilityExecutionOutcome.Faulted, result.Outcome);
            Assert.IsFalse(string.IsNullOrEmpty(result.Message));
            CollectionAssert.AreEqual(new[] { GameEvent.AbilityStarted, GameEvent.AbilityFaulted }, events); // AbilityResolved never fires
            caster.TryGetResource(resource.Id, out var resourceState);
            Assert.AreEqual(0, resourceState.Current); // spent -- no rollback on a post-commit fault, by design
        }

        // --- Task 4.2: no mutation on ordinary rejection ---
        [TestCase(true)]
        [TestCase(false)]
        public void OrdinaryRejectionMutatesNothingWhetherFromValidateOrExecutesOwnRevalidation(bool viaExecute)
        {
            var resource = TestDefinitionFactory.CreateResourceDefinition("resource-action", "Action");
            var registry = new CharacterRegistry();
            var caster = new CharacterState(RuntimeEntityId.NewId());
            caster.AddResource(new ResourceState(resource.Id, max: 1, current: 0)); // insufficient -- guaranteed rejection
            registry.Register(caster);
            var target = RegisterTarget(registry);

            var ability = CreateAbility(resource, 1, 1, 1);
            var events = new List<GameEvent>();
            var eventBus = new GameEventBus();
            foreach (var e in new[] { GameEvent.AbilityStarted, GameEvent.AbilityResolved, GameEvent.AbilityFaulted })
                eventBus.Subscribe(e, ctx => events.Add(ctx.Event));
            var service = new AbilityResolutionService(registry, new DiceService(new SeededRandomSource(1)), eventBus: eventBus);

            if (viaExecute) service.Execute(caster.Id, ability, new[] { target });
            else service.Validate(caster.Id, ability, new[] { target });

            caster.TryGetResource(resource.Id, out var resourceState);
            Assert.AreEqual(0, resourceState.Current);
            Assert.IsEmpty(events);
        }
    }
}
