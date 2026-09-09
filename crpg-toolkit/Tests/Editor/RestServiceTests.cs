using CRPG.Actors;
using CRPG.Core;
using NUnit.Framework;

namespace CRPG.Tests.EditMode
{
    public class RestServiceTests
    {
        [Test]
        public void ShortRestOnlyRestoresResourcesTaggedShortRestOrBoth()
        {
            var rulesDatabase = new RulesDatabase();
            var secondWind = TestDefinitionFactory.CreateResourceDefinition("resource-second-wind", "Second Wind", RestTrigger.Both);
            var spellSlots = TestDefinitionFactory.CreateResourceDefinition("resource-spell-slots", "Spell Slots", RestTrigger.LongRest);
            rulesDatabase.Register(secondWind);
            rulesDatabase.Register(spellSlots);

            var registry = new CharacterRegistry();
            var character = new CharacterState(RuntimeEntityId.NewId());
            character.AddResource(new ResourceState(secondWind.Id, max: 1, current: 0));
            character.AddResource(new ResourceState(spellSlots.Id, max: 3, current: 0));
            registry.Register(character);

            var service = new RestService(registry, rulesDatabase);
            service.Rest(new[] { character.Id }, GameEvent.ShortRest);

            character.TryGetResource(secondWind.Id, out var secondWindState);
            character.TryGetResource(spellSlots.Id, out var spellSlotsState);
            Assert.AreEqual(1, secondWindState.Current); // restored
            Assert.AreEqual(0, spellSlotsState.Current); // not restored -- long rest only
        }

        [Test]
        public void LongRestRestoresEverythingTaggedLongRestOrBoth()
        {
            var rulesDatabase = new RulesDatabase();
            var secondWind = TestDefinitionFactory.CreateResourceDefinition("resource-second-wind", "Second Wind", RestTrigger.Both);
            var spellSlots = TestDefinitionFactory.CreateResourceDefinition("resource-spell-slots", "Spell Slots", RestTrigger.LongRest);
            var turnAction = TestDefinitionFactory.CreateResourceDefinition("resource-action", "Action", RestTrigger.None);
            rulesDatabase.Register(secondWind);
            rulesDatabase.Register(spellSlots);
            rulesDatabase.Register(turnAction);

            var registry = new CharacterRegistry();
            var character = new CharacterState(RuntimeEntityId.NewId());
            character.AddResource(new ResourceState(secondWind.Id, max: 1, current: 0));
            character.AddResource(new ResourceState(spellSlots.Id, max: 3, current: 0));
            character.AddResource(new ResourceState(turnAction.Id, max: 1, current: 0));
            registry.Register(character);

            var service = new RestService(registry, rulesDatabase);
            service.Rest(new[] { character.Id }, GameEvent.LongRest);

            character.TryGetResource(secondWind.Id, out var secondWindState);
            character.TryGetResource(spellSlots.Id, out var spellSlotsState);
            character.TryGetResource(turnAction.Id, out var actionState);
            Assert.AreEqual(1, secondWindState.Current);
            Assert.AreEqual(3, spellSlotsState.Current);
            Assert.AreEqual(0, actionState.Current); // RestTrigger.None -- rest never touches this, only TurnResourceSet does
        }

        [Test]
        public void RestAppliesToEveryPartyMemberGiven()
        {
            var rulesDatabase = new RulesDatabase();
            var hp = TestDefinitionFactory.CreateResourceDefinition("resource-hp", "Hit Points", RestTrigger.LongRest);
            rulesDatabase.Register(hp);

            var registry = new CharacterRegistry();
            var a = new CharacterState(RuntimeEntityId.NewId());
            var b = new CharacterState(RuntimeEntityId.NewId());
            a.AddResource(new ResourceState(hp.Id, max: 10, current: 2));
            b.AddResource(new ResourceState(hp.Id, max: 8, current: 1));
            registry.Register(a);
            registry.Register(b);

            var service = new RestService(registry, rulesDatabase);
            service.Rest(new[] { a.Id, b.Id }, GameEvent.LongRest);

            a.TryGetResource(hp.Id, out var aState);
            b.TryGetResource(hp.Id, out var bState);
            Assert.AreEqual(10, aState.Current);
            Assert.AreEqual(8, bState.Current);
        }

        [Test]
        public void RestPublishesExactlyOneEventForTheWholeParty()
        {
            var rulesDatabase = new RulesDatabase();
            var registry = new CharacterRegistry();
            var a = new CharacterState(RuntimeEntityId.NewId());
            var b = new CharacterState(RuntimeEntityId.NewId());
            registry.Register(a);
            registry.Register(b);

            var eventBus = new GameEventBus();
            var callCount = 0;
            eventBus.Subscribe(GameEvent.LongRest, _ => callCount++);

            var service = new RestService(registry, rulesDatabase, eventBus);
            service.Rest(new[] { a.Id, b.Id }, GameEvent.LongRest);

            Assert.AreEqual(1, callCount);
        }
    }
}
