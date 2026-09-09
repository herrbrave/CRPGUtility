using System.Collections.Generic;
using CRPG.Actors;
using CRPG.Combat;
using CRPG.Core;
using NUnit.Framework;

namespace CRPG.Tests.EditMode
{
    public class CombatEncounterServiceTests
    {
        private sealed class FixedInitiativeRule : IInitiativeRule
        {
            private readonly Dictionary<RuntimeEntityId, int> rolls;
            public FixedInitiativeRule(Dictionary<RuntimeEntityId, int> rolls) => this.rolls = rolls;

            public RollResult RollInitiative(RuntimeEntityId combatant) =>
                new(new RollRequest(new DiceExpression(1, 20), source: combatant), new[] { new DieRollResult(20, rolls[combatant], true) }, 0);
        }

        [Test]
        public void StartCombatOrdersCombatantsAndRestoresFirstTurnResources()
        {
            var actionResource = TestDefinitionFactory.CreateResourceDefinition("resource-action", "Action");
            var registry = new CharacterRegistry();
            var slow = new CharacterState(RuntimeEntityId.NewId());
            var fast = new CharacterState(RuntimeEntityId.NewId());
            slow.AddResource(new ResourceState(actionResource.Id, max: 1, current: 0));
            fast.AddResource(new ResourceState(actionResource.Id, max: 1, current: 0));
            registry.Register(slow);
            registry.Register(fast);

            var initiative = new FixedInitiativeRule(new Dictionary<RuntimeEntityId, int> { [slow.Id] = 5, [fast.Id] = 20 });
            var turnResources = new TurnResourceSet(new[] { actionResource });
            var eventBus = new GameEventBus();
            var events = new List<GameEvent>();
            eventBus.Subscribe(GameEvent.CombatStarted, ctx => events.Add(ctx.Event));
            eventBus.Subscribe(GameEvent.TurnStarted, ctx => events.Add(ctx.Event));

            var service = new CombatEncounterService(registry, initiative, turnResources, eventBus);
            service.StartCombat(new[] { slow.Id, fast.Id });

            Assert.AreEqual(fast.Id, service.Encounter.CurrentTurn.Id);
            fast.TryGetResource(actionResource.Id, out var fastAction);
            Assert.AreEqual(1, fastAction.Current);
            slow.TryGetResource(actionResource.Id, out var slowAction);
            Assert.AreEqual(0, slowAction.Current); // not their turn yet
            CollectionAssert.AreEqual(new[] { GameEvent.CombatStarted, GameEvent.TurnStarted }, events);
        }

        [Test]
        public void EndTurnAdvancesAndRestoresTheNextCombatantsResources()
        {
            var actionResource = TestDefinitionFactory.CreateResourceDefinition("resource-action", "Action");
            var registry = new CharacterRegistry();
            var a = new CharacterState(RuntimeEntityId.NewId());
            var b = new CharacterState(RuntimeEntityId.NewId());
            a.AddResource(new ResourceState(actionResource.Id, max: 1));
            b.AddResource(new ResourceState(actionResource.Id, max: 1, current: 0));
            registry.Register(a);
            registry.Register(b);

            var initiative = new FixedInitiativeRule(new Dictionary<RuntimeEntityId, int> { [a.Id] = 20, [b.Id] = 10 });
            var service = new CombatEncounterService(registry, initiative, new TurnResourceSet(new[] { actionResource }));
            service.StartCombat(new[] { a.Id, b.Id });

            a.TryGetResource(actionResource.Id, out var aAction);
            aAction.Spend(1); // simulate spending their action

            service.EndTurn();

            Assert.AreEqual(b.Id, service.Encounter.CurrentTurn.Id);
            b.TryGetResource(actionResource.Id, out var bAction);
            Assert.AreEqual(1, bAction.Current);
        }

        [Test]
        public void EndCombatClearsTheEncounterAndPublishesCombatEnded()
        {
            var actionResource = TestDefinitionFactory.CreateResourceDefinition("resource-action", "Action");
            var registry = new CharacterRegistry();
            var a = new CharacterState(RuntimeEntityId.NewId());
            a.AddResource(new ResourceState(actionResource.Id, max: 1));
            registry.Register(a);

            var initiative = new FixedInitiativeRule(new Dictionary<RuntimeEntityId, int> { [a.Id] = 10 });
            var eventBus = new GameEventBus();
            var ended = false;
            eventBus.Subscribe(GameEvent.CombatEnded, _ => ended = true);

            var service = new CombatEncounterService(registry, initiative, new TurnResourceSet(new[] { actionResource }), eventBus);
            service.StartCombat(new[] { a.Id });
            service.EndCombat();

            Assert.IsTrue(ended);
            Assert.IsNull(service.Encounter);
        }

        [Test]
        public void StartCombatWithFactionsTagsEachCombatantSoTheEncounterCanTellSidesApart()
        {
            var actionResource = TestDefinitionFactory.CreateResourceDefinition("resource-action", "Action");
            var registry = new CharacterRegistry();
            var hero = new CharacterState(RuntimeEntityId.NewId());
            var goblin = new CharacterState(RuntimeEntityId.NewId());
            hero.AddResource(new ResourceState(actionResource.Id, max: 1));
            goblin.AddResource(new ResourceState(actionResource.Id, max: 1));
            registry.Register(hero);
            registry.Register(goblin);

            var initiative = new FixedInitiativeRule(new Dictionary<RuntimeEntityId, int> { [hero.Id] = 20, [goblin.Id] = 10 });
            var service = new CombatEncounterService(registry, initiative, new TurnResourceSet(new[] { actionResource }));

            var heroes = new DefinitionId("faction-heroes");
            var goblins = new DefinitionId("faction-goblins");
            service.StartCombat(new[] { (hero.Id, heroes), (goblin.Id, goblins) });

            CollectionAssert.AreEqual(new[] { goblin.Id }, service.Encounter.GetEnemiesOf(hero.Id));
        }
    }
}
