using System.Collections.Generic;
using CRPG.Actors;
using CRPG.Combat;
using CRPG.Core;
using NUnit.Framework;

namespace CRPG.Tests.EditMode
{
    /// <summary>Task 4.5: the first real, shipped <see cref="IFactionProvider"/> -- resolves an entity's
    /// faction from its live <see cref="CombatEncounterService"/> membership, not any standing/out-of-combat
    /// store.</summary>
    public class EncounterFactionProviderTests
    {
        private sealed class FixedInitiativeRule : IInitiativeRule
        {
            private readonly Dictionary<RuntimeEntityId, int> rolls;
            public FixedInitiativeRule(Dictionary<RuntimeEntityId, int> rolls) => this.rolls = rolls;

            public RollResult RollInitiative(RuntimeEntityId combatant) =>
                new(new RollRequest(new DiceExpression(1, 20), source: combatant), new[] { new DieRollResult(20, rolls[combatant], true) }, 0);
        }

        private static CombatEncounterService CreateStartedEncounter(out RuntimeEntityId hero, out RuntimeEntityId goblin)
        {
            var actionResource = TestDefinitionFactory.CreateResourceDefinition("resource-action", "Action");
            var registry = new CharacterRegistry();
            hero = RuntimeEntityId.NewId();
            goblin = RuntimeEntityId.NewId();
            var heroCharacter = new CharacterState(hero);
            var goblinCharacter = new CharacterState(goblin);
            heroCharacter.AddResource(new ResourceState(actionResource.Id, max: 1, current: 0));
            goblinCharacter.AddResource(new ResourceState(actionResource.Id, max: 1, current: 0));
            registry.Register(heroCharacter);
            registry.Register(goblinCharacter);

            var initiative = new FixedInitiativeRule(new Dictionary<RuntimeEntityId, int> { [hero] = 20, [goblin] = 5 });
            var service = new CombatEncounterService(registry, initiative, new TurnResourceSet(new[] { actionResource }));
            service.StartCombat(new[] { (hero, new DefinitionId("faction-heroes")), (goblin, new DefinitionId("faction-goblins")) });
            return service;
        }

        [Test]
        public void ReturnsFalseWhenNoEncounterHasStarted()
        {
            var registry = new CharacterRegistry();
            var initiative = new FixedInitiativeRule(new Dictionary<RuntimeEntityId, int>());
            var service = new CombatEncounterService(registry, initiative, new TurnResourceSet(new[] { TestDefinitionFactory.CreateResourceDefinition("resource-action", "Action") }));
            var provider = new EncounterFactionProvider(service);

            Assert.IsFalse(provider.TryGetFaction(RuntimeEntityId.NewId(), out _));
        }

        [Test]
        public void ResolvesEachCombatantsOwnFactionFromTheLiveEncounter()
        {
            var service = CreateStartedEncounter(out var hero, out var goblin);
            var provider = new EncounterFactionProvider(service);

            Assert.IsTrue(provider.TryGetFaction(hero, out var heroFaction));
            Assert.AreEqual(new DefinitionId("faction-heroes"), heroFaction);

            Assert.IsTrue(provider.TryGetFaction(goblin, out var goblinFaction));
            Assert.AreEqual(new DefinitionId("faction-goblins"), goblinFaction);
        }

        [Test]
        public void ReturnsFalseForAnEntityNotInTheEncounter()
        {
            var service = CreateStartedEncounter(out _, out _);
            var provider = new EncounterFactionProvider(service);

            Assert.IsFalse(provider.TryGetFaction(RuntimeEntityId.NewId(), out _));
        }

        [Test]
        public void ReturnsFalseOnceACombatantHasBeenRemovedFromTheEncounter()
        {
            var service = CreateStartedEncounter(out var hero, out var goblin);
            var provider = new EncounterFactionProvider(service);

            service.Encounter.RemoveCombatant(goblin);

            Assert.IsFalse(provider.TryGetFaction(goblin, out _));
            Assert.IsTrue(provider.TryGetFaction(hero, out _));
        }
    }
}
