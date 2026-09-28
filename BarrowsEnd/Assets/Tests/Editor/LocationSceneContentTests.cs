using System.Collections.Generic;
using System.Linq;
using BarrowsEnd.Editor;
using BarrowsEnd.Shell;
using CRPG.Editor.Location;
using CRPG.Gameplay.Interactions;
using CRPG.Gameplay.Interactions.Adapters;
using CRPG.Gameplay.Scenes;
using CRPG.InputSystem;
using CRPG.Navigation.Grid;
using CRPG.Navigation.Grid.Tilemaps;
using CRPG.Party;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.Tilemaps;

namespace BarrowsEnd.Tests
{
    /// <summary>Task 3.4: the two demo location scenes and the Core scene, checked as authored data (no play loop).</summary>
    public class LocationSceneContentTests
    {
        // Authoring never overwrites an existing location scene, so hand-painted changes to the demo maps survive these tests.
        [OneTimeSetUp]
        public void Author() => BarrowsEndCoreSceneAuthor.Author();

        private static Scene OpenAdditive(string path) => EditorSceneManager.OpenScene(path, OpenSceneMode.Additive);

        private static GameObject RootOf(Scene scene) => scene.GetRootGameObjects().First(g => g.GetComponentInChildren<Grid>(true) != null);

        private static IEnumerable<(string path, int width, int depth)> Locations()
        {
            yield return (BarrowsEndLocationAuthor.TownPath, BarrowsEndLocationAuthor.TownWidth, BarrowsEndLocationAuthor.TownDepth);
            yield return (BarrowsEndLocationAuthor.CavePath, BarrowsEndLocationAuthor.CaveWidth, BarrowsEndLocationAuthor.CaveDepth);
        }

        [Test]
        public void EachLocationIsBakedWithItsGridBoundsAndColliderLayers()
        {
            foreach (var (path, width, depth) in Locations())
            {
                var scene = OpenAdditive(path);
                try
                {
                    var root = RootOf(scene);
                    var grid = root.GetComponentInChildren<WalkabilityGridProvider>(true).Grid;
                    Assert.AreEqual(width, grid.Width, path);
                    Assert.AreEqual(depth, grid.Depth, path);
                    Assert.AreEqual(Vector3.zero, grid.Origin);
                    Assert.AreEqual(new Rect(0f, 0f, width, depth), root.GetComponent<LocationContent>().CameraBoundsXZ, path);
                    Assert.AreEqual(BarrowsEndLayers.Ground, root.transform.Find("Baked/Ground").gameObject.layer);
                    var walls = root.transform.Find("Baked/Walls").GetComponentsInChildren<BoxCollider>();
                    Assert.Greater(walls.Length, 0);
                    Assert.IsTrue(walls.All(w => w.gameObject.layer == BarrowsEndLayers.WorldObstacle));
                }
                finally
                {
                    EditorSceneManager.CloseScene(scene, true);
                }
            }
        }

        [Test]
        public void ReBakingEachLocationIsCleanWithNoWarningsSoNothingIsUnreachableOrFloating()
        {
            foreach (var (path, _, _) in Locations())
            {
                var scene = OpenAdditive(path);
                try
                {
                    var report = LocationBaker.Bake(RootOf(scene), BarrowsEndLocationBake.Options());
                    Assert.IsTrue(report.Baked, path);
                    Assert.IsEmpty(report.Warnings.Select(w => w.ToString()).ToList(), path);
                }
                finally
                {
                    EditorSceneManager.CloseScene(scene, true);
                }
            }
        }

        [Test]
        public void EveryArrivalAnchorHasFourWalkableFormationSlots()
        {
            foreach (var (path, _, _) in Locations())
            {
                var scene = OpenAdditive(path);
                try
                {
                    var grid = RootOf(scene).GetComponentInChildren<WalkabilityGridProvider>(true).Grid;
                    var pathfinding = new GridPathfindingService();
                    pathfinding.SetActiveGrid(grid);
                    var destinations = scene.GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<SceneTravelDestination>(true)).ToList();
                    Assert.Greater(destinations.Count, 0, path);
                    foreach (var destination in destinations)
                    {
                        Assert.IsTrue(grid.IsWalkableWorld(destination.Anchor.position), $"{destination.DestinationId} stands on walkable ground");
                        var wanted = new WedgeFormationSolver().Solve(destination.Anchor.position, destination.Anchor.forward, 4, 1.5f);
                        var claimed = new List<Vector3>();
                        foreach (var slot in wanted)
                        {
                            Assert.IsTrue(pathfinding.TrySamplePositionAvoiding(slot, out var spot, 6f, claimed), $"{destination.DestinationId}: a slot near {slot}");
                            claimed.Add(spot);
                        }

                        Assert.AreEqual(4, claimed.Select(c => pathfinding.ActiveGrid.ToGrid(c)).Distinct().Count(), "four different cells");
                    }
                }
                finally
                {
                    EditorSceneManager.CloseScene(scene, true);
                }
            }
        }

        [Test]
        public void TheTravelPointsAreWiredAndEachLeadsToARealDestinationInTheOtherScene()
        {
            var pairs = new[]
            {
                (BarrowsEndLocationAuthor.TownPath, BarrowsEndLocationAuthor.CavePath, BarrowsEndLocationAuthor.CaveFromTown),
                (BarrowsEndLocationAuthor.CavePath, BarrowsEndLocationAuthor.TownPath, BarrowsEndLocationAuthor.TownFromCave),
            };

            foreach (var (from, to, destinationId) in pairs)
            {
                var scene = OpenAdditive(from);
                var target = OpenAdditive(to);
                try
                {
                    var point = scene.GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<TravelInteractionAdapter>(true)).Single();
                    var so = new SerializedObject(point);
                    Assert.AreEqual(to, so.FindProperty("targetScenePath").stringValue);
                    Assert.AreEqual(destinationId, so.FindProperty("destinationId").stringValue);
                    Assert.IsNotNull(point.GetComponent<Collider>(), "the router hits a collider to find the interactable");
                    var anchor = new SerializedObject(point.GetComponent<WorldInteractable>()).FindProperty("interactionAnchor").objectReferenceValue as Transform;
                    Assert.IsNotNull(anchor);
                    var grid = RootOf(scene).GetComponentInChildren<WalkabilityGridProvider>(true).Grid;
                    Assert.IsTrue(grid.IsWalkableWorld(anchor.position), "the spot you stand on to travel is walkable");

                    var arrival = target.GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<SceneTravelDestination>(true)).Any(d => d.DestinationId == destinationId);
                    Assert.IsTrue(arrival, $"'{to}' has destination '{destinationId}'");
                }
                finally
                {
                    EditorSceneManager.CloseScene(scene, true);
                    EditorSceneManager.CloseScene(target, true);
                }
            }
        }

        [Test]
        public void EveryLocationsBakeIsFreshSoNoPaintedChangeIsLeftUnbaked()
        {
            foreach (var (path, _, _) in Locations())
            {
                var scene = OpenAdditive(path);
                try
                {
                    Assert.IsFalse(LocationBaker.IsStale(RootOf(scene), out var message), $"{path}: {message}");
                }
                finally
                {
                    EditorSceneManager.CloseScene(scene, true);
                }
            }
        }

        [Test]
        public void TheTownKeepsItsHouseDoorAtTheSouthWall()
        {
            var town = OpenAdditive(BarrowsEndLocationAuthor.TownPath);
            try
            {
                var doors = new List<Vector3Int>();
                foreach (var map in RootOf(town).GetComponentsInChildren<Tilemap>(true))
                foreach (var pos in map.cellBounds.allPositionsWithin)
                    if (map.GetTile(pos) is SemanticTile tile && tile.Semantic == TileSemantic.Door) doors.Add(pos);
                CollectionAssert.Contains(doors, new Vector3Int(6, 11, 0), "the house door (more doors may have been painted since)");
            }
            finally
            {
                EditorSceneManager.CloseScene(town, true);
            }
        }

        [Test]
        public void TheCoreSceneIsFullyWiredAndAllScenesAreInTheBuildSettingsWithCoreFirst()
        {
            var scene = OpenAdditive(BarrowsEndCoreSceneAuthor.ScenePath);
            try
            {
                var boot = scene.GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<ShellBootstrap>(true)).Single();
                var so = new SerializedObject(boot);
                foreach (var field in new[] { "ruleset", "partyPrefab", "inputSource", "cameraRig", "cameraFollow" })
                    Assert.IsNotNull(so.FindProperty(field).objectReferenceValue, field);
                // Task 6.0: the real Town, not the Phase 3 demo slice -- ShellBootstrap.StartingSceneOverride is
                // what lets ShellTests/ShellCaptureTests/ShellStartFlowTests keep booting Town_Slice on purpose.
                Assert.AreEqual(BarrowsEndTownAuthor.Path, so.FindProperty("startingScenePath").stringValue);
                Assert.AreEqual(BarrowsEndTownAuthor.StartDestination, so.FindProperty("startingDestinationId").stringValue);
                Assert.AreEqual(4, so.FindProperty("members").arraySize);

                var input = scene.GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<InputSystemCommandSource>(true)).Single();
                var inputSo = new SerializedObject(input);
                var mask = (int)inputSo.FindProperty("hittableMask.m_Bits").uintValue;
                Assert.AreEqual(0, mask & BarrowsEndLayers.ObstacleMask, "the router never hits a baked wall box");
                Assert.AreNotEqual(0, mask & BarrowsEndLayers.GroundMask, "but it hits the ground plane (and everything else)");
                Assert.AreEqual(BarrowsEndLayers.RouterMask, mask);
                Assert.IsNotNull(inputSo.FindProperty("cameraRig").objectReferenceValue, "zoom input reaches the rig");
            }
            finally
            {
                EditorSceneManager.CloseScene(scene, true);
            }

            var paths = EditorBuildSettings.scenes.Where(s => s.enabled).Select(s => s.path).ToList();
            Assert.AreEqual(BarrowsEndCoreSceneAuthor.ScenePath, paths[0], "Core loads first");
            CollectionAssert.IsSupersetOf(paths, new[] { BarrowsEndLocationAuthor.TownPath, BarrowsEndLocationAuthor.CavePath });
            CollectionAssert.IsSupersetOf(paths, new[]
            {
                BarrowsEndTownAuthor.Path, BarrowsEndCavernsAuthor.UpperPath, BarrowsEndCavernsAuthor.MiddlePath, BarrowsEndCavernsAuthor.DeepPath,
            });
        }
    }
}
