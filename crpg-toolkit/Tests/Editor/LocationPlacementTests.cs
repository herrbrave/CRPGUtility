using System;
using System.Collections.Generic;
using System.Linq;
using CRPG.Actors;
using CRPG.Editor.Location;
using CRPG.Gameplay.Scenes;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace CRPG.Tests.EditMode
{
    /// <summary>The pure placement API behind the NPC placement tool: anchors, ids, one Undo step.</summary>
    public sealed class LocationPlacementTests
    {
        private readonly List<UnityEngine.Object> made = new();
        private GameObject root;
        private LocationContent location;

        [SetUp]
        public void SetUp()
        {
            root = new GameObject("CRPG"); made.Add(root);
            var host = new GameObject("Location"); host.transform.SetParent(root.transform, false);
            location = host.AddComponent<LocationContent>();
        }

        [TearDown]
        public void TearDown()
        {
            foreach (var o in made) if (o != null) UnityEngine.Object.DestroyImmediate(o);
            made.Clear();
        }

        private NpcDefinition Npc(string id)
        {
            var npc = ScriptableObject.CreateInstance<NpcDefinition>(); made.Add(npc);
            var so = new SerializedObject(npc);
            so.FindProperty("id").stringValue = id;
            so.ApplyModifiedPropertiesWithoutUndo();
            return npc;
        }

        private SerializedProperty Points() => new SerializedObject(location).FindProperty("spawnPoints");

        [Test]
        public void AddSpawnCreatesTheAnchorUnderASiblingNpcsRootAndTheEntryWithAnIdFromTheDefinition()
        {
            var id = LocationPlacement.AddSpawn(location, Npc("npc-village-hild"), new Vector3(3f, 0f, 4f), Quaternion.Euler(0f, 90f, 0f));
            Assert.AreEqual("village-hild", id, "the npc- prefix is dropped");
            Assert.AreEqual(1, location.SpawnPoints.Count);
            var point = location.SpawnPoints[0];
            Assert.AreEqual(id, point.SpawnId); Assert.IsNotNull(point.Definition); Assert.IsNotNull(point.Anchor);
            Assert.AreEqual("NPCs", point.Anchor.parent.name); Assert.AreSame(root.transform, point.Anchor.parent.parent, "NPCs is a sibling of the Location host, under the same root");
            Assert.AreEqual("Anchor_village-hild", point.Anchor.name);
            Assert.AreEqual(3f, point.Anchor.position.x, 1e-4f); Assert.AreEqual(4f, point.Anchor.position.z, 1e-4f);
            Assert.AreEqual(90f, point.Anchor.eulerAngles.y, 1e-3f);
        }

        [Test]
        public void ASecondSpawnOfTheSameDefinitionGetsADistinctId()
        {
            var npc = Npc("npc-guard");
            Assert.AreEqual("guard", LocationPlacement.AddSpawn(location, npc, Vector3.zero, Quaternion.identity));
            Assert.AreEqual("guard-2", LocationPlacement.AddSpawn(location, npc, Vector3.one, Quaternion.identity));
            Assert.AreEqual("guard-3", LocationPlacement.AddSpawn(location, npc, Vector3.one * 2f, Quaternion.identity));
            Assert.AreEqual(3, location.SpawnPoints.Select(p => p.SpawnId).Distinct().Count());
        }

        [Test]
        public void ANewIdAvoidsTheOtherOpenLocationsAndTheReservedHeroPrefix()
        {
            var other = new GameObject("Other").AddComponent<LocationContent>(); made.Add(other.gameObject);
            LocationPlacement.AddSpawn(other, Npc("npc-smith"), Vector3.zero, Quaternion.identity);
            Assert.AreEqual("smith-2", LocationPlacement.NextSpawnId(location, Npc("npc-smith"), new[] { other }));
            Assert.AreEqual("npc-hero-bjorn", LocationPlacement.NextSpawnId(location, Npc("npc-hero-bjorn"), null), "a generated id never starts with the party's reserved prefix: the npc- prefix is kept");
        }

        [Test]
        public void ATypedIdIsKeptAndADuplicateTypedIdIsRefused()
        {
            Assert.AreEqual("the-smith", LocationPlacement.AddSpawn(location, Npc("npc-smith"), Vector3.zero, Quaternion.identity, "the-smith"));
            Assert.Throws<ArgumentException>(() => LocationPlacement.AddSpawn(location, Npc("npc-other"), Vector3.zero, Quaternion.identity, "the-smith"));
            Assert.Throws<ArgumentException>(() => LocationPlacement.AddSpawn(location, null, Vector3.zero, Quaternion.identity));
            Assert.AreEqual(1, location.SpawnPoints.Count, "a refused add changes nothing");
        }

        [Test]
        public void TheAnchorParentIsTheCommonParentOfTheExistingAnchors()
        {
            var group = new GameObject("Villagers"); group.transform.SetParent(root.transform, false);
            var first = new GameObject("a"); first.transform.SetParent(group.transform, false);
            var second = new GameObject("b"); second.transform.SetParent(group.transform, false);
            var so = new SerializedObject(location);
            var points = so.FindProperty("spawnPoints");
            foreach (var anchor in new[] { first, second })
            {
                points.InsertArrayElementAtIndex(points.arraySize);
                var element = points.GetArrayElementAtIndex(points.arraySize - 1);
                element.FindPropertyRelative("SpawnId").stringValue = anchor.name;
                element.FindPropertyRelative("Anchor").objectReferenceValue = anchor.transform;
            }

            so.ApplyModifiedPropertiesWithoutUndo();
            Assert.AreSame(group.transform, LocationPlacement.AnchorParent(location));
            LocationPlacement.AddSpawn(location, Npc("npc-new"), Vector3.zero, Quaternion.identity);
            Assert.AreSame(group.transform, location.SpawnPoints.Last().Anchor.parent);
        }

        [Test]
        public void WithNoSiblingRootTheAnchorsGoUnderANewNpcsRootBesideTheHostOrUnderAHostWithNoParent()
        {
            var loose = new GameObject("LooseLocation").AddComponent<LocationContent>(); made.Add(loose.gameObject);
            LocationPlacement.AddSpawn(loose, Npc("npc-x"), Vector3.zero, Quaternion.identity);
            var anchor = loose.SpawnPoints[0].Anchor;
            Assert.AreEqual("NPCs", anchor.parent.name, "a host with no parent still gets an NPCs root");
            made.Add(anchor.parent.gameObject);
        }

        [Test]
        public void AddSpawnIsOneUndoStepThatRemovesBothTheEntryAndTheAnchor()
        {
            LocationPlacement.AddSpawn(location, Npc("npc-undo"), Vector3.zero, Quaternion.identity);
            Assert.AreEqual(1, location.SpawnPoints.Count);
            Undo.PerformUndo();
            Assert.AreEqual(0, location.SpawnPoints.Count, "the entry is gone after ONE undo");
            Assert.IsFalse(UnityEngine.Object.FindObjectsByType<Transform>().Any(t => t.name.StartsWith("Anchor_undo")), "so is the anchor");
            Undo.PerformRedo();
            Assert.AreEqual(1, location.SpawnPoints.Count, "and redo brings the entry back");
            Assert.IsNotNull(location.SpawnPoints[0].Anchor);
        }

        [Test]
        public void RemoveSpawnCanKeepOrDeleteTheAnchorAndNeverDeletesASharedOne()
        {
            var npc = Npc("npc-gone");
            LocationPlacement.AddSpawn(location, npc, Vector3.zero, Quaternion.identity);
            LocationPlacement.AddSpawn(location, npc, Vector3.one, Quaternion.identity);
            var keep = location.SpawnPoints[0].Anchor; var drop = location.SpawnPoints[1].Anchor;
            LocationPlacement.RemoveSpawn(location, 1, deleteAnchor: true);
            Assert.AreEqual(1, location.SpawnPoints.Count); Assert.IsTrue(drop == null, "the anchor was deleted");
            LocationPlacement.RemoveSpawn(location, 0, deleteAnchor: false);
            Assert.AreEqual(0, location.SpawnPoints.Count); Assert.IsTrue(keep != null, "the anchor was kept");

            // two entries on ONE anchor: removing one must not destroy what the other still uses
            var shared = new GameObject("Shared").transform; made.Add(shared.gameObject);
            var so = new SerializedObject(location); var points = so.FindProperty("spawnPoints");
            for (var i = 0; i < 2; i++)
            {
                points.InsertArrayElementAtIndex(points.arraySize);
                var element = points.GetArrayElementAtIndex(points.arraySize - 1);
                element.FindPropertyRelative("SpawnId").stringValue = "s" + i; element.FindPropertyRelative("Anchor").objectReferenceValue = shared;
            }

            so.ApplyModifiedPropertiesWithoutUndo();
            LocationPlacement.RemoveSpawn(location, 0, deleteAnchor: true);
            Assert.IsTrue(shared != null, "the shared anchor survives");
            Assert.Throws<ArgumentOutOfRangeException>(() => LocationPlacement.RemoveSpawn(location, 5, true));
        }
    }
}
