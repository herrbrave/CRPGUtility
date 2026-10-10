using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using CRPG.Actors;
using CRPG.Editor.Location;
using CRPG.Editor.Validation;
using CRPG.Gameplay.Scenes;
using CRPG.Navigation;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.SceneManagement;

namespace CRPG.Tests.EditMode
{
    /// <summary>The location validator: each rule fires for its own defect (and only that) and a clean location has no findings. Uses a fake navigation service, so no baked NavMesh is needed.</summary>
    public sealed class LocationValidatorTests
    {
        private sealed class FakeNav : INavMeshService
        {
            public Func<Vector3, Vector3?> Snap = p => p;
            public Func<Vector3, Vector3, bool> Reach = (a, b) => true;

            public bool TrySamplePosition(Vector3 point, out Vector3 sampledPosition, float maxDistance = 5f, int areaMask = NavMesh.AllAreas)
            {
                var snapped = Snap(point);
                sampledPosition = snapped ?? default;
                return snapped.HasValue && Vector3.Distance(snapped.Value, point) <= maxDistance;
            }

            public bool TryCalculatePathLength(Vector3 from, Vector3 to, out float length, int areaMask = NavMesh.AllAreas) { length = Vector3.Distance(from, to); return Reach(from, to); }
            public bool TryCalculatePath(Vector3 from, Vector3 to, out Vector3[] corners, int areaMask = NavMesh.AllAreas) { corners = new[] { from, to }; return Reach(from, to); }
        }

        private readonly List<UnityEngine.Object> made = new();
        private LocationContent location;
        private GameObject visual;
        private NpcDefinition smith, guard;
        private FakeNav nav;
        private const string TempFolder = "Assets/__LocationValidatorTests";

        [SetUp]
        public void SetUp()
        {
            nav = new FakeNav();
            visual = new GameObject("visual"); made.Add(visual);
            smith = Npc("npc-smith"); guard = Npc("npc-guard");
            var root = new GameObject("CRPG"); made.Add(root);
            var host = new GameObject("Location"); host.transform.SetParent(root.transform, false);
            location = host.AddComponent<LocationContent>();
            Add(location, "smith", smith, new Vector3(0f, 0f, 0f)); Add(location, "guard", guard, new Vector3(5f, 0f, 0f));
        }

        [TearDown]
        public void TearDown()
        {
            foreach (var o in made) if (o != null) UnityEngine.Object.DestroyImmediate(o);
            made.Clear();
            if (AssetDatabase.IsValidFolder(TempFolder)) AssetDatabase.DeleteAsset(TempFolder);
        }

        private NpcDefinition Npc(string id, bool withVisual = true)
        {
            var npc = ScriptableObject.CreateInstance<NpcDefinition>(); made.Add(npc);
            var so = new SerializedObject(npc);
            so.FindProperty("id").stringValue = id;
            if (withVisual) so.FindProperty("visualPrefab").objectReferenceValue = visual;
            so.ApplyModifiedPropertiesWithoutUndo();
            return npc;
        }

        private Transform Add(LocationContent target, string spawnId, NpcDefinition definition, Vector3 position, Transform anchorOverride = null)
        {
            var anchor = anchorOverride;
            if (anchor == null) { anchor = new GameObject("Anchor_" + spawnId).transform; made.Add(anchor.gameObject); anchor.SetParent(target.transform.parent, false); anchor.position = position; }
            var so = new SerializedObject(target);
            var points = so.FindProperty("spawnPoints");
            points.InsertArrayElementAtIndex(points.arraySize);
            var element = points.GetArrayElementAtIndex(points.arraySize - 1);
            element.FindPropertyRelative("SpawnId").stringValue = spawnId;
            element.FindPropertyRelative("Definition").objectReferenceValue = definition;
            element.FindPropertyRelative("Anchor").objectReferenceValue = anchor;
            so.ApplyModifiedPropertiesWithoutUndo();
            return anchor;
        }

        private void Set(Action<SerializedObject> edit) { var so = new SerializedObject(location); edit(so); so.ApplyModifiedPropertiesWithoutUndo(); }
        private List<ValidationResult> Run(IEnumerable<LocationContent> others = null, IEnumerable<Transform> origins = null) => LocationValidator.Validate(location, nav, others, origins);
        private static List<ValidationResult> Where(List<ValidationResult> r, string messagePart) => r.Where(x => x.Message.Contains(messagePart)).ToList();

        [Test]
        public void ACleanLocationHasNoFindings() => Assert.IsEmpty(Run(), string.Join("\n", Run()));

        [Test]
        public void AnEmptySpawnIdIsAnErrorForThatRowOnly()
        {
            Set(so => so.FindProperty("spawnPoints").GetArrayElementAtIndex(1).FindPropertyRelative("SpawnId").stringValue = "");
            var results = Run();
            var found = Where(results, "empty").Single();
            Assert.AreEqual(ValidationSeverity.Error, found.Severity); StringAssert.Contains("spawnPoints[1]", found.Field);
            Assert.AreEqual(1, results.Count, string.Join("\n", results));
        }

        [Test]
        public void ADuplicateSpawnIdInTheSameLocationIsAnErrorOnTheLaterRow()
        {
            Set(so => so.FindProperty("spawnPoints").GetArrayElementAtIndex(1).FindPropertyRelative("SpawnId").stringValue = "smith");
            var found = Where(Run(), "duplicate").Single();
            Assert.AreEqual(ValidationSeverity.Error, found.Severity); StringAssert.Contains("spawnPoints[1]", found.Field);
        }

        [Test]
        public void ADuplicateSpawnIdInAnotherOpenLocationIsAnError()
        {
            var other = new GameObject("Other").AddComponent<LocationContent>(); made.Add(other.gameObject);
            Add(other, "smith", smith, Vector3.one * 50f);
            var found = Where(Run(new[] { other }), "another location").Single();
            Assert.AreEqual(ValidationSeverity.Error, found.Severity); StringAssert.Contains("spawnPoints[0]", found.Field);
            Assert.IsEmpty(Run(new[] { location }), "the location itself is not 'another location'");
        }

        [Test]
        public void AMissingDefinitionOrAMissingVisualPrefabIsAnError()
        {
            Set(so => so.FindProperty("spawnPoints").GetArrayElementAtIndex(0).FindPropertyRelative("Definition").objectReferenceValue = null);
            Set(so => so.FindProperty("spawnPoints").GetArrayElementAtIndex(1).FindPropertyRelative("Definition").objectReferenceValue = Npc("npc-bare", withVisual: false));
            var results = Run();
            Assert.AreEqual(ValidationSeverity.Error, Where(results, "no NpcDefinition").Single().Severity);
            Assert.AreEqual(ValidationSeverity.Error, Where(results, "visual prefab").Single().Severity);
            Assert.AreEqual(2, results.Count, string.Join("\n", results));
        }

        [Test]
        public void AMissingAnchorIsAnErrorBecauseTheLocationInitializerWouldThrow()
        {
            Set(so => so.FindProperty("spawnPoints").GetArrayElementAtIndex(0).FindPropertyRelative("Anchor").objectReferenceValue = null);
            var found = Run().Single();
            Assert.AreEqual(ValidationSeverity.Error, found.Severity); StringAssert.Contains("anchor", found.Message.ToLowerInvariant());
        }

        [Test]
        public void AnAnchorThatIsAPrefabAssetIsAnError()
        {
            Directory.CreateDirectory(TempFolder);
            var source = new GameObject("PrefabAnchor"); made.Add(source);
            var prefab = PrefabUtility.SaveAsPrefabAsset(source, TempFolder + "/PrefabAnchor.prefab");
            Set(so => so.FindProperty("spawnPoints").GetArrayElementAtIndex(0).FindPropertyRelative("Anchor").objectReferenceValue = prefab.transform);
            var found = Where(Run(), "prefab asset").Single();
            Assert.AreEqual(ValidationSeverity.Error, found.Severity);
        }

        [Test]
        public void AnAnchorInAnotherSceneIsAnError()
        {
            var other = EditorSceneManager.NewPreviewScene();   // an additive scene cannot be created next to an unsaved untitled one
            try
            {
                var stray = new GameObject("StrayAnchor");
                SceneManager.MoveGameObjectToScene(stray, other);
                Set(so => so.FindProperty("spawnPoints").GetArrayElementAtIndex(0).FindPropertyRelative("Anchor").objectReferenceValue = stray.transform);
                var found = Where(Run(), "scene").Single();
                Assert.AreEqual(ValidationSeverity.Error, found.Severity);
            }
            finally { EditorSceneManager.ClosePreviewScene(other); }
        }

        [Test]
        public void AnAnchorOffTheWalkableSurfaceIsAWarning()
        {
            nav.Snap = p => p.x > 1f ? null : p;
            var found = Run().Single();
            Assert.AreEqual(ValidationSeverity.Warning, found.Severity); StringAssert.Contains("walkable", found.Message);
        }

        [Test]
        public void AnAnchorUnreachableFromAnOriginIsAWarningAndNoOriginMeansNoReachabilityCheck()
        {
            var origin = new GameObject("PartyStart").transform; made.Add(origin.gameObject); origin.position = new Vector3(-20f, 0f, 0f);
            nav.Reach = (a, b) => b.x < 1f;   // the guard at x = 5 sits on an island
            var found = Run(origins: new[] { origin }).Single();
            Assert.AreEqual(ValidationSeverity.Warning, found.Severity); StringAssert.Contains("reach", found.Message);
            Assert.IsEmpty(Run(), "without an origin reachability is not checked");
        }

        [Test]
        public void TwoAnchorsCloserThanEightyCentimetresAreAWarning()
        {
            Set(so => so.FindProperty("spawnPoints").GetArrayElementAtIndex(1).FindPropertyRelative("Anchor").objectReferenceValue = location.SpawnPoints[0].Anchor);
            location.SpawnPoints[0].Anchor.position = Vector3.zero;
            var results = Where(Run(), "apart");
            Assert.AreEqual(1, results.Count); Assert.AreEqual(ValidationSeverity.Warning, results[0].Severity);
        }

        [Test]
        public void AnAnchorOutsideTheCameraBoundsIsAWarningOnlyWhenBoundsAreSet()
        {
            Assert.IsEmpty(Run());
            Set(so => { so.FindProperty("hasCameraBounds").boolValue = true; so.FindProperty("cameraBoundsXZ").rectValue = new Rect(-1f, -1f, 3f, 3f); });
            var found = Run().Single();
            Assert.AreEqual(ValidationSeverity.Warning, found.Severity); StringAssert.Contains("camera bounds", found.Message);
        }

        [Test]
        public void StoreAndLootIdsMustBeUniqueAndNonEmptyAndTheirAdaptersSet()
        {
            Set(so =>
            {
                var stores = so.FindProperty("stores");
                for (var i = 0; i < 3; i++) stores.InsertArrayElementAtIndex(i);
                stores.GetArrayElementAtIndex(0).FindPropertyRelative("StoreId").stringValue = "shop";
                stores.GetArrayElementAtIndex(1).FindPropertyRelative("StoreId").stringValue = "shop";
                stores.GetArrayElementAtIndex(2).FindPropertyRelative("StoreId").stringValue = "";
                var loot = so.FindProperty("lootContainers");
                loot.InsertArrayElementAtIndex(0);
                loot.GetArrayElementAtIndex(0).FindPropertyRelative("ContainerId").stringValue = "chest";
            });
            var results = Run();
            Assert.AreEqual(ValidationSeverity.Error, Where(results, "store id 'shop'").Single().Severity);
            Assert.AreEqual(ValidationSeverity.Error, Where(results, "store id is empty").Single().Severity);
            Assert.AreEqual(4, Where(results, "adapter").Count, "every store and the chest lack an adapter: " + string.Join("\n", results));
            Assert.IsTrue(Where(results, "adapter").All(r => r.Severity == ValidationSeverity.Warning));
            Set(so => so.FindProperty("lootContainers").GetArrayElementAtIndex(0).FindPropertyRelative("ContainerId").stringValue = "");
            Assert.AreEqual(ValidationSeverity.Error, Where(Run(), "container id is empty").Single().Severity);
        }

        [Test]
        public void AReservedPartyPrefixOnASpawnIdIsAWarning()
        {
            Set(so => so.FindProperty("spawnPoints").GetArrayElementAtIndex(0).FindPropertyRelative("SpawnId").stringValue = "hero-smith");
            var found = Run().Single();
            Assert.AreEqual(ValidationSeverity.Warning, found.Severity); StringAssert.Contains("hero-", found.Message);
        }

        [Test]
        public void EveryFindingNamesItsObjectSoAResultCanSelectIt()
        {
            Set(so => so.FindProperty("spawnPoints").GetArrayElementAtIndex(0).FindPropertyRelative("Definition").objectReferenceValue = null);
            var found = Run().Single();
            Assert.IsNotNull(found.Context, "the context is the anchor (or the location host when there is no anchor)");
            StringAssert.Contains("Location", found.AssetPath);
        }

        [Test]
        public void WithoutANavigationServiceTheSurfaceRulesAreSkippedButTheDataRulesStillRun()
        {
            nav.Snap = p => null;
            Assert.IsNotEmpty(Run());
            Assert.IsEmpty(LocationValidator.Validate(location, null, null, null), "no navigation service: no off-surface or reachability findings, and this location's data is fine");
            Set(so => so.FindProperty("spawnPoints").GetArrayElementAtIndex(0).FindPropertyRelative("SpawnId").stringValue = "");
            Assert.AreEqual(1, LocationValidator.Validate(location, null, null, null).Count);
        }
    }
}
