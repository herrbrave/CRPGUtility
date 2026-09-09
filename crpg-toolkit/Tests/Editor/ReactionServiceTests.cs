using System.Collections.Generic;
using CRPG.Abilities;
using CRPG.Actors;
using CRPG.Combat;
using CRPG.Core;
using NUnit.Framework;
using UnityEngine;

namespace CRPG.Tests.EditMode
{
    public class ReactionServiceTests
    {
        private static AbilityDefinition CreateAbility(string id, AbilityTag tags, ResourceDefinition costResource, int costAmount)
        {
            var ability = ScriptableObject.CreateInstance<AbilityDefinition>();
            TestDefinitionFactory.SetPrivateField(ability, "id", id);
            TestDefinitionFactory.SetPrivateField(ability, "displayName", id);
            TestDefinitionFactory.SetPrivateField(ability, "tags", tags);

            object boxedCost = new AbilityCost();
            TestDefinitionFactory.SetPrivateField(boxedCost, "resource", costResource);
            TestDefinitionFactory.SetPrivateField(boxedCost, "amount", costAmount);
            TestDefinitionFactory.SetPrivateField(ability, "costs", new List<AbilityCost> { (AbilityCost)boxedCost });

            var targeting = new TargetingDefinition();
            TestDefinitionFactory.SetPrivateField(targeting, "minTargets", 1);
            TestDefinitionFactory.SetPrivateField(targeting, "maxTargets", 1);
            TestDefinitionFactory.SetPrivateField(ability, "targeting", targeting);

            TestDefinitionFactory.SetPrivateField(ability, "effects", new List<AbilityEffect>());
            return ability;
        }

        [Test]
        public void ResolveOpportunityFailsWhenReactorHasNoReactionLeft()
        {
            var reaction = TestDefinitionFactory.CreateResourceDefinition("resource-reaction", "Reaction");
            var registry = new CharacterRegistry();
            var reactor = new CharacterState(RuntimeEntityId.NewId());
            reactor.AddResource(new ResourceState(reaction.Id, max: 1, current: 0));
            registry.Register(reactor);

            var opportunistAttack = CreateAbility("ability-opportunist", AbilityTag.Reaction, reaction, 1);
            var resolution = new AbilityResolutionService(registry, new DiceService(new SeededRandomSource(1)));
            var service = new ReactionService(registry, resolution, new FirstEligibleReactionProvider(), reaction);

            var opportunity = new ReactionOpportunity(GameEvent.OnMiss, reactor.Id, RuntimeEntityId.NewId());
            var resolved = service.ResolveOpportunity(opportunity, new[] { opportunistAttack }, new[] { opportunity.Instigator });

            Assert.IsFalse(resolved);
        }

        [Test]
        public void ResolveOpportunityIgnoresNonReactionAbilities()
        {
            var reaction = TestDefinitionFactory.CreateResourceDefinition("resource-reaction", "Reaction");
            var registry = new CharacterRegistry();
            var reactor = new CharacterState(RuntimeEntityId.NewId());
            reactor.AddResource(new ResourceState(reaction.Id, max: 1));
            registry.Register(reactor);

            var notAReaction = CreateAbility("ability-fireball", AbilityTag.Spell, reaction, 1);
            var resolution = new AbilityResolutionService(registry, new DiceService(new SeededRandomSource(1)));
            var service = new ReactionService(registry, resolution, new FirstEligibleReactionProvider(), reaction);

            var opportunity = new ReactionOpportunity(GameEvent.OnMiss, reactor.Id, RuntimeEntityId.NewId());
            var resolved = service.ResolveOpportunity(opportunity, new[] { notAReaction }, new[] { opportunity.Instigator });

            Assert.IsFalse(resolved);
        }

        [Test]
        public void ResolveOpportunityDeclinesWhenProviderDeclines()
        {
            var reaction = TestDefinitionFactory.CreateResourceDefinition("resource-reaction", "Reaction");
            var registry = new CharacterRegistry();
            var reactor = new CharacterState(RuntimeEntityId.NewId());
            reactor.AddResource(new ResourceState(reaction.Id, max: 1));
            registry.Register(reactor);

            var opportunistAttack = CreateAbility("ability-opportunist", AbilityTag.Reaction, reaction, 1);
            var resolution = new AbilityResolutionService(registry, new DiceService(new SeededRandomSource(1)));
            var service = new ReactionService(registry, resolution, new AlwaysDeclineReactionProvider(), reaction);

            var opportunity = new ReactionOpportunity(GameEvent.OnMiss, reactor.Id, RuntimeEntityId.NewId());
            var resolved = service.ResolveOpportunity(opportunity, new[] { opportunistAttack }, new[] { opportunity.Instigator });

            Assert.IsFalse(resolved);
            reactor.TryGetResource(reaction.Id, out var state);
            Assert.AreEqual(1, state.Current); // untouched
        }

        [Test]
        public void ResolveOpportunityExecutesTheChosenReactionAndSpendsTheResource()
        {
            var reaction = TestDefinitionFactory.CreateResourceDefinition("resource-reaction", "Reaction");
            var registry = new CharacterRegistry();
            var reactor = new CharacterState(RuntimeEntityId.NewId());
            reactor.AddResource(new ResourceState(reaction.Id, max: 1));
            registry.Register(reactor);
            var target = new CharacterState(RuntimeEntityId.NewId());
            registry.Register(target);

            var opportunistAttack = CreateAbility("ability-opportunist", AbilityTag.Reaction, reaction, 1);
            var resolution = new AbilityResolutionService(registry, new DiceService(new SeededRandomSource(1)));
            var service = new ReactionService(registry, resolution, new FirstEligibleReactionProvider(), reaction);

            var opportunity = new ReactionOpportunity(GameEvent.OnMiss, reactor.Id, target.Id);
            var resolved = service.ResolveOpportunity(opportunity, new[] { opportunistAttack }, new[] { target.Id });

            Assert.IsTrue(resolved);
            reactor.TryGetResource(reaction.Id, out var state);
            Assert.AreEqual(0, state.Current);
        }
    }
}
