using System.Collections.Generic;
using CRPG.Actors;
using CRPG.AI;
using CRPG.Core;
using NUnit.Framework;
using UnityEngine;

namespace CRPG.Tests.EditMode
{
    public class ConsiderationInputRegistryTests
    {
        private RuntimeEntityId self;
        private RuntimeEntityId target;
        private CharacterRegistry characters;
        private FakePositionProvider positions;
        private ConsiderationInputRegistry registry;

        [SetUp]
        public void SetUp()
        {
            self = RuntimeEntityId.NewId();
            target = RuntimeEntityId.NewId();
            characters = new CharacterRegistry();
            positions = new FakePositionProvider();
            registry = new ConsiderationInputRegistry();
        }

        private ConsiderationContext CreateContext(IRandomSource randomSource = null, InfluenceMap influenceMap = null) =>
            new(self, target, characters, positions, randomSource ?? new SeededRandomSource(1), influenceMap);

        [Test]
        public void DistanceReturnsTheEuclideanDistanceBetweenSelfAndTarget()
        {
            positions.Set(self, Vector3.zero);
            positions.Set(target, new Vector3(3f, 0f, 4f));

            Assert.AreEqual(5f, registry.GetInput("Distance", CreateContext()), 0.0001f);
        }

        [Test]
        public void RandomChanceReturnsAValueInUnitRange()
        {
            var random = new TestRandomSource();
            random.EnqueueResult(7500);

            var value = registry.GetInput("RandomChance", CreateContext(random));

            Assert.AreEqual(0.75f, value, 0.0001f);
        }

        [Test]
        public void SelfAndTargetResourcePercentReadFromCharacterState()
        {
            var hpId = new DefinitionId("resource-hp");
            var selfCharacter = new CharacterState(self);
            selfCharacter.AddResource(new ResourceState(hpId, max: 20, current: 5));
            var targetCharacter = new CharacterState(target);
            targetCharacter.AddResource(new ResourceState(hpId, max: 10, current: 10));
            characters.Register(selfCharacter);
            characters.Register(targetCharacter);

            Assert.AreEqual(0.25f, registry.GetInput("SelfResourcePercent:resource-hp", CreateContext()), 0.0001f);
            Assert.AreEqual(1f, registry.GetInput("TargetResourcePercent:resource-hp", CreateContext()), 0.0001f);
        }

        [Test]
        public void InfluenceMapSampleReadsAtTheRequestedEntitysPosition()
        {
            var map = new InfluenceMap();
            map.AddSource(new InfluenceSource(RuntimeEntityId.NewId(), "Danger", Vector3.zero, 10f, 1f, AnimationCurve.Linear(0f, 1f, 1f, 0f)));
            positions.Set(target, Vector3.zero);

            var value = registry.GetInput("InfluenceMap:Danger:Target", CreateContext(influenceMap: map));

            Assert.AreEqual(1f, value, 0.0001f);
        }

        [Test]
        public void UnresolvedKeysReturnZeroRatherThanThrowing()
        {
            Assert.AreEqual(0f, registry.GetInput("SomeTypo", CreateContext()));
        }

        [Test]
        public void CustomKeysCanBeRegistered()
        {
            registry.Register("AlwaysOne", _ => 1f);

            Assert.AreEqual(1f, registry.GetInput("AlwaysOne", CreateContext()));
        }
    }
}
