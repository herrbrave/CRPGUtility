using System.Collections.Generic;
using System.Linq;
using CRPG.Editor.Location;
using CRPG.Gameplay.Scenes;
using CRPG.Navigation.Grid;
using CRPG.Navigation.Grid.Tilemaps;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.Tilemaps;

namespace CRPG.Tests.EditMode
{
    /// <summary>2D campaign Task 3.3: LocationBaker (walkability grid, ground plane, merged wall boxes for line of sight, camera bounds, checks, fail-closed
    /// and idempotence) on small painted maps, plus WallMerger and WalkableRegions.</summary>
    public class LocationBakerTests
    {
        private const int GroundLayer = 30;
        private const int ObstacleLayer = 31;

        private readonly List<Object> made = new();
        private readonly Dictionary<TileSemantic, SemanticTile> tiles = new();
        private GameObject root;

        [SetUp]
        public void SetUp()
        {
            foreach (var semantic in new[] { TileSemantic.Ground, TileSemantic.Blocked, TileSemantic.Wall, TileSemantic.Prop, TileSemantic.Decor, TileSemantic.Door })
            {
                var tile = ScriptableObject.CreateInstance<SemanticTile>();
                tile.Configure(semantic, null, null);
                tiles[semantic] = tile;
                made.Add(tile);
            }
        }

        [TearDown]
        public void TearDown()
        {
            foreach (var o in made) if (o != null) Object.DestroyImmediate(o);
            made.Clear();
            tiles.Clear();
            if (root != null) Object.DestroyImmediate(root);
            foreach (var d in Object.FindObjectsByType<SceneTravelDestination>(FindObjectsSortMode.None)) Object.DestroyImmediate(d.gameObject);
        }

        private static LocationBakeOptions Options() => new() { GroundLayer = GroundLayer, ObstacleLayer = ObstacleLayer };

        /// <summary>Paints a map from text rows (the FIRST row is y = 0, the south edge). '.' ground, '#' wall on ground, 'T' prop on ground, '~' water,
        /// 'D' door on ground, 'd' decor on ground, 'v' decor on VOID, 'w' wall on void, ' ' nothing.</summary>
        private Tilemap[] Paint(string[] rows, Vector3? rootPosition = null, GridLayout.CellSwizzle swizzle = GridLayout.CellSwizzle.XZY)
        {
            root = new GameObject("Location");
            root.transform.position = rootPosition ?? Vector3.zero;
            var grid = root.AddComponent<Grid>();
            grid.cellSwizzle = swizzle;

            Tilemap NewMap(string name)
            {
                var go = new GameObject(name);
                go.transform.SetParent(root.transform, false);
                var map = go.AddComponent<Tilemap>();
                go.AddComponent<TilemapRenderer>();
                map.orientation = Tilemap.Orientation.XZ;
                return map;
            }

            var ground = NewMap("Ground");
            var objects = NewMap("Objects");
            for (var y = 0; y < rows.Length; y++)
            for (var x = 0; x < rows[y].Length; x++)
            {
                var cell = new Vector3Int(x, y, 0);
                switch (rows[y][x])
                {
                    case '.': ground.SetTile(cell, tiles[TileSemantic.Ground]); break;
                    case '#': ground.SetTile(cell, tiles[TileSemantic.Ground]); objects.SetTile(cell, tiles[TileSemantic.Wall]); break;
                    case 'T': ground.SetTile(cell, tiles[TileSemantic.Ground]); objects.SetTile(cell, tiles[TileSemantic.Prop]); break;
                    case '~': ground.SetTile(cell, tiles[TileSemantic.Blocked]); break;
                    case 'D': ground.SetTile(cell, tiles[TileSemantic.Ground]); objects.SetTile(cell, tiles[TileSemantic.Door]); break;
                    case 'd': ground.SetTile(cell, tiles[TileSemantic.Ground]); objects.SetTile(cell, tiles[TileSemantic.Decor]); break;
                    case 'v': objects.SetTile(cell, tiles[TileSemantic.Decor]); break;
                    case 'w': objects.SetTile(cell, tiles[TileSemantic.Wall]); break;
                }
            }

            return new[] { ground, objects };
        }

        private static WalkabilityGrid GridOf(GameObject root) => root.GetComponentInChildren<WalkabilityGridProvider>(true).Grid;

        private SceneTravelDestination Destination(string id, Vector3 position)
        {
            var go = new GameObject("Destination " + id);
            go.transform.position = position;
            var d = go.AddComponent<SceneTravelDestination>();
            var so = new SerializedObject(d);
            so.FindProperty("destinationId").stringValue = id;
            so.ApplyModifiedPropertiesWithoutUndo();
            return d;
        }

        // ---------------------------------------------------------------------------------------------
        // Walkability grid
        // ---------------------------------------------------------------------------------------------

        [Test]
        public void TheGridSizeOriginAndBlockedCellsComeFromTheSemanticsWithVoidUnwalkableAndDoorsLeftOpenForTheirBlocker()
        {
            // row y=3 is the top (north) row
            Paint(new[]
            {
                "..T..~",   // y=0
                ".#...v",   // y=1: wall at x=1; decor on void at x=5
                "..D...",   // y=2: a door at x=2
                "......",   // y=3
            });

            var report = LocationBaker.Bake(root, Options());

            Assert.IsTrue(report.Baked, string.Join("\n", report.Issues));
            var grid = GridOf(root);
            Assert.AreEqual(6, grid.Width);
            Assert.AreEqual(4, grid.Depth);
            Assert.AreEqual(Vector3.zero, grid.Origin);
            bool Blocked(int x, int y) => !grid.IsWalkable(new GridCoordinate(x, y));
            Assert.IsTrue(Blocked(2, 0), "prop");
            Assert.IsTrue(Blocked(5, 0), "water");
            Assert.IsTrue(Blocked(1, 1), "wall");
            Assert.IsTrue(Blocked(5, 1), "a decor tile with no ground under it is void: unwalkable");
            Assert.IsFalse(Blocked(2, 2), "the door cell stays unblocked in the provider: its own blocker owns it at runtime");
            Assert.IsFalse(Blocked(0, 0));
            Assert.IsFalse(Blocked(3, 3));
            Assert.AreEqual(1, report.DoorCells);
            Assert.AreEqual(24 - report.BlockedCells, report.WalkableCells);
        }

        [Test]
        public void ARootAwayFromTheOriginGivesAWorldSpaceGridAndCellsAreRelativeToTheMapCorner()
        {
            Paint(new[] { "...", "..." }, rootPosition: new Vector3(40f, 0f, 12f));

            var report = LocationBaker.Bake(root, Options());

            var grid = GridOf(root);
            Assert.AreEqual(new Vector3(40f, 0f, 12f), grid.Origin);
            Assert.AreEqual(new Rect(40f, 12f, 3f, 2f), report.WorldBoundsXZ);
            Assert.IsTrue(grid.IsWalkableWorld(new Vector3(41.5f, 0f, 12.5f)));
            Assert.AreEqual(new GridCoordinate(1, 0), grid.ToGrid(new Vector3(41.5f, 0f, 12.5f)));
        }

        [Test]
        public void PaintingDoesNotHaveToStartAtCellZero()
        {
            // Nothing painted at x 0-1: the bounds start at the first painted cell.
            Paint(new[] { "  ..", "  .." });
            var report = LocationBaker.Bake(root, Options());
            var grid = GridOf(root);
            Assert.AreEqual(2, grid.Width);
            Assert.AreEqual(2, grid.Depth);
            Assert.AreEqual(new Vector3(2f, 0f, 0f), grid.Origin);
            Assert.AreEqual(new RectInt(2, 0, 2, 2), report.CellBounds);
        }

        [Test]
        public void SeveralTilemapsUnifyAndAnyBlockingTileOnACellWins()
        {
            Paint(new[] { "..." });
            var extra = new GameObject("Rocks");
            extra.transform.SetParent(root.transform, false);
            var map = extra.AddComponent<Tilemap>();
            extra.AddComponent<TilemapRenderer>();
            map.orientation = Tilemap.Orientation.XZ;
            map.SetTile(new Vector3Int(1, 0, 0), tiles[TileSemantic.Prop]);

            LocationBaker.Bake(root, Options());

            Assert.IsFalse(GridOf(root).IsWalkable(new GridCoordinate(1, 0)));
            Assert.IsTrue(GridOf(root).IsWalkable(new GridCoordinate(0, 0)));
        }

        // ---------------------------------------------------------------------------------------------
        // Colliders, line of sight, camera bounds
        // ---------------------------------------------------------------------------------------------

        [Test]
        public void WallCellsBecomeMergedBoxesOnTheObstacleLayerThatStraddleTheGroundPlane()
        {
            Paint(new[]
            {
                "#####",
                ".....",
                "##...",
                "##...",
            });

            var report = LocationBaker.Bake(root, Options());

            var boxes = root.transform.Find("Baked/Walls").GetComponentsInChildren<BoxCollider>();
            Assert.AreEqual(2, boxes.Length, "a 5-long run and a 2x2 block, not seven boxes");
            Assert.AreEqual(2, report.WallBoxes);
            Assert.IsTrue(boxes.All(b => b.gameObject.layer == ObstacleLayer));
            foreach (var box in boxes)
            {
                Assert.Less(box.bounds.min.y, 0f, "straddles the ground plane");
                Assert.Greater(box.bounds.max.y, 0f);
            }

            Assert.AreEqual(5f, boxes.Max(b => b.bounds.size.x), 1e-3f);
        }

        [Test]
        public void ALineOfSightAtCharacterHeightCrossesAWallBoxAndIsClearOverOpenFloor()
        {
            Paint(new[]
            {
                "..#..",
                "..#..",
                "..#..",
                ".....",
            });
            LocationBaker.Bake(root, Options());
            Physics.SyncTransforms();
            var obstacles = 1 << ObstacleLayer;

            Assert.IsTrue(Physics.Linecast(new Vector3(0.5f, 0f, 1.5f), new Vector3(4.5f, 0f, 1.5f), obstacles), "through the wall column");
            Assert.IsFalse(Physics.Linecast(new Vector3(0.5f, 0f, 3.5f), new Vector3(4.5f, 0f, 3.5f), obstacles), "along the open row in front of it");
        }

        [Test]
        public void TheGroundPlaneIsOnTheGroundLayerAndACameraRayThatIgnoresObstaclesHitsItEvenOverAWall()
        {
            Paint(new[] { "..#..", "....." });
            LocationBaker.Bake(root, Options());
            Physics.SyncTransforms();
            var overWall = new Ray(new Vector3(2.5f, 10f, 0.5f), Vector3.down);

            Assert.IsTrue(Physics.Raycast(overWall, out var hit, 50f, ~(1 << ObstacleLayer)), "the router's mask excludes the obstacle layer");
            Assert.AreEqual(GroundLayer, hit.collider.gameObject.layer);
            Assert.AreEqual(0f, hit.point.y, 0.01f, "on the ground plane, not on top of the wall box");

            Assert.IsTrue(Physics.Raycast(overWall, out var wallHit, 50f, 1 << ObstacleLayer), "the wall box is still there for line of sight");
            Assert.AreEqual(ObstacleLayer, wallHit.collider.gameObject.layer);
        }

        [Test]
        public void TheGroundColliderCoversTheWholeMap()
        {
            Paint(new[] { "....", "...." }, rootPosition: new Vector3(5f, 0f, 3f));
            LocationBaker.Bake(root, Options());

            var ground = root.transform.Find("Baked/Ground").GetComponent<BoxCollider>();
            Assert.AreEqual(GroundLayer, ground.gameObject.layer);
            Assert.AreEqual(new Vector3(9f, 0f, 5f).x, ground.bounds.max.x, 1e-3f);
            Assert.AreEqual(5f, ground.bounds.min.x, 1e-3f);
            Assert.AreEqual(3f, ground.bounds.min.z, 1e-3f);
            Assert.AreEqual(5f, ground.bounds.max.z, 1e-3f);
            Assert.LessOrEqual(ground.bounds.max.y, 0.001f, "its top is the ground plane");
        }

        [Test]
        public void CameraBoundsAreWrittenToTheLocationContentWithoutDisturbingItsOtherData()
        {
            Paint(new[] { "....", "...." });
            var content = root.AddComponent<LocationContent>();
            var so = new SerializedObject(content);
            so.FindProperty("spawnPoints").arraySize = 1;
            so.FindProperty("spawnPoints").GetArrayElementAtIndex(0).FindPropertyRelative("SpawnId").stringValue = "keep-me";
            so.ApplyModifiedPropertiesWithoutUndo();

            LocationBaker.Bake(root, Options());

            var after = root.GetComponent<LocationContent>();
            Assert.AreSame(content, after, "no second LocationContent");
            Assert.AreEqual(new Rect(0f, 0f, 4f, 2f), after.CameraBoundsXZ);
            Assert.AreEqual(1, after.SpawnPoints.Count);
            Assert.AreEqual("keep-me", after.SpawnPoints[0].SpawnId);
        }

        // ---------------------------------------------------------------------------------------------
        // Checks and failing closed
        // ---------------------------------------------------------------------------------------------

        private void AssertRefused(BakeReport report, string mentions)
        {
            Assert.IsFalse(report.Baked);
            Assert.IsTrue(report.HasErrors);
            StringAssert.Contains(mentions, string.Join("\n", report.Errors.Select(e => e.Message)));
            Assert.IsNull(root.transform.Find("Baked"), "fails closed: nothing was written");
            Assert.IsNull(root.GetComponent<LocationContent>());
        }

        [Test]
        public void ATileWithNoSemanticOrThatIsNotASemanticTileIsAnErrorAndNothingIsWritten()
        {
            var maps = Paint(new[] { "..." });
            var unclassified = ScriptableObject.CreateInstance<SemanticTile>();
            made.Add(unclassified);
            maps[1].SetTile(new Vector3Int(0, 0, 0), unclassified);
            AssertRefused(LocationBaker.Bake(root, Options()), "no semantic");

            maps[1].SetTile(new Vector3Int(0, 0, 0), null);
            var plain = ScriptableObject.CreateInstance<Tile>();
            made.Add(plain);
            maps[1].SetTile(new Vector3Int(1, 0, 0), plain);
            AssertRefused(LocationBaker.Bake(root, Options()), "not a SemanticTile");
        }

        [Test]
        public void AGridThatIsNotXzyAnEmptyMapAndBadLayersAreRefused()
        {
            Paint(new[] { "..." }, swizzle: GridLayout.CellSwizzle.XYZ);
            AssertRefused(LocationBaker.Bake(root, Options()), "XZY");
            Object.DestroyImmediate(root);

            var maps = Paint(new[] { "..." });
            maps[0].ClearAllTiles();
            maps[1].ClearAllTiles();
            AssertRefused(LocationBaker.Bake(root, Options()), "no painted tiles");
            Object.DestroyImmediate(root);

            Paint(new[] { "..." });
            var sameLayers = LocationBaker.Bake(root, new LocationBakeOptions { GroundLayer = 5, ObstacleLayer = 5 });
            Assert.IsTrue(sameLayers.HasErrors);
            Assert.IsFalse(sameLayers.Baked);
        }

        [Test]
        public void ALocationOutsideTheSortableZRangeIsRefused()
        {
            Paint(new[] { "...", "..." }, rootPosition: new Vector3(0f, 0f, 300f));
            AssertRefused(LocationBaker.Bake(root, Options()), "256");
        }

        [Test]
        public void ADoorWithNoWalkableNeighbourIsAnError()
        {
            Paint(new[]
            {
                "###",
                "#D#",
                "###",
            });
            AssertRefused(LocationBaker.Bake(root, Options()), "Door has no walkable neighbouring cell");
        }

        [Test]
        public void ATravelDestinationOnAWallIsAnErrorAndOnFloorIsFine()
        {
            Paint(new[] { "..#..", "....." });
            Destination("onWall", new Vector3(2.5f, 0f, 0.5f));
            AssertRefused(LocationBaker.Bake(root, Options()), "onWall");

            foreach (var d in Object.FindObjectsByType<SceneTravelDestination>(FindObjectsSortMode.None)) Object.DestroyImmediate(d.gameObject);
            Destination("okay", new Vector3(0.5f, 0f, 0.5f));
            Assert.IsTrue(LocationBaker.Bake(root, Options()).Baked);
        }

        [Test]
        public void ADecorTileOnVoidAndAnUnreachableWalkableRegionAreWarningsNotErrors()
        {
            Paint(new[]
            {
                "..#..",
                "..#..",
                "..#.v",
            });
            Destination("west", new Vector3(0.5f, 0f, 0.5f));

            var report = LocationBaker.Bake(root, Options());

            Assert.IsTrue(report.Baked, "warnings do not stop a bake");
            var warnings = string.Join("\n", report.Warnings.Select(w => w.Message));
            StringAssert.Contains("floats on a cell with no ground", warnings);
            StringAssert.Contains("5 walkable cell(s) cannot be reached", warnings, "the east side (2 x 3 minus the void corner) is cut off by the wall");
        }

        [Test]
        public void ADoorCountsAsOpenForReachabilityAndWithNoDestinationsReachabilityIsSkipped()
        {
            Paint(new[]
            {
                "..#..",
                "..D..",
                "..#..",
            });
            Destination("west", new Vector3(0.5f, 0f, 0.5f));
            var report = LocationBaker.Bake(root, Options());
            Assert.IsTrue(report.Baked);
            Assert.IsFalse(report.Warnings.Any(w => w.Message.Contains("cannot be reached")), "the east side is reachable through the door");

            foreach (var d in Object.FindObjectsByType<SceneTravelDestination>(FindObjectsSortMode.None)) Object.DestroyImmediate(d.gameObject);
            var noStarts = LocationBaker.Bake(root, Options());
            Assert.IsTrue(noStarts.Issues.Any(i => i.Severity == BakeSeverity.Info && i.Message.Contains("reachability was not checked")));
        }

        // ---------------------------------------------------------------------------------------------
        // Idempotence
        // ---------------------------------------------------------------------------------------------

        [Test]
        public void BakingTwiceGivesTheSameResultLeavesOneBakedRootAndKeepsHandPlacedObjects()
        {
            var maps = Paint(new[] { "..#..", "..#..", "....." });
            var handPlaced = new GameObject("HandPlaced");
            handPlaced.transform.SetParent(root.transform, false);

            var first = LocationBaker.Bake(root, Options());
            var firstBlocked = Enumerable.Range(0, 5).SelectMany(x => Enumerable.Range(0, 3).Select(y => (x, y))).Where(c => !GridOf(root).IsWalkable(new GridCoordinate(c.x, c.y))).ToList();
            var firstWalls = root.transform.Find("Baked/Walls").GetComponentsInChildren<BoxCollider>().Select(b => (b.bounds.center, b.bounds.size)).ToList();

            var second = LocationBaker.Bake(root, Options());

            Assert.IsTrue(second.Baked);
            Assert.AreEqual(1, root.transform.Cast<Transform>().Count(t => t.name == LocationBaker.BakedRootName), "the old Baked root was replaced");
            Assert.IsNotNull(handPlaced, "a hand-placed sibling survives");
            var secondBlocked = Enumerable.Range(0, 5).SelectMany(x => Enumerable.Range(0, 3).Select(y => (x, y))).Where(c => !GridOf(root).IsWalkable(new GridCoordinate(c.x, c.y))).ToList();
            CollectionAssert.AreEqual(firstBlocked, secondBlocked);
            CollectionAssert.AreEqual(firstWalls, root.transform.Find("Baked/Walls").GetComponentsInChildren<BoxCollider>().Select(b => (b.bounds.center, b.bounds.size)).ToList());
            Assert.AreEqual(first.WalkableCells, second.WalkableCells);
            Assert.IsNotNull(maps);
        }

        [Test]
        public void ARefusedRebakeLeavesThePreviousBakedRootUntouched()
        {
            var maps = Paint(new[] { "...", "..." });
            Assert.IsTrue(LocationBaker.Bake(root, Options()).Baked);
            var previous = root.transform.Find("Baked");

            var bad = ScriptableObject.CreateInstance<SemanticTile>();
            made.Add(bad);
            maps[1].SetTile(new Vector3Int(0, 0, 0), bad);
            var report = LocationBaker.Bake(root, Options());

            Assert.IsFalse(report.Baked);
            Assert.AreSame(previous, root.transform.Find("Baked"), "the last good bake is still there");
        }

        // ---------------------------------------------------------------------------------------------
        // Stale-bake detection (BakeFingerprint)
        // ---------------------------------------------------------------------------------------------

        [Test]
        public void ABakeRecordsAFingerprintOfThePaintedTilesAndIsFreshStraightAfter()
        {
            Paint(new[] { "..#..", "....." });
            LocationBaker.Bake(root, Options());

            var fingerprint = root.transform.Find("Baked").GetComponent<BakeFingerprint>();
            Assert.IsNotNull(fingerprint);
            Assert.AreEqual(TileFingerprint.Compute(root.GetComponentInChildren<Grid>()), fingerprint.Value);
            Assert.IsFalse(LocationBaker.IsStale(root, out var message), message);
            Assert.IsTrue(fingerprint.IsCurrent(out _));
        }

        [Test]
        public void PaintingAnObjectAfterTheBakeMakesItStaleWithAnActionableMessageAndReBakingFixesIt()
        {
            var maps = Paint(new[] { ".....", "....." });
            LocationBaker.Bake(root, Options());

            maps[1].SetTile(new Vector3Int(2, 1, 0), tiles[TileSemantic.Prop]); // a tree painted after the bake: still walk-through

            Assert.IsTrue(LocationBaker.IsStale(root, out var message));
            StringAssert.Contains("changed since it was last baked", message);
            StringAssert.Contains("Bake Selected Location", message);
            StringAssert.Contains("Location", message, "names the location");

            Assert.IsTrue(LocationBaker.Bake(root, Options()).Baked);
            Assert.IsFalse(LocationBaker.IsStale(root, out _));
            Assert.IsFalse(GridOf(root).IsWalkable(new GridCoordinate(2, 1)), "and the new object now blocks");
        }

        [Test]
        public void ErasingATileMakesItStaleAndPaintingTheSameTileBackMakesItFreshAgain()
        {
            var maps = Paint(new[] { "..T..", "....." });
            LocationBaker.Bake(root, Options());

            maps[1].SetTile(new Vector3Int(2, 0, 0), null);
            Assert.IsTrue(LocationBaker.IsStale(root, out _), "erased");

            maps[1].SetTile(new Vector3Int(2, 0, 0), tiles[TileSemantic.Prop]);
            Assert.IsFalse(LocationBaker.IsStale(root, out var message), "the same tile back in the same cell is the same map: " + message);
        }

        [Test]
        public void MovingATileToAnotherCellMakesTheBakeStale()
        {
            var maps = Paint(new[] { "..T..", "....." });
            LocationBaker.Bake(root, Options());

            maps[1].SetTile(new Vector3Int(2, 0, 0), null);
            maps[1].SetTile(new Vector3Int(3, 1, 0), tiles[TileSemantic.Prop]); // the same tile, one cell over: same count, different map

            Assert.IsTrue(LocationBaker.IsStale(root, out _));
        }

        [Test]
        public void SwappingOneGroundTileForAnotherOfTheSameMeaningIsNotStaleBecauseTheBakeIsUnchanged()
        {
            var maps = Paint(new[] { "....." });
            LocationBaker.Bake(root, Options());

            var otherGrass = ScriptableObject.CreateInstance<SemanticTile>();
            made.Add(otherGrass);
            otherGrass.Configure(TileSemantic.Ground, null, null);
            otherGrass.name = "another-grass";
            maps[0].SetTile(new Vector3Int(1, 0, 0), otherGrass);

            Assert.IsFalse(LocationBaker.IsStale(root, out var message), message);
        }

        [Test]
        public void ATilemapOutsideTheGridDoesNotAffectTheFingerprint()
        {
            Paint(new[] { "..#.." });
            LocationBaker.Bake(root, Options());

            var stray = new GameObject("Stray");
            made.Add(stray);
            var strayGrid = stray.AddComponent<Grid>();
            var mapGo = new GameObject("Elsewhere");
            mapGo.transform.SetParent(stray.transform, false);
            mapGo.AddComponent<Tilemap>().SetTile(new Vector3Int(1, 1, 0), tiles[TileSemantic.Prop]);

            Assert.IsFalse(LocationBaker.IsStale(root, out var message), message);
            Assert.IsNotNull(strayGrid);
        }

        [Test]
        public void ChangingWhatATileMeansMakesTheBakeStale()
        {
            Paint(new[] { "..T.." });
            LocationBaker.Bake(root, Options());

            tiles[TileSemantic.Prop].Configure(TileSemantic.Decor, null, null); // the same asset, now walkable decor

            Assert.IsTrue(LocationBaker.IsStale(root, out _));
        }

        [Test]
        public void HandPlacedObjectsAndReBakingDoNotChangeTheFingerprint()
        {
            Paint(new[] { "..#..", "....." });
            LocationBaker.Bake(root, Options());
            var before = root.transform.Find("Baked").GetComponent<BakeFingerprint>().Value;

            new GameObject("A hand-placed chest").transform.SetParent(root.transform, false);
            LocationBaker.Bake(root, Options());

            Assert.AreEqual(before, root.transform.Find("Baked").GetComponent<BakeFingerprint>().Value);
            Assert.IsFalse(LocationBaker.IsStale(root, out _));
        }

        [Test]
        public void TheFingerprintIsDeterministicForTheSameMapAndDiffersForADifferentOne()
        {
            Paint(new[] { "..#..", "..T.." });
            var first = TileFingerprint.Compute(root.GetComponentInChildren<Grid>());
            Object.DestroyImmediate(root);

            Paint(new[] { "..#..", "..T.." });
            Assert.AreEqual(first, TileFingerprint.Compute(root.GetComponentInChildren<Grid>()), "the same painting, built again");
            Object.DestroyImmediate(root);

            Paint(new[] { "..#..", "..#.." });
            Assert.AreNotEqual(first, TileFingerprint.Compute(root.GetComponentInChildren<Grid>()));
        }

        [Test]
        public void ALocationNeverBakedAndABakeWithoutAFingerprintAreBothReportedStale()
        {
            Paint(new[] { "...", "..." });
            Assert.IsTrue(LocationBaker.IsStale(root, out var never));
            StringAssert.Contains("no bake", never);

            LocationBaker.Bake(root, Options());
            Object.DestroyImmediate(root.transform.Find("Baked").GetComponent<BakeFingerprint>()); // a bake made before fingerprints existed
            Assert.IsTrue(LocationBaker.IsStale(root, out var old));
            StringAssert.Contains("fingerprint", old);
        }

        // ---------------------------------------------------------------------------------------------
        // WallMerger / WalkableRegions
        // ---------------------------------------------------------------------------------------------

        [Test]
        public void WallMergerCoversEveryCellExactlyOnceForRandomShapes()
        {
            var random = new System.Random(1234);
            for (var trial = 0; trial < 200; trial++)
            {
                var cells = new HashSet<Vector2Int>();
                for (var y = 0; y < 8; y++)
                for (var x = 0; x < 10; x++)
                    if (random.NextDouble() < 0.55) cells.Add(new Vector2Int(x, y));

                var rects = WallMerger.Merge(cells);
                var covered = new HashSet<Vector2Int>();
                foreach (var rect in rects)
                foreach (var cell in rect.allPositionsWithin)
                    Assert.IsTrue(covered.Add(cell), $"trial {trial}: {cell} covered twice");
                Assert.IsTrue(covered.SetEquals(cells), $"trial {trial}: exact cover");
                Assert.LessOrEqual(rects.Count, cells.Count);
            }
        }

        [Test]
        public void WallMergerMakesOneRectangleForARectangleAndIsDeterministic()
        {
            var block = new List<Vector2Int>();
            for (var y = 2; y < 5; y++)
            for (var x = 1; x < 6; x++)
                block.Add(new Vector2Int(x, y));

            var once = WallMerger.Merge(block);
            Assert.AreEqual(1, once.Count);
            Assert.AreEqual(new RectInt(1, 2, 5, 3), once[0]);

            block.Reverse();
            CollectionAssert.AreEqual(once, WallMerger.Merge(block), "input order does not matter");
            Assert.IsEmpty(WallMerger.Merge(new Vector2Int[0]));
        }

        [Test]
        public void WalkableRegionsFollowThePathfindersRuleNoCornerCutting()
        {
            // Two walkable cells that touch only diagonally, both orthogonal cells beside the diagonal blocked.
            bool Diagonal(int x, int y) => (x == 0 && y == 0) || (x == 1 && y == 1);
            Assert.AreEqual(2, WalkableRegions.Find(2, 2, Diagonal, null).Count, "corner cutting is not allowed");

            // With one orthogonal cell open they are one region (an L).
            bool L(int x, int y) => (x == 0 && y == 0) || (x == 1 && y == 1) || (x == 1 && y == 0);
            Assert.AreEqual(1, WalkableRegions.Find(2, 2, L, null).Count);

            var regions = WalkableRegions.Find(5, 1, (x, y) => x != 2, new[] { new Vector2Int(0, 0) });
            Assert.AreEqual(2, regions.Count);
            Assert.AreEqual(1, regions.Count(r => r.ContainsStart));
        }
    }
}
