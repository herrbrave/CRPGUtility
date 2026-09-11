using CRPG.Actors;
using CRPG.Core;
using NUnit.Framework;

namespace CRPG.Tests.EditMode
{
    public class AnimationOrderServiceTests
    {
        [Test]
        public void RequestAnimationSetsTheCharactersCurrentAnimationCueId()
        {
            var registry = new CharacterRegistry();
            var character = new CharacterState(RuntimeEntityId.NewId());
            registry.Register(character);
            var cueId = new DefinitionId("anim-attack-melee");

            var service = new AnimationOrderService(registry);
            service.RequestAnimation(character.Id, cueId);

            Assert.AreEqual(cueId, character.CurrentAnimationCueId);
        }

        [Test]
        public void RequestAnimationPublishesAnimationCueChangedWithTheCue()
        {
            var registry = new CharacterRegistry();
            var character = new CharacterState(RuntimeEntityId.NewId());
            registry.Register(character);
            var cueId = new DefinitionId("anim-attack-melee");

            var eventBus = new GameEventBus();
            GameEventContext received = null;
            eventBus.Subscribe(GameEvent.AnimationCueChanged, context => received = context);

            var service = new AnimationOrderService(registry, eventBus);
            service.RequestAnimation(character.Id, cueId);

            Assert.IsNotNull(received);
            Assert.AreEqual(character.Id, received.Target);
            Assert.AreEqual(cueId, received.Data["CueId"]);
        }

        [Test]
        public void RequestAnimationForAnUnknownCharacterDoesNotThrowOrPublish()
        {
            var registry = new CharacterRegistry();
            var eventBus = new GameEventBus();
            var published = false;
            eventBus.Subscribe(GameEvent.AnimationCueChanged, _ => published = true);

            var service = new AnimationOrderService(registry, eventBus);

            Assert.DoesNotThrow(() => service.RequestAnimation(RuntimeEntityId.NewId(), new DefinitionId("anim-idle")));
            Assert.IsFalse(published);
        }
    }
}
