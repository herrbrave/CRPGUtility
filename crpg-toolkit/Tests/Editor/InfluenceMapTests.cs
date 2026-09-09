using CRPG.Core;
using NUnit.Framework;
using UnityEngine;

namespace CRPG.Tests.EditMode
{
    public class InfluenceMapTests
    {
        private static InfluenceSource CreateSource(string layer, Vector3 position, float radius, float magnitude = 1f) =>
            new(RuntimeEntityId.NewId(), layer, position, radius, magnitude, AnimationCurve.Linear(0f, 1f, 1f, 0f));

        [Test]
        public void SampleFallsOffLinearlyWithDistanceToTheSourceCenter()
        {
            var map = new InfluenceMap();
            map.AddSource(CreateSource("Danger", Vector3.zero, radius: 10f));

            Assert.AreEqual(1f, map.Sample("Danger", Vector3.zero), 0.0001f); // at the center
            Assert.AreEqual(0.5f, map.Sample("Danger", new Vector3(5f, 0f, 0f)), 0.0001f); // halfway to the edge
            Assert.AreEqual(0f, map.Sample("Danger", new Vector3(10f, 0f, 0f)), 0.0001f); // exactly at the edge
        }

        [Test]
        public void SampleIsZeroBeyondTheSourcesRadius()
        {
            var map = new InfluenceMap();
            map.AddSource(CreateSource("Danger", Vector3.zero, radius: 10f));

            Assert.AreEqual(0f, map.Sample("Danger", new Vector3(15f, 0f, 0f)));
        }

        [Test]
        public void OverlappingSourcesOnTheSameLayerSum()
        {
            var map = new InfluenceMap();
            map.AddSource(CreateSource("Danger", new Vector3(-5f, 0f, 0f), radius: 10f)); // 0.5 contribution at origin
            map.AddSource(CreateSource("Danger", new Vector3(5f, 0f, 0f), radius: 10f)); // 0.5 contribution at origin

            Assert.AreEqual(1f, map.Sample("Danger", Vector3.zero), 0.0001f);
        }

        [Test]
        public void DifferentLayersAreIndependent()
        {
            var map = new InfluenceMap();
            map.AddSource(CreateSource("Danger", Vector3.zero, radius: 10f));

            Assert.AreEqual(0f, map.Sample("DifficultTerrain", Vector3.zero));
        }

        [Test]
        public void RemoveSourceStopsItFromContributing()
        {
            var map = new InfluenceMap();
            var source = CreateSource("Danger", Vector3.zero, radius: 10f);
            map.AddSource(source);

            var removed = map.RemoveSource(source.Id);

            Assert.IsTrue(removed);
            Assert.AreEqual(0f, map.Sample("Danger", Vector3.zero));
        }
    }
}
