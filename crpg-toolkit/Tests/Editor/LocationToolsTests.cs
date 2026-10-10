using System;
using System.Collections.Generic;
using System.Linq;
using CRPG.Actors;
using CRPG.Editor.Location;
using CRPG.Editor.Validation;
using CRPG.Gameplay.Scenes;
using CRPG.Navigation;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.UIElements;

namespace CRPG.Tests.EditMode
{
    /// <summary>The testable parts around the scene-view tool: the spawn point drawer, the walkable-ray rule of place mode, the picked definition, the open-locations command and the gizmo heights. The interactive drawing itself is checked by hand.</summary>
    public sealed class LocationToolsTests
    {
        private sealed class FakeNav : INavMeshService
        {
            public Func<Vector3, bool> Walkable = p => true;
            public Func<Vector3, Vector3, bool> Reach = (a, b) => true;
            public bool TrySamplePosition(Vector3 point, out Vector3 sampledPosition, float maxDistance = 5f, int areaMask = NavMesh.AllAreas) { sampledPosition = point; return Walkable(point); }
            public bool TryCalculatePathLength(Vector3 from, Vector3 to, out float length, int areaMask = NavMesh.AllAreas) { length = 0f; return Reach(from, to); }
            public bool TryCalculatePath(Vector3 from, Vector3 to, out Vector3[] corners, int areaMask = NavMesh.AllAreas) { corners = Array.Empty<Vector3>(); return Reach(from, to); }
        }

        private readonly List<UnityEngine.Object> made = new();
        private LocationContent location;
        private GameObject visual;

        [SetUp]
        public void SetUp()
        {
            visual = new GameObject("visual"); made.Add(visual);
            var root = new GameObject("CRPG"); made.Add(root);
            var host = new GameObject("Location"); host.transform.SetParent(root.transform, false);
            location = host.AddComponent<LocationContent>();
        }

        [TearDown]
        public void TearDown()
        {
            foreach (var o in made) if (o != null) UnityEngine.Object.DestroyImmediate(o);
            made.Clear();
            SessionState.EraseString("CRPG.PlaceNpc.Definition");
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

        [Test]
        public void TheDrawerBuildsThreeFieldsThreeButtonsAndAStatusDotThatReflectsTheValidator()
        {
            LocationPlacement.AddSpawn(location, Npc("npc-smith"), Vector3.zero, Quaternion.identity);
            made.Add(location.SpawnPoints[0].Anchor.parent.gameObject);
            var so = new SerializedObject(location);
            var element = new NpcSpawnPointDrawer().CreatePropertyGUI(so.FindProperty("spawnPoints").GetArrayElementAtIndex(0));
            foreach (var name in new[] { "spawn-select", "spawn-frame", "spawn-snap", "spawn-npc", "spawn-dialogue" }) Assert.IsNotNull(element.Q<Button>(name), name);
            var npcAsset = location.SpawnPoints[0].Definition;
            Selection.activeObject = null;
            NpcSpawnPointDrawer.SelectNpc(so, "spawnPoints.Array.data[0]");
            Assert.AreSame(npcAsset, Selection.activeObject, "the NPC button selects the NpcDefinition so its inspector (appearance creator, stats) opens");
            Selection.activeObject = null;
            Assert.AreEqual(3, element.Query<PropertyField>().ToList().Count, "Spawn id, NPC, Anchor");
            var status = element.Q<Label>("spawn-status");
            Assert.Greater(status.style.color.value.g, 0.5f, "not the red of an Error (the test scene has no NavMesh, so the row is at most a yellow off-surface Warning)");
            if (status.style.color.value.r > 0.9f && status.style.color.value.g < 0.8f) StringAssert.Contains("walkable", status.tooltip);

            LocationPlacement.AddSpawn(location, Npc("npc-smith"), Vector3.one, Quaternion.identity, "bad");
            var so2 = new SerializedObject(location);
            so2.FindProperty("spawnPoints").GetArrayElementAtIndex(1).FindPropertyRelative("SpawnId").stringValue = "";
            so2.ApplyModifiedPropertiesWithoutUndo();
            var bad = new NpcSpawnPointDrawer().CreatePropertyGUI(new SerializedObject(location).FindProperty("spawnPoints").GetArrayElementAtIndex(1));
            var red = bad.Q<Label>("spawn-status").style.color.value;
            Assert.Greater(red.r, red.g, "an Error row is red");
            StringAssert.Contains("empty", bad.Q<Label>("spawn-status").tooltip);
        }

        [Test]
        public void PlaceModeTakesTheFirstHitThatSnapsOntoWalkableGroundSoARoofDoesNotSwallowTheClick()
        {
            var ground = GameObject.CreatePrimitive(PrimitiveType.Cube); made.Add(ground);
            ground.transform.position = new Vector3(100f, -0.5f, 100f); ground.transform.localScale = new Vector3(20f, 1f, 20f);
            var roof = GameObject.CreatePrimitive(PrimitiveType.Cube); made.Add(roof);
            roof.transform.position = new Vector3(100f, 5f, 100f); roof.transform.localScale = new Vector3(4f, 0.2f, 4f);
            Physics.SyncTransforms();
            var ray = new Ray(new Vector3(100f, 20f, 100f), Vector3.down);
            var nav = new FakeNav { Walkable = p => p.y < 1f };   // only the ground is walkable, the roof is not
            var point = PlaceNpcTool.WalkablePointAlong(ray, ~0, nav);
            Assert.IsTrue(point.HasValue); Assert.AreEqual(0f, point.Value.y, 1e-3f, "the point is on the ground below the roof");
            Assert.IsFalse(PlaceNpcTool.WalkablePointAlong(ray, ~0, new FakeNav { Walkable = p => false }).HasValue, "nothing walkable: no placement");
            var roofOnly = 1 << 30;
            roof.layer = 30;
            Assert.IsFalse(PlaceNpcTool.WalkablePointAlong(ray, roofOnly, nav).HasValue, "the layer mask is honoured: the roof layer only, and the roof is not walkable");
        }

        [Test]
        public void ThePlaceToolPlacesIntoTheSelectedLocationElseItsSceneElseAnyOpenOne()
        {
            var rootB = new GameObject("CRPG_B"); made.Add(rootB);
            var hostB = new GameObject("Location"); hostB.transform.SetParent(rootB.transform, false);
            var locationB = hostB.AddComponent<LocationContent>();
            var unrelated = new GameObject("Unrelated"); made.Add(unrelated);
            try
            {
                Selection.activeGameObject = hostB;
                Assert.AreSame(locationB, PlaceNpcTool.ResolveLocation(), "the selected host");
                Selection.activeGameObject = rootB;
                Assert.AreSame(locationB, PlaceNpcTool.ResolveLocation(), "a root that holds a location below it");
                Selection.activeGameObject = unrelated;
                Assert.IsNotNull(PlaceNpcTool.ResolveLocation(), "an unrelated object in the same scene: a location of that scene");
                Selection.activeObject = null;
                Assert.IsNotNull(PlaceNpcTool.ResolveLocation(), "nothing selected: the first open location");
            }
            finally { Selection.activeObject = null; }
        }

        [Test]
        public void ActivatingThePlaceToolFromTheMenuNeverThrowsWhateverTheSelection()
        {
            // regression: the tool was a component tool bound to LocationContent and SetActiveTool threw "the current selection does not contain any objects editable by the component tool"
            var unrelated = new GameObject("Unrelated"); made.Add(unrelated);
            try
            {
                foreach (var selection in new GameObject[] { null, unrelated, location.gameObject })
                {
                    Selection.activeGameObject = selection;
                    Assert.DoesNotThrow(LocationMenu.ActivatePlaceTool, selection == null ? "no selection" : selection.name);
                }

                Assert.AreEqual(typeof(PlaceNpcTool), UnityEditor.EditorTools.ToolManager.activeToolType, "the global tool really became the active tool");
            }
            finally { Selection.activeObject = null; UnityEditor.Tools.current = UnityEditor.Tool.Move; }
        }

        [Test]
        public void ThePickedDefinitionSurvivesInSessionStateAsAGuid()
        {
            Assert.IsNull(PlaceNpcTool.Picked);
            var asset = ScriptableObject.CreateInstance<NpcDefinition>();
            var path = "Assets/__LocationToolsTests_Npc.asset";
            AssetDatabase.CreateAsset(asset, path);
            try
            {
                PlaceNpcTool.Picked = asset;
                Assert.AreEqual(AssetDatabase.AssetPathToGUID(path), SessionState.GetString("CRPG.PlaceNpc.Definition", ""));
                Assert.AreSame(asset, PlaceNpcTool.Picked);
                PlaceNpcTool.Picked = null;
                Assert.IsNull(PlaceNpcTool.Picked);
            }
            finally { AssetDatabase.DeleteAsset(path); }
        }

        [Test]
        public void ValidateOpenLocationsChecksEveryLocationAgainstTheOthersAndTheSceneTravelDestinations()
        {
            LocationPlacement.AddSpawn(location, Npc("npc-smith"), new Vector3(10f, 0f, 0f), Quaternion.identity);
            made.Add(location.SpawnPoints[0].Anchor.parent.gameObject);
            var other = new GameObject("Other").AddComponent<LocationContent>(); made.Add(other.gameObject);
            LocationPlacement.AddSpawn(other, Npc("npc-smith"), new Vector3(50f, 0f, 0f), Quaternion.identity, "smith");
            made.Add(other.SpawnPoints[0].Anchor.parent.gameObject);

            var duplicate = LocationMenu.ValidateOpenLocations(new FakeNav()).Where(r => r.Message.Contains("another location")).ToList();
            Assert.AreEqual(2, duplicate.Count, "each of the two locations reports the id they share");
            Assert.IsTrue(duplicate.All(r => r.Severity == ValidationSeverity.Error));

            var arrival = new GameObject("Arrival").AddComponent<SceneTravelDestination>(); made.Add(arrival.gameObject);
            arrival.transform.position = new Vector3(-30f, 0f, 0f);
            var unreachable = LocationMenu.ValidateOpenLocations(new FakeNav { Reach = (a, b) => false }).Where(r => r.Message.Contains("reached")).ToList();
            Assert.AreEqual(2, unreachable.Count, "with an arrival point in the scene the reachability rule runs for every anchor");
        }

        [Test]
        public void TheGizmoHeightComesFromTheVisualPrefabsMeshes()
        {
            Assert.AreEqual(LocationGizmos.DefaultHeight, LocationGizmos.HeightOf(null));
            Assert.AreEqual(LocationGizmos.DefaultHeight, LocationGizmos.HeightOf(Npc("npc-empty-visual")), "a prefab with no meshes: the default");
            var tall = GameObject.CreatePrimitive(PrimitiveType.Cube); made.Add(tall);
            tall.transform.localScale = new Vector3(0.5f, 2.4f, 0.5f); tall.transform.position = new Vector3(0f, 1.2f, 0f);
            var def = ScriptableObject.CreateInstance<NpcDefinition>(); made.Add(def);
            var so = new SerializedObject(def); so.FindProperty("visualPrefab").objectReferenceValue = tall; so.ApplyModifiedPropertiesWithoutUndo();
            Assert.AreEqual(1f, LocationGizmos.HeightOf(def), 1e-3f, "the height is the MESH extent (a unit cube is 1 tall), not the scaled instance: a prefab asset's renderer bounds are not reliable");
        }
    }
}
