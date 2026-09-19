using System;
using System.Collections.Generic;
using System.Linq;
using CRPG.Core;
using CRPG.Navigation.Grid;
using NUnit.Framework;
using UnityEngine;

namespace CRPG.Tests
{
    public class WalkabilityGridTests
    {
        private static GridCoordinate C(int x, int z) => new(x, z);

        [Test]
        public void ConstructorRejectsNonPositiveSizes()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => new WalkabilityGrid(Vector3.zero, 0f, 4, 4));
            Assert.Throws<ArgumentOutOfRangeException>(() => new WalkabilityGrid(Vector3.zero, 1f, 0, 4));
            Assert.Throws<ArgumentOutOfRangeException>(() => new WalkabilityGrid(Vector3.zero, 1f, 4, 0));
        }

        [Test]
        public void ABlockedCellOutsideTheGridIsRejected() =>
            Assert.Throws<ArgumentOutOfRangeException>(() => new WalkabilityGrid(Vector3.zero, 1f, 4, 4, new[] { C(4, 0) }));

        [Test]
        public void ToGridFloorsIncludingBelowTheOrigin()
        {
            var grid = new WalkabilityGrid(Vector3.zero, 1f, 4, 4);

            Assert.AreEqual(C(0, 0), grid.ToGrid(new Vector3(0.99f, 0f, 0.01f)));
            Assert.AreEqual(C(1, 0), grid.ToGrid(new Vector3(1f, 0f, 0f)));
            Assert.AreEqual(C(-1, -1), grid.ToGrid(new Vector3(-0.01f, 0f, -0.5f)), "Negative side must floor, not truncate toward zero.");
        }

        [Test]
        public void ToWorldIsTheCellCentreAtGroundHeightRespectingOriginAndCellSize()
        {
            var grid = new WalkabilityGrid(new Vector3(10f, 3f, 20f), 2f, 5, 5);

            Assert.AreEqual(new Vector3(13f, 3f, 25f), grid.ToWorld(C(1, 2)));
            Assert.AreEqual(3f, grid.GroundY);
        }

        [Test]
        public void ToGridOfToWorldRoundTripsEveryCell()
        {
            var grid = new WalkabilityGrid(new Vector3(-7f, 0f, 13f), 1.5f, 6, 9);

            for (var x = 0; x < 6; x++)
                for (var z = 0; z < 9; z++)
                    Assert.AreEqual(C(x, z), grid.ToGrid(grid.ToWorld(C(x, z))));
        }

        [Test]
        public void WalkabilityReflectsBoundsAndBlockingAndSetBlockedToggles()
        {
            var grid = new WalkabilityGrid(Vector3.zero, 1f, 4, 4, new[] { C(1, 1) });

            Assert.IsTrue(grid.IsWalkable(C(0, 0)));
            Assert.IsFalse(grid.IsWalkable(C(1, 1)));
            Assert.IsFalse(grid.IsWalkable(C(-1, 0)));
            Assert.IsFalse(grid.IsWalkable(C(4, 0)));

            grid.SetBlocked(C(1, 1), false);
            grid.SetBlocked(C(2, 2), true);
            Assert.IsTrue(grid.IsWalkable(C(1, 1)));
            Assert.IsFalse(grid.IsWalkable(C(2, 2)));

            Assert.DoesNotThrow(() => grid.SetBlocked(C(99, 99), true));
        }

        [Test]
        public void OccupancyIsExclusiveIdempotentForTheHolderAndOnlyReleasableByIt()
        {
            var grid = new WalkabilityGrid(Vector3.zero, 1f, 4, 4);
            var a = RuntimeEntityId.NewId();
            var b = RuntimeEntityId.NewId();

            Assert.IsTrue(grid.TryOccupy(C(1, 1), a));
            Assert.IsTrue(grid.TryOccupy(C(1, 1), a), "Re-claiming your own cell is a harmless no-op.");
            Assert.IsFalse(grid.TryOccupy(C(1, 1), b), "Someone else's cell cannot be taken.");
            Assert.IsTrue(grid.IsOccupied(C(1, 1), out var holder));
            Assert.AreEqual(a, holder);

            grid.Release(C(1, 1), b); // stale/foreign release
            Assert.IsTrue(grid.IsOccupied(C(1, 1), out _), "A non-holder must not be able to evict the holder.");

            grid.Release(C(1, 1), a);
            Assert.IsFalse(grid.IsOccupied(C(1, 1), out _));
            Assert.IsTrue(grid.TryOccupy(C(1, 1), b));
        }

        [Test]
        public void OccupancyOutsideTheGridIsRefusedAndDoesNotAffectWalkability()
        {
            var grid = new WalkabilityGrid(Vector3.zero, 1f, 4, 4);
            var a = RuntimeEntityId.NewId();

            Assert.IsFalse(grid.TryOccupy(C(9, 9), a));
            Assert.IsTrue(grid.TryOccupy(C(2, 2), a));
            Assert.IsTrue(grid.IsWalkable(C(2, 2)), "An occupied cell is still terrain-walkable -- occupancy is enforced by the mover, not by walls.");
        }
    }

    public class GridPathfindingServiceTests
    {
        private static GridCoordinate C(int x, int z) => new(x, z);

        private static WalkabilityGrid MakeGrid(int width, int depth, params (int x, int z)[] blocked) =>
            new(new Vector3(100f, 2f, -40f), 1f, width, depth, blocked.Select(b => C(b.x, b.z)));

        private static GridPathfindingService ServiceFor(WalkabilityGrid grid)
        {
            var service = new GridPathfindingService();
            service.SetActiveGrid(grid);
            return service;
        }

        /// <summary>The invariant that makes straight-line interpolation between corners safe: every consecutive
        /// pair of corners sits in adjacent-or-same walkable cells, and a diagonal step never clips a wall
        /// corner (both orthogonally-adjacent cells are walkable).</summary>
        private static void AssertCornersAreSafe(WalkabilityGrid grid, Vector3[] corners)
        {
            for (var i = 1; i < corners.Length; i++)
            {
                var a = grid.ToGrid(corners[i - 1]);
                var b = grid.ToGrid(corners[i]);
                Assert.IsTrue(grid.IsWalkable(b), $"corner {i} is in a blocked cell {b}");

                // A mover standing inside a blocked cell (it was snapped out) legitimately hops to the nearest
                // walkable centre, which need not be adjacent; the straight-line guarantee starts after that.
                if (i == 1 && !grid.IsWalkable(a)) continue;

                Assert.LessOrEqual(Mathf.Abs(a.X - b.X), 1, $"corners {i - 1}->{i} are not adjacent");
                Assert.LessOrEqual(Mathf.Abs(a.Z - b.Z), 1, $"corners {i - 1}->{i} are not adjacent");
                if (a.X != b.X && a.Z != b.Z)
                {
                    Assert.IsTrue(grid.IsWalkable(C(b.X, a.Z)) && grid.IsWalkable(C(a.X, b.Z)), $"corners {i - 1}->{i} cut a wall corner");
                }
            }
        }

        [Test]
        public void WithNoActiveGridEveryQueryFailsClosed()
        {
            var service = new GridPathfindingService();
            var point = new Vector3(1f, 2f, 3f);

            Assert.IsFalse(service.TryCalculatePath(point, point, out _));
            Assert.IsFalse(service.TryCalculatePathLength(point, point, out _));
            Assert.IsFalse(service.TrySamplePosition(point, out var sampled));
            Assert.AreEqual(point, sampled);
        }

        [Test]
        public void AStraightPathIsTheCallersFromThenEveryCellCentre()
        {
            var grid = MakeGrid(10, 4);
            var service = ServiceFor(grid);

            Assert.IsTrue(service.TryCalculatePath(grid.ToWorld(C(0, 1)), grid.ToWorld(C(4, 1)), out var corners));

            Assert.AreEqual(5, corners.Length);
            for (var i = 0; i < 5; i++) Assert.AreEqual(grid.ToWorld(C(i, 1)), corners[i]);
            AssertCornersAreSafe(grid, corners);
        }

        [Test]
        public void TheFirstCornerIsTheCallersExactPositionProjectedOntoTheGroundNotACellCentre()
        {
            var grid = MakeGrid(10, 4);
            var service = ServiceFor(grid);
            var from = grid.ToWorld(C(1, 1)) + new Vector3(0.3f, 99f, -0.2f); // off-centre and at the wrong height

            Assert.IsTrue(service.TryCalculatePath(from, grid.ToWorld(C(5, 1)), out var corners));

            Assert.AreEqual(from.x, corners[0].x);
            Assert.AreEqual(from.z, corners[0].z);
            Assert.AreEqual(grid.GroundY, corners[0].y);
            AssertCornersAreSafe(grid, corners);
        }

        [Test]
        public void ASameCellMoveIsTwoCornersWhenOffCentreAndOneWhenAlreadyOnTheCentre()
        {
            var grid = MakeGrid(6, 6);
            var service = ServiceFor(grid);
            var centre = grid.ToWorld(C(2, 2));

            Assert.IsTrue(service.TryCalculatePath(centre + new Vector3(0.3f, 0f, 0.1f), centre + new Vector3(-0.2f, 0f, 0.2f), out var offCentre));
            Assert.AreEqual(2, offCentre.Length);
            Assert.AreEqual(centre, offCentre[1]);

            Assert.IsTrue(service.TryCalculatePath(centre, centre, out var onCentre));
            Assert.AreEqual(1, onCentre.Length);
        }

        [Test]
        public void APathRoutesAroundAWallThroughTheGap()
        {
            // Wall down x = 3 for z = 0..3; gap at z = 4..5.
            var grid = MakeGrid(8, 6, Enumerable.Range(0, 4).Select(z => (3, z)).ToArray());
            var service = ServiceFor(grid);

            Assert.IsTrue(service.TryCalculatePath(grid.ToWorld(C(1, 1)), grid.ToWorld(C(6, 1)), out var corners));

            AssertCornersAreSafe(grid, corners);
            Assert.IsTrue(corners.Any(c => grid.ToGrid(c).Z >= 4), "The only way across is through the gap.");
            Assert.IsTrue(service.TryCalculatePathLength(grid.ToWorld(C(1, 1)), grid.ToWorld(C(6, 1)), out var length));
            Assert.Greater(length, 5f, "Longer than the blocked straight line.");
        }

        [Test]
        public void ADiagonalShortcutBetweenTwoBlockedCornersIsRefusedAndDetoured()
        {
            // (1,2) and (2,1) blocked: stepping (1,1)->(2,2) diagonally would squeeze through a pinch.
            var grid = MakeGrid(4, 4, (1, 2), (2, 1));
            var service = ServiceFor(grid);

            Assert.IsTrue(service.TryCalculatePath(grid.ToWorld(C(1, 1)), grid.ToWorld(C(2, 2)), out var corners));

            Assert.Greater(corners.Length, 2, "The direct diagonal must not be taken.");
            AssertCornersAreSafe(grid, corners);
        }

        [Test]
        public void ACellSealedOffByADiagonalPinchIsUnreachable()
        {
            var grid = MakeGrid(3, 3, (1, 0), (0, 1)); // (0,0) can only leave diagonally, through the pinch
            var service = ServiceFor(grid);

            Assert.IsFalse(service.TryCalculatePath(grid.ToWorld(C(0, 0)), grid.ToWorld(C(2, 2)), out _));
            Assert.IsFalse(service.TryCalculatePathLength(grid.ToWorld(C(0, 0)), grid.ToWorld(C(2, 2)), out _));
        }

        [Test]
        public void OpenFieldPathsAreOptimalUsingTrueEuclideanStepCosts()
        {
            var grid = MakeGrid(12, 12);
            var service = ServiceFor(grid);

            Assert.IsTrue(service.TryCalculatePathLength(grid.ToWorld(C(0, 0)), grid.ToWorld(C(5, 3)), out var length));

            Assert.AreEqual(3f * Mathf.Sqrt(2f) + 2f, length, 1e-3f, "3 diagonals + 2 cardinals.");
        }

        [Test]
        public void CellSizeScalesPathLengths()
        {
            var grid = new WalkabilityGrid(new Vector3(100f, 2f, -40f), 2f, 8, 8);
            var service = ServiceFor(grid);

            Assert.IsTrue(service.TryCalculatePathLength(grid.ToWorld(C(0, 0)), grid.ToWorld(C(3, 0)), out var length));

            Assert.AreEqual(6f, length, 1e-3f);
        }

        [Test]
        public void ADestinationInsideAWallSnapsToANearbyWalkableCentre()
        {
            var grid = MakeGrid(8, 8, (5, 3));
            var service = ServiceFor(grid);

            Assert.IsTrue(service.TryCalculatePath(grid.ToWorld(C(1, 3)), grid.ToWorld(C(5, 3)), out var corners));

            var last = corners[corners.Length - 1];
            Assert.IsTrue(grid.IsWalkableWorld(last));
            Assert.LessOrEqual(Vector3.Distance(last, grid.ToWorld(C(5, 3))), 1.001f);
            AssertCornersAreSafe(grid, corners);
        }

        [Test]
        public void ADestinationTooFarFromAnyWalkableCellFails()
        {
            // A solid block x = 2..12; the middle of it is ~5 cells from the nearest open cell, past the 2-unit snap.
            var blocked = new List<(int, int)>();
            for (var x = 2; x <= 12; x++)
                for (var z = 0; z < 5; z++) blocked.Add((x, z));
            var grid = MakeGrid(15, 5, blocked.ToArray());
            var service = ServiceFor(grid);

            Assert.IsFalse(service.TryCalculatePath(grid.ToWorld(C(0, 2)), grid.ToWorld(C(7, 2)), out _));
            Assert.IsFalse(service.TryCalculatePathLength(grid.ToWorld(C(0, 2)), grid.ToWorld(C(7, 2)), out _));
        }

        [Test]
        public void AMoverStandingInANowBlockedCellSnapsOutViaTheNearestWalkableCentre()
        {
            var grid = MakeGrid(8, 4, (3, 1)); // e.g. a door that just closed on the mover
            var service = ServiceFor(grid);
            var from = grid.ToWorld(C(3, 1)) + new Vector3(0.1f, 0f, 0.05f);

            Assert.IsTrue(service.TryCalculatePath(from, grid.ToWorld(C(6, 1)), out var corners));

            Assert.AreEqual(from.x, corners[0].x);
            Assert.IsTrue(grid.IsWalkableWorld(corners[1]), "The first stop must be a walkable centre, not another point inside the wall.");
            Assert.LessOrEqual(Vector3.Distance(corners[1], grid.ToWorld(C(3, 1))), 1.001f);
        }

        [Test]
        public void AnUnreachableIslandFailsBothPathAndLength()
        {
            var ring = new[] { (5, 5), (6, 5), (7, 5), (5, 6), (7, 6), (5, 7), (6, 7), (7, 7) };
            var grid = MakeGrid(10, 10, ring);
            var service = ServiceFor(grid);

            Assert.IsFalse(service.TryCalculatePath(grid.ToWorld(C(0, 0)), grid.ToWorld(C(6, 6)), out _));
            Assert.IsFalse(service.TryCalculatePathLength(grid.ToWorld(C(0, 0)), grid.ToWorld(C(6, 6)), out _));
        }

        [Test]
        public void PathLengthAndPathAgreeOnReachabilityAndSnappingAcrossManyRandomQueries()
        {
            // TryCalculatePathLength gates party formation slots and TryCalculatePath plans the move over the same
            // destination, so any disagreement would surface as "resolved Exact, then Rejected at plan time".
            var random = new System.Random(1234);
            var blocked = new List<(int, int)>();
            for (var x = 0; x < 15; x++)
                for (var z = 0; z < 15; z++)
                    if (random.NextDouble() < 0.3) blocked.Add((x, z));
            var grid = MakeGrid(15, 15, blocked.ToArray());
            var service = ServiceFor(grid);

            var reachable = 0;
            for (var i = 0; i < 400; i++)
            {
                var from = new Vector3(100f + (float)random.NextDouble() * 15f, 2f, -40f + (float)random.NextDouble() * 15f);
                var to = new Vector3(100f + (float)random.NextDouble() * 15f, 2f, -40f + (float)random.NextDouble() * 15f);

                var pathOk = service.TryCalculatePath(from, to, out var corners);
                var lengthOk = service.TryCalculatePathLength(from, to, out var length);

                Assert.AreEqual(pathOk, lengthOk, $"query {i}: the two methods disagree about reachability");
                if (!pathOk) continue;

                reachable++;
                var summed = 0f;
                for (var k = 1; k < corners.Length; k++) summed += Vector3.Distance(corners[k - 1], corners[k]);
                Assert.AreEqual(summed, length, 1e-3f);
                AssertCornersAreSafe(grid, corners);
            }

            Assert.Greater(reachable, 50, "The random map should leave plenty of reachable queries, or this test proves nothing.");
        }

        [Test]
        public void TrySamplePositionReturnsTheCentreOfTheNearestWalkableCellWithinRange()
        {
            var grid = MakeGrid(8, 8, (4, 4));
            var service = ServiceFor(grid);

            Assert.IsTrue(service.TrySamplePosition(grid.ToWorld(C(2, 2)) + new Vector3(0.3f, 50f, 0.1f), out var onCell));
            Assert.AreEqual(grid.ToWorld(C(2, 2)), onCell, "A point on a walkable cell samples to its centre (Y is ignored).");

            Assert.IsTrue(service.TrySamplePosition(grid.ToWorld(C(4, 4)), out var fromWall, maxDistance: 1.5f));
            Assert.IsTrue(grid.IsWalkableWorld(fromWall));
            Assert.LessOrEqual(Vector3.Distance(fromWall, grid.ToWorld(C(4, 4))), 1.5f);

            var farOut = new Vector3(500f, 2f, 500f);
            Assert.IsFalse(service.TrySamplePosition(farOut, out var untouched));
            Assert.AreEqual(farOut, untouched);
        }

        [Test]
        public void SettingANewActiveGridSwapsReachabilityWithNoStateLeftFromTheOldOne()
        {
            var open = MakeGrid(8, 3);
            var walled = MakeGrid(8, 3, (4, 0), (4, 1), (4, 2));
            var service = ServiceFor(open);
            var from = open.ToWorld(C(0, 1));
            var to = open.ToWorld(C(7, 1));

            Assert.IsTrue(service.TryCalculatePath(from, to, out _));
            service.SetActiveGrid(walled);
            Assert.IsFalse(service.TryCalculatePath(from, to, out _), "The old grid's open corridor must not leak into the new one.");
            service.SetActiveGrid(open);
            Assert.IsTrue(service.TryCalculatePath(from, to, out _));
            service.SetActiveGrid(null);
            Assert.IsFalse(service.TryCalculatePath(from, to, out _));
        }

        [Test]
        public void TogglingADoorCellChangesReachability()
        {
            var grid = MakeGrid(7, 1, (3, 0));
            var service = ServiceFor(grid);
            var from = grid.ToWorld(C(0, 0));
            var to = grid.ToWorld(C(6, 0));

            Assert.IsFalse(service.TryCalculatePath(from, to, out _), "A closed door blocks the only path.");
            grid.SetBlocked(C(3, 0), false);
            Assert.IsTrue(service.TryCalculatePath(from, to, out _), "Opening it restores the path.");
            grid.SetBlocked(C(3, 0), true);
            Assert.IsFalse(service.TryCalculatePath(from, to, out _));
        }

        [Test]
        public void OccupancyIsIgnoredByPathfinding()
        {
            var grid = MakeGrid(7, 1);
            var service = ServiceFor(grid);
            grid.TryOccupy(C(3, 0), RuntimeEntityId.NewId());

            Assert.IsTrue(service.TryCalculatePath(grid.ToWorld(C(0, 0)), grid.ToWorld(C(6, 0)), out var corners),
                "Characters are dynamic; the mover enforces occupancy at move time, not the pathfinder.");
            Assert.AreEqual(7, corners.Length);
        }

        [Test]
        public void RepeatedAndInterleavedQueriesAreIndependent()
        {
            var grid = MakeGrid(20, 20, Enumerable.Range(0, 15).Select(z => (10, z)).ToArray());
            var service = ServiceFor(grid);
            var a = grid.ToWorld(C(1, 1));
            var b = grid.ToWorld(C(18, 2));
            var c = grid.ToWorld(C(3, 18));

            Assert.IsTrue(service.TryCalculatePath(a, b, out var first));
            Assert.IsTrue(service.TryCalculatePath(a, c, out _));
            Assert.IsTrue(service.TryCalculatePath(b, c, out _));
            Assert.IsTrue(service.TryCalculatePath(a, b, out var second));

            CollectionAssert.AreEqual(first, second, "Reusing search scratch between queries must never change a result.");
        }

        [Test]
        public void ALongPathAcrossALargeGridIsOptimal()
        {
            var grid = MakeGrid(200, 200);
            var service = ServiceFor(grid);

            Assert.IsTrue(service.TryCalculatePathLength(grid.ToWorld(C(0, 0)), grid.ToWorld(C(199, 199)), out var length));

            Assert.AreEqual(199f * Mathf.Sqrt(2f), length, 0.01f);
        }
    }
}
