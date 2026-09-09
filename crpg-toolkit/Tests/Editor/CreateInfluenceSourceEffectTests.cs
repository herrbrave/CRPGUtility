using CRPG.Abilities;
using CRPG.Actors;
using CRPG.Core;
using NUnit.Framework;
using UnityEngine;

namespace CRPG.Tests.EditMode
{
    public class CreateInfluenceSourceEffectTests
    {
        private static CreateInfluenceSourceEffect CreateEffect(string layer, float radius = 5f, float magnitude = 1f)
        {
            var effect = new CreateInfluenceSourceEffect();
            TestDefinitionFactory.SetPrivateField(effect, "layer", layer);
            TestDefinitionFactory.SetPrivateField(effect, "radius", radius);
            TestDefinitionFactory.SetPrivateField(effect, "magnitude", magnitude);
            TestDefinitionFactory.SetPrivateField(effect, "falloff", AnimationCurve.Linear(0f, 1f, 1f, 0f));
            return effect;
        }

        [Test]
        public void RegistersASourceAtEachTargetsPositionWhenSpatialServicesAreWiredUp()
        {
            var targetId = RuntimeEntityId.NewId();
            var positions = new FakePositionProvider();
            positions.Set(targetId, new Vector3(2f, 0f, 3f));
            var influenceMap = new InfluenceMap();

            var context = new AbilityExecutionContext(
                RuntimeEntityId.NewId(), new[] { targetId }, new CharacterRegistry(), new DiceService(new SeededRandomSource(1)),
                combatLog: null, eventBus: null, positions: positions, influenceMap: influenceMap);

            CreateEffect("Fire").Apply(context);

            Assert.AreEqual(1f, influenceMap.Sample("Fire", new Vector3(2f, 0f, 3f)), 0.0001f);
        }

        [Test]
        public void IsANoOpWhenTheContextHasNoSpatialServices()
        {
            var targetId = RuntimeEntityId.NewId();
            var context = new AbilityExecutionContext(
                RuntimeEntityId.NewId(), new[] { targetId }, new CharacterRegistry(), new DiceService(new SeededRandomSource(1)), combatLog: null);

            Assert.DoesNotThrow(() => CreateEffect("Fire").Apply(context));
        }
    }
}
