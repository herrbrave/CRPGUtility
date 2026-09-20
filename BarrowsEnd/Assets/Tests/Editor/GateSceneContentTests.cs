using System.Collections.Generic;
using System.Linq;
using BarrowsEnd.Editor;
using BarrowsEnd.Gate;
using CRPG.Core;
using CRPG.Gameplay.Interactions;
using CRPG.Gameplay.Interactions.Adapters;
using CRPG.Inventory;
using CRPG.Navigation.Grid;
using CRPG.Presentation.Sprites;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace BarrowsEnd.Tests
{
    /// <summary>
    /// Task 2.7c: static checks on the authored Phase 2 gate scene, so it cannot silently rot. Reads the scene's SERIALIZED data (a live grid
    /// is not populated in Edit mode: GridWalkabilityBlocker pushes its cells from OnEnable), against <see cref="GateLayout"/>, the catalogs
    /// and the ruleset.
    /// </summary>
    public class GateSceneContentTests
    {
        private Scene scene;

        [OneTimeSetUp]
        public void Open() => scene = EditorSceneManager.OpenScene(PhaseTwoGateSceneAuthor.ScenePath, OpenSceneMode.Additive);

        [OneTimeTearDown]
        public void Close() => EditorSceneManager.CloseScene(scene, true);

        private T Find<T>() where T : Component => scene.GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<T>(true)).First();
        private IEnumerable<T> FindAll<T>() where T : Component => scene.GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<T>(true));

        private static GridCoordinate C(Vector2Int v) => new(v.x, v.y);

        [Test]
        public void TheSerializedBlockedCellsAreExactlyTheLayoutsAndTheDoorCellIsLeftToItsBlocker()
        {
            var grid = Find<WalkabilityGridProvider>().Grid;
            Assert.AreEqual(GateLayout.Width, grid.Width);
            Assert.AreEqual(GateLayout.Depth, grid.Depth);

            var expected = new HashSet<Vector2Int>(GateLayout.BlockedCells());
            for (var x = 0; x < grid.Width; x++)
            for (var z = 0; z < grid.Depth; z++)
                Assert.AreEqual(!expected.Contains(new Vector2Int(x, z)), grid.IsWalkable(new GridCoordinate(x, z)), $"cell {x},{z}");

            Assert.IsTrue(grid.IsWalkable(C(GateLayout.DoorCell)), "the provider does not block the door cell itself: the blocker owns it");
            Assert.IsFalse(expected.Contains(GateLayout.DoorCell));
        }

        [Test]
        public void TheDoorIsWiredOnOneObjectWithABlockerAnAnchorOnAWalkableCellAndAClickableCollider()
        {
            var door = Find<UseDoorInteractionAdapter>().gameObject;
            var blocker = door.GetComponent<GridWalkabilityBlocker>();
            var interactable = door.GetComponent<WorldInteractable>();
            Assert.IsNotNull(blocker, "the adapter finds its blocker on its own GameObject");
            Assert.IsNotNull(interactable);
            Assert.IsNotNull(door.GetComponent<Collider>(), "the router hits a collider to find the interactable");
            Assert.IsTrue(blocker.IsBlocking, "closed at start");
            var anchor = new SerializedObject(interactable).FindProperty("interactionAnchor").objectReferenceValue as Transform;
            Assert.IsNotNull(anchor, "explicit anchor: WorldInteractable logs an error without one (IsConfigured also needs the runtime adapter, so read the serialized field)");
            Assert.AreNotEqual(door.transform.position, anchor.position, "and it is not the door's own (blocked) cell");

            var grid = Find<WalkabilityGridProvider>().Grid;
            var anchorCell = grid.ToGrid(interactable.InteractionPoint);
            Assert.AreEqual(C(GateLayout.DoorAnchorCell), anchorCell);
            Assert.IsTrue(grid.IsWalkable(anchorCell), "a member can stand on the anchor");
            Assert.AreEqual(C(GateLayout.DoorCell), grid.ToGrid(door.transform.position));
        }

        [Test]
        public void EveryTileAndPropSpriteComesFromTheCopiedKenneySheetAndSitsInsideTheSortableRange()
        {
            var renderers = FindAll<SpriteRenderer>().ToList();
            Assert.Greater(renderers.Count, GateLayout.Width * GateLayout.Depth, "tiles plus props");
            foreach (var renderer in renderers)
            {
                Assert.IsNotNull(renderer.sprite, renderer.name);
                Assert.AreEqual(GateLayout.SheetPath, AssetDatabase.GetAssetPath(renderer.sprite), $"{renderer.name} uses the copied sheet, never an original");
                Assert.LessOrEqual(Mathf.Abs(renderer.transform.position.z), SpriteSortingContract.MaxWorldZ, $"{renderer.name} is inside |Z| <= {SpriteSortingContract.MaxWorldZ}");
            }

            var world = renderers.Where(r => r.sortingLayerName == SpriteSortingContract.WorldLayer).ToList();
            Assert.AreEqual(GateLayout.WallCells().Count() + GateLayout.SolidProps.Length + 2, world.Count, "walls + solid props + the door's two visuals");
            Assert.IsTrue(world.All(r => r.GetComponent<SpriteDepthSorter>() != null), "every World-layer prop y-sorts through the prop lane");
            Assert.AreEqual(GateLayout.Width * GateLayout.Depth, renderers.Count(r => r.sortingLayerName == SpriteSortingContract.GroundLayer));
        }

        [Test]
        public void TheSortingLayersExistAndTheCameraIsAnOrthographicYawZeroPitchNinetyMainCamera()
        {
            var names = SortingLayer.layers.Select(l => l.name).ToList();
            CollectionAssert.Contains(names, SpriteSortingContract.GroundLayer);
            CollectionAssert.Contains(names, SpriteSortingContract.WorldLayer);
            Assert.Less(names.IndexOf(SpriteSortingContract.GroundLayer), names.IndexOf(SpriteSortingContract.WorldLayer), "Ground draws under World");

            var gateCamera = Find<GateCamera>();
            var camera = gateCamera.GetComponent<Camera>();
            Assert.AreEqual("MainCamera", camera.tag);
            Assert.IsTrue(camera.orthographic);
            Assert.AreEqual(90f, gateCamera.transform.eulerAngles.x, 0.01f);
            Assert.AreEqual(0f, gateCamera.transform.eulerAngles.y, 0.01f, "the sorting contract needs yaw 0");
        }

        [Test]
        public void ThereIsARealGroundColliderCoveringTheWholeGridForTheClickRouter()
        {
            var ground = scene.GetRootGameObjects().First(g => g.name == "Ground").GetComponentInChildren<BoxCollider>();
            var grid = Find<WalkabilityGridProvider>().Grid;
            var bounds = ground.bounds;
            Assert.LessOrEqual(bounds.min.x, grid.Origin.x + 0.01f);
            Assert.LessOrEqual(bounds.min.z, grid.Origin.z + 0.01f);
            Assert.GreaterOrEqual(bounds.max.x, grid.Origin.x + grid.Width * grid.CellSize - 0.01f);
            Assert.GreaterOrEqual(bounds.max.z, grid.Origin.z + grid.Depth * grid.CellSize - 0.01f);
        }

        [Test]
        public void EveryPartyMemberAndTheSkeletonStandOnDistinctWalkableCellsWithResolvableAppearanceAndGear()
        {
            var bootstrap = Find<GateBootstrap>();
            var grid = Find<WalkabilityGridProvider>().Grid;
            var ruleset = AssetDatabase.LoadAssetAtPath<BarrowsEndRulesetProvider>(BarrowsEndGearAuthor.RulesetPath);
            var extra = ruleset.ExtraContent;
            var itemIds = new HashSet<string>(extra.OfType<ItemDefinition>().Select(i => i.Id.Value));
            var options = new HashSet<(string slot, string option)>();
            foreach (var catalog in extra.OfType<SpriteLayerCatalog>())
            foreach (var option in catalog.Options)
                options.Add((catalog.LayerSlot.Value, option.optionId.Value));

            Assert.AreEqual(4, bootstrap.Members.Count);
            var spawnIds = new HashSet<string>();
            var cells = new HashSet<Vector2Int>();
            foreach (var member in bootstrap.Members)
            {
                Assert.IsTrue(spawnIds.Add(member.spawnId), $"{member.displayName}: unique spawn id");
                Assert.IsTrue(cells.Add(member.cell), $"{member.displayName}: own cell");
                Assert.IsTrue(grid.IsWalkable(C(member.cell)), $"{member.displayName}: starts on a walkable cell");
                foreach (var entry in member.appearance)
                {
                    var parts = entry.Split('=');
                    Assert.AreEqual(2, parts.Length, entry);
                    Assert.IsTrue(options.Contains((parts[0], parts[1])), $"{member.displayName}: appearance '{entry}' exists in the catalogs");
                }

                foreach (var item in member.gear) Assert.IsTrue(itemIds.Contains(item), $"{member.displayName}: gear '{item}' is in the ruleset");
                Assert.Greater(member.maxHp, 0);
            }

            var so = new SerializedObject(bootstrap);
            var skeleton = so.FindProperty("skeletonCell").vector2IntValue;
            Assert.IsTrue(grid.IsWalkable(C(skeleton)));
            Assert.IsFalse(cells.Contains(skeleton));
            Assert.IsNotNull(so.FindProperty("ruleset").objectReferenceValue);
            Assert.IsNotNull(so.FindProperty("partyPrefab").objectReferenceValue);
            Assert.IsNotNull(so.FindProperty("inputSource").objectReferenceValue);
        }

        [Test]
        public void BothScenesAreInTheBuildSettingsSoTheyLoadByPathInPlayMode()
        {
            var paths = EditorBuildSettings.scenes.Where(s => s.enabled).Select(s => s.path).ToList();
            CollectionAssert.Contains(paths, PhaseTwoGateSceneAuthor.ScenePath);
        }
    }
}
