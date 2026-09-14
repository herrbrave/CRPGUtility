using System.Collections;
using System.Collections.Generic;
using CRPG.Core;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace CRPG.Tests.PlayMode
{
    /// <summary>Task 4.2: PhysicsLineOfSightProvider needs a real scene/frame (Physics.Linecast), hence
    /// [UnityTest] -- mirrors PointerCommandTests' own established discipline for Physics-dependent tests in
    /// this repo (per-test origin to avoid broadphase clashes between tests, WaitForColliders before querying).</summary>
    public class PhysicsLineOfSightProviderTests
    {
        private static int originCounter;
        private Vector3 Origin { get; set; }
        private readonly List<GameObject> spawned = new();

        [SetUp]
        public void SetUp()
        {
            Origin = new Vector3(9000f + originCounter * 500f, 0f, 9000f);
            originCounter++;
        }

        [TearDown]
        public void TearDown()
        {
            foreach (var go in spawned) if (go != null) Object.DestroyImmediate(go);
            spawned.Clear();
        }

        private static IEnumerator WaitForColliders()
        {
            yield return null;
            yield return new WaitForFixedUpdate();
            Physics.SyncTransforms();
        }

        private GameObject CreateObstruction(int layer)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.layer = layer;
            go.transform.position = Origin;
            go.transform.localScale = new Vector3(2f, 2f, 2f);
            spawned.Add(go);
            return go;
        }

        [UnityTest]
        public IEnumerator HasLineOfSightIsTrueWithNoObstructionBetweenTheTwoPoints()
        {
            var provider = new PhysicsLineOfSightProvider(~0);
            yield return WaitForColliders();

            var result = provider.HasLineOfSight(Origin + new Vector3(-5f, 0f, 0f), Origin + new Vector3(5f, 0f, 0f));

            Assert.IsTrue(result);
        }

        [UnityTest]
        public IEnumerator HasLineOfSightIsFalseWhenAColliderOnTheObstructionMaskBlocksTheLine()
        {
            const int obstructionLayer = 0; // Default -- included in the mask below
            CreateObstruction(obstructionLayer);
            var provider = new PhysicsLineOfSightProvider(1 << obstructionLayer);
            yield return WaitForColliders();

            var result = provider.HasLineOfSight(Origin + new Vector3(-5f, 0f, 0f), Origin + new Vector3(5f, 0f, 0f));

            Assert.IsFalse(result);
        }

        [UnityTest]
        public IEnumerator HasLineOfSightIgnoresACollidingObjectNotOnTheObstructionMask()
        {
            const int obstructionLayer = 0;
            CreateObstruction(obstructionLayer);
            var provider = new PhysicsLineOfSightProvider(0); // mask excludes every layer, including Default

            yield return WaitForColliders();

            var result = provider.HasLineOfSight(Origin + new Vector3(-5f, 0f, 0f), Origin + new Vector3(5f, 0f, 0f));

            Assert.IsTrue(result);
        }
    }
}
