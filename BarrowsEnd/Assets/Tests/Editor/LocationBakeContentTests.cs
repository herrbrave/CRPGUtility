using System.Linq;
using BarrowsEnd.Editor;
using CRPG.Editor.Location;
using CRPG.Gameplay.Scenes;
using CRPG.Navigation.Grid;
using CRPG.Navigation.Grid.Tilemaps;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.Tilemaps;

namespace BarrowsEnd.Tests
{
    /// <summary>Task 3.3: the location baker with Barrow's End's real physics layers and REAL generated tiles (grass, a wall, a tree, water and a door).</summary>
    public class LocationBakeContentTests
    {
        private GameObject root;

        [OneTimeSetUp]
        public void Author() => BarrowsEndTileAuthor.Author();

        [TearDown]
        public void TearDown()
        {
            if (root != null) Object.DestroyImmediate(root);
        }

        private static SemanticTile Tile(string key, int col, int row) => AssetDatabase.LoadAssetAtPath<SemanticTile>(BarrowsEndTileAuthor.TilePath(key, col, row));

        [Test]
        public void TheProjectDeclaresTheGroundAndObstacleLayersTheGameUses()
        {
            Assert.AreEqual("Ground", LayerMask.LayerToName(BarrowsEndLayers.Ground));
            Assert.AreEqual("WorldObstacle", LayerMask.LayerToName(BarrowsEndLayers.WorldObstacle));
            Assert.AreNotEqual(BarrowsEndLayers.Ground, BarrowsEndLayers.WorldObstacle);
            Assert.AreEqual(0, BarrowsEndLayers.RouterMask & BarrowsEndLayers.ObstacleMask, "the router never hits a baked wall box");
            Assert.AreNotEqual(0, BarrowsEndLayers.RouterMask & BarrowsEndLayers.GroundMask, "but it does hit the ground plane");
            Assert.AreEqual(BarrowsEndLayers.ObstacleMask, BarrowsEndLayers.LineOfSightMask);
        }

        [Test]
        public void ARealPaintedRoomBakesWithTheRealLayersAndTheRealTilesMeanWhatTheTableSays()
        {
            root = new GameObject("TownSlice");
            root.AddComponent<Grid>().cellSwizzle = GridLayout.CellSwizzle.XZY;
            Tilemap Map(string name)
            {
                var go = new GameObject(name);
                go.transform.SetParent(root.transform, false);
                var map = go.AddComponent<Tilemap>();
                go.AddComponent<TilemapRenderer>();
                map.orientation = Tilemap.Orientation.XZ;
                return map;
            }

            var ground = Map("Ground");
            var objects = Map("Objects");
            const int w = 8, d = 6;
            for (var x = 0; x < w; x++)
            for (var y = 0; y < d; y++)
                ground.SetTile(new Vector3Int(x, y, 0), x == 7 ? Tile("town", 0, 0) : Tile("town", 5, 0)); // grass, water down the east edge
            for (var x = 0; x < 7; x++) objects.SetTile(new Vector3Int(x, 3, 0), x == 3 ? Tile("town", 32, 1) : Tile("town", 23, 17)); // a wall with a door at x=3
            objects.SetTile(new Vector3Int(1, 1, 0), Tile("town", 13, 9)); // a tree

            var report = LocationBaker.Bake(root, BarrowsEndLocationBake.Options());

            Assert.IsTrue(report.Baked, string.Join("\n", report.Issues));
            var grid = root.GetComponentInChildren<WalkabilityGridProvider>().Grid;
            Assert.IsFalse(grid.IsWalkable(new GridCoordinate(1, 1)), "the tree blocks");
            Assert.IsFalse(grid.IsWalkable(new GridCoordinate(7, 2)), "water blocks");
            Assert.IsFalse(grid.IsWalkable(new GridCoordinate(0, 3)), "the wall blocks");
            Assert.IsTrue(grid.IsWalkable(new GridCoordinate(3, 3)), "the door cell is left to its blocker");
            Assert.IsTrue(grid.IsWalkable(new GridCoordinate(3, 2)));
            Assert.AreEqual(2, report.WallBoxes, "the wall is split by the door into two boxes");
            Assert.AreEqual(1, report.DoorCells);

            var walls = root.transform.Find("Baked/Walls").GetComponentsInChildren<BoxCollider>();
            Assert.IsTrue(walls.All(b => b.gameObject.layer == BarrowsEndLayers.WorldObstacle));
            Assert.AreEqual(BarrowsEndLayers.Ground, root.transform.Find("Baked/Ground").gameObject.layer);
            Assert.AreEqual(new Rect(0f, 0f, w, d), root.GetComponent<LocationContent>().CameraBoundsXZ);

            Physics.SyncTransforms();
            Assert.IsTrue(Physics.Linecast(new Vector3(1.5f, 0f, 2.5f), new Vector3(1.5f, 0f, 4.5f), BarrowsEndLayers.LineOfSightMask), "sight is blocked through the wall");
            Assert.IsFalse(Physics.Linecast(new Vector3(1.5f, 0f, 0.5f), new Vector3(5.5f, 0f, 0.5f), BarrowsEndLayers.LineOfSightMask), "and clear across open grass");
        }
    }
}
