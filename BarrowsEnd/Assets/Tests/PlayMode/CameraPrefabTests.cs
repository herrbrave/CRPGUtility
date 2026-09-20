using System.Collections;
using System.Collections.Generic;
using CRPG.Camera;
using CRPG.Core;
using CRPG.Exploration;
using CRPG.Gameplay;
using CRPG.Party;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace BarrowsEnd.Tests
{
    /// <summary>
    /// Task 3.1: the authored camera rig prefab, instantiated and driven by the REAL PartyCameraFollowBehaviour against a party whose selected member
    /// walks: it follows, stays pixel-snapped, keeps the visible rectangle inside the map bounds, steps integer scales, and stays at yaw 0 and pitch 90.
    /// </summary>
    public class CameraPrefabTests
    {
        private sealed class MovingSource : ISelectableSource
        {
            public readonly Dictionary<RuntimeEntityId, Vector3> Positions = new();
            public IEnumerable<SelectableEntity> GetSelectables()
            {
                foreach (var pair in Positions) yield return new SelectableEntity(pair.Key, pair.Value);
            }
        }

        private const string RigPrefabPath = "Assets/Content/Camera/BarrowsEnd_CameraRig.prefab";

        private GameObject instance;
        private IsometricCameraRig rig;
        private UnityEngine.Camera cam;
        private MovingSource source;
        private PartyState party;
        private RuntimeEntityId hero;

        [SetUp]
        public void Setup()
        {
            GameObject prefab = null;
#if UNITY_EDITOR
            prefab = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(RigPrefabPath);
#endif
            Assert.IsNotNull(prefab, "run 'Barrows End/Content/Author Camera'");
            instance = Object.Instantiate(prefab);
            rig = instance.GetComponent<IsometricCameraRig>();
            cam = instance.GetComponentInChildren<UnityEngine.Camera>();

            hero = RuntimeEntityId.NewId();
            party = new PartyState(new[] { hero });
            party.Select(hero);
            source = new MovingSource();
            source.Positions[hero] = new Vector3(20f, 0f, 20f);
            instance.GetComponent<PartyCameraFollowBehaviour>().Initialize(party, source);
        }

        [TearDown]
        public void TearDown()
        {
            if (instance != null) Object.Destroy(instance);
        }

        [UnityTest]
        public IEnumerator TheInstantiatedRigLooksStraightDownOrthographicallyAtYawZeroAndStartsAtTheParty()
        {
            yield return null;
            Assert.IsTrue(cam.orthographic);
            Assert.AreEqual(90f, cam.transform.eulerAngles.x, 0.01f);
            Assert.AreEqual(0f, Mathf.DeltaAngle(0f, cam.transform.eulerAngles.y), 0.01f);
            Assert.AreEqual(3, rig.Rig.Scale, "default scale 3");
            Assert.AreEqual(cam.pixelHeight / (2f * 16f * 3f), cam.orthographicSize, 1e-4f);
            Assert.AreEqual(20f, rig.Rig.FocusPoint.x, 0.05f, "SnapToActiveCharacter: the camera starts at the party");
            Assert.AreEqual(20f, rig.Rig.FocusPoint.z, 0.05f);
        }

        [UnityTest]
        public IEnumerator TheCameraFollowsAWalkingPartyMemberOnThePixelGridAndStaysInsideTheMap()
        {
            yield return null;
            rig.Rig.SetBounds(new Rect(0f, 0f, 60f, 60f));
            var step = 1f / (16f * 3f);

            for (var i = 0; i < 240; i++)
            {
                source.Positions[hero] = new Vector3(20f + i * 0.4f, 0f, 20f + i * 0.2f); // walks off toward, then past, the map corner
                yield return null;

                var p = instance.transform.position;
                Assert.AreEqual(0f, Mathf.Repeat(p.x / step + 0.5f, 1f) - 0.5f, 2e-3f, $"frame {i}: x snapped");
                var half = new Vector2(cam.orthographicSize * cam.aspect, cam.orthographicSize);
                Assert.LessOrEqual(cam.transform.position.x + half.x, 60.1f, $"frame {i}: right edge inside the map");
                Assert.LessOrEqual(cam.transform.position.z + half.y, 60.1f, $"frame {i}: top edge inside the map");
            }

            Assert.Greater(instance.transform.position.x, 25f, "and it really followed (the checks are not vacuous)");
        }

        [UnityTest]
        public IEnumerator ZoomStepsThroughTheListedIntegerScalesAndKeepsTheCameraPixelPerfect()
        {
            yield return null;
            var seen = new List<int>();
            for (var i = 0; i < 6; i++)
            {
                rig.Rig.ApplyZoom(1f);
                yield return null;
                seen.Add(rig.Rig.Scale);
                Assert.AreEqual(cam.pixelHeight / (2f * 16f * rig.Rig.Scale), cam.orthographicSize, 1e-4f, "one source pixel is exactly Scale screen pixels");
            }

            CollectionAssert.AreEqual(new[] { 4, 5, 6, 6, 6, 6 }, seen, "stops at the top of the list");
            for (var i = 0; i < 6; i++) { rig.Rig.ApplyZoom(-1f); yield return null; }
            Assert.AreEqual(2, rig.Rig.Scale, "and at the bottom");
        }

        [UnityTest]
        public IEnumerator RotationAndSnapRotationInputCannotTurnTheYawOrChangeThePitch()
        {
            yield return null;
            rig.Rig.Rotate(1f, 5f);
            rig.Rig.SnapRotate(1f);
            rig.Rig.SnapRotate(-1f);
            yield return null;
            Assert.AreEqual(0f, Mathf.DeltaAngle(0f, cam.transform.eulerAngles.y), 0.01f);
            Assert.AreEqual(90f, cam.transform.eulerAngles.x, 0.01f);
        }
    }
}
