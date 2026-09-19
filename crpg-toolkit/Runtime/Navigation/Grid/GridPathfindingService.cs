using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

namespace CRPG.Navigation.Grid
{
    /// <summary>
    /// Grid-backed <see cref="INavMeshService"/>: 8-connected A* over a <see cref="WalkabilityGrid"/> with true
    /// Euclidean step costs (1 x cellSize cardinal, sqrt(2) x cellSize diagonal -- matching the Euclidean range/
    /// threat maths used everywhere else in the engine) and no corner cutting (a diagonal step is only legal
    /// when both orthogonally-adjacent cells are walkable, which also guarantees any straight segment between
    /// two consecutive path corners never clips a wall).
    /// <para>Constructed once at the composition root and re-pointed at each location's grid with
    /// <see cref="SetActiveGrid"/> -- the same lifetime as ActionScheduler/PartyMovementService, which persist
    /// across scene travel. With no active grid every query fails closed.</para>
    /// <para>Pathfinding ignores occupancy on purpose: characters are dynamic and enforced by the mover at move
    /// time. <c>areaMask</c> is accepted for interface compatibility and ignored (no terrain-cost tiers here),
    /// and Y is ignored entirely (a grid is a ground-plane structure).</para>
    /// </summary>
    public sealed class GridPathfindingService : ISlotAvoidingNavMeshService
    {
        private const float Sqrt2 = 1.41421356f;
        private const float SameCellCentreEpsilon = 1e-4f;

        private readonly float endpointSnapDistance;
        private WalkabilityGrid grid;

        // Reusable A* scratch, stamped per query so nothing needs clearing between queries.
        private int[] gStamp;
        private int[] closedStamp;
        private float[] gScore;
        private int[] parent;
        private int stamp;
        private readonly MinHeap open = new();
        private readonly HashSet<GridCoordinate> avoidedCells = new(); // scratch for TrySamplePositionAvoiding

        /// <param name="endpointSnapDistance">How far (world units, measured to a cell's centre) a path's start or
        /// end may be from the nearest walkable cell and still be snapped onto it, like NavMesh's own implicit
        /// snapping. Farther than this fails.</param>
        public GridPathfindingService(float endpointSnapDistance = 2f) => this.endpointSnapDistance = endpointSnapDistance;

        public WalkabilityGrid ActiveGrid => grid;

        /// <summary>Points the service at a location's grid (or null for none). Nothing about the previous grid is
        /// retained.</summary>
        public void SetActiveGrid(WalkabilityGrid newGrid)
        {
            grid = newGrid;
            stamp = 0;

            if (newGrid == null)
            {
                gStamp = closedStamp = parent = null;
                gScore = null;
                return;
            }

            gStamp = new int[newGrid.CellCount];
            closedStamp = new int[newGrid.CellCount];
            gScore = new float[newGrid.CellCount];
            parent = new int[newGrid.CellCount];
        }

        /// <summary>The walkable cell's centre if <paramref name="point"/> is on one; otherwise the centre of the
        /// nearest walkable cell within <paramref name="maxDistance"/> (measured to cell centres, ground plane
        /// only). Fails (returning the point unchanged) if there is none, or no active grid.</summary>
        public bool TrySamplePosition(Vector3 point, out Vector3 sampledPosition, float maxDistance = 5f, int areaMask = NavMesh.AllAreas)
        {
            sampledPosition = point;
            if (grid == null || !TryFindNearestWalkable(point, maxDistance, out var cell)) return false;

            sampledPosition = grid.ToWorld(cell);
            return true;
        }

        /// <summary>Like <see cref="TrySamplePosition"/>, but never returns a cell containing any of
        /// <paramref name="claimed"/> (two positions in the same cell are the same spot). Used by party formation
        /// resolution so several formation slots that fall within one cell resolve to distinct neighbouring cells
        /// instead of all snapping to the same one.</summary>
        public bool TrySamplePositionAvoiding(Vector3 point, out Vector3 sampledPosition, float maxDistance, IReadOnlyCollection<Vector3> claimed, int areaMask = NavMesh.AllAreas)
        {
            sampledPosition = point;
            if (grid == null) return false;

            avoidedCells.Clear();
            if (claimed != null)
            {
                foreach (var position in claimed) avoidedCells.Add(grid.ToGrid(position));
            }

            if (!TryFindNearestWalkable(point, maxDistance, out var cell, avoidedCells)) return false;

            sampledPosition = grid.ToWorld(cell);
            return true;
        }

        /// <summary>Exactly as permissive as <see cref="TryCalculatePath"/> (it runs the same search) -- callers
        /// such as party formation resolution gate on this and then plan with TryCalculatePath over the same
        /// destination, so the two must never disagree about reachability or snapping.</summary>
        public bool TryCalculatePathLength(Vector3 from, Vector3 to, out float length, int areaMask = NavMesh.AllAreas)
        {
            length = 0f;
            if (!TryCalculatePath(from, to, out var corners, areaMask)) return false;

            for (var i = 1; i < corners.Length; i++) length += Vector3.Distance(corners[i - 1], corners[i]);
            return true;
        }

        /// <summary>Corner list for a complete path. <c>corners[0]</c> is always the caller's exact
        /// <paramref name="from"/> (projected onto the grid's ground height), never a snapped cell centre --
        /// matching NavMesh's own <c>path.corners[0]</c> and keeping the first leg's threat-crossing maths anchored
        /// where the mover really stands. The rest are cell centres ending at the destination cell's centre, so
        /// every consecutive pair is (from -> an adjacent cell centre) or (two adjacent cell centres). If
        /// <c>from</c> and <c>to</c> land in the same cell this is <c>[from, thatCentre]</c>, or just
        /// <c>[from]</c> when the mover is already on the centre. Failure (unreachable, or an endpoint farther
        /// than the snap distance from any walkable cell) reports false, never a partial path.</summary>
        public bool TryCalculatePath(Vector3 from, Vector3 to, out Vector3[] corners, int areaMask = NavMesh.AllAreas)
        {
            corners = null;
            if (grid == null) return false;
            if (!TryFindNearestWalkable(from, endpointSnapDistance, out var startCell)) return false;
            if (!TryFindNearestWalkable(to, endpointSnapDistance, out var goalCell)) return false;
            if (!TryFindCellPath(startCell, goalCell, out var cells)) return false;

            var flatFrom = new Vector3(from.x, grid.GroundY, from.z);
            var result = new List<Vector3>(cells.Count + 2) { flatFrom };

            if (cells.Count == 1)
            {
                var centre = grid.ToWorld(goalCell);
                if (Vector3.Distance(flatFrom, centre) > SameCellCentreEpsilon) result.Add(centre);
            }
            else
            {
                // A start that had to be snapped (the mover stands in a now-blocked cell) walks to the nearest
                // walkable centre first, so the leg never cuts across the blocked cell's neighbours.
                if (startCell != grid.ToGrid(from)) result.Add(grid.ToWorld(startCell));
                for (var i = 1; i < cells.Count; i++) result.Add(grid.ToWorld(cells[i]));
            }

            corners = result.ToArray();
            return true;
        }

        private bool TryFindNearestWalkable(Vector3 point, float maxDistance, out GridCoordinate cell, HashSet<GridCoordinate> exclude = null)
        {
            var here = grid.ToGrid(point);
            if (grid.IsWalkable(here) && (exclude == null || !exclude.Contains(here)))
            {
                cell = here;
                return true;
            }

            cell = default;
            var found = false;
            var bestDistance = float.MaxValue;
            var reach = Mathf.CeilToInt(maxDistance / grid.CellSize) + 1;

            // Scan order (z outer, x inner) plus strict "<" makes ties deterministic.
            for (var dz = -reach; dz <= reach; dz++)
            {
                for (var dx = -reach; dx <= reach; dx++)
                {
                    var candidate = new GridCoordinate(here.X + dx, here.Z + dz);
                    if (!grid.IsWalkable(candidate) || (exclude != null && exclude.Contains(candidate))) continue;

                    var centre = grid.ToWorld(candidate);
                    var distance = Mathf.Sqrt((centre.x - point.x) * (centre.x - point.x) + (centre.z - point.z) * (centre.z - point.z));
                    if (distance > maxDistance || distance >= bestDistance) continue;

                    bestDistance = distance;
                    cell = candidate;
                    found = true;
                }
            }

            return found;
        }

        private bool TryFindCellPath(GridCoordinate start, GridCoordinate goal, out List<GridCoordinate> cells)
        {
            if (start == goal)
            {
                cells = new List<GridCoordinate> { start };
                return true;
            }

            if (stamp == int.MaxValue)
            {
                Array.Clear(gStamp, 0, gStamp.Length);
                Array.Clear(closedStamp, 0, closedStamp.Length);
                stamp = 0;
            }

            stamp++;
            open.Clear();

            var startIndex = grid.IndexOf(start);
            var goalIndex = grid.IndexOf(goal);
            gStamp[startIndex] = stamp;
            gScore[startIndex] = 0f;
            parent[startIndex] = -1;
            open.Push(startIndex, Heuristic(start, goal));

            var reached = false;
            while (open.Count > 0)
            {
                var node = open.Pop();
                if (closedStamp[node] == stamp) continue;
                closedStamp[node] = stamp;
                if (node == goalIndex)
                {
                    reached = true;
                    break;
                }

                var current = grid.FromIndex(node);
                foreach (var (dx, dz) in GridCoordinate.NeighbourOffsets)
                {
                    var next = new GridCoordinate(current.X + dx, current.Z + dz);
                    if (!grid.IsWalkable(next)) continue;

                    var diagonal = dx != 0 && dz != 0;
                    if (diagonal && (!grid.IsWalkable(new GridCoordinate(current.X + dx, current.Z)) || !grid.IsWalkable(new GridCoordinate(current.X, current.Z + dz)))) continue;

                    var nextIndex = grid.IndexOf(next);
                    if (closedStamp[nextIndex] == stamp) continue;

                    var cost = gScore[node] + (diagonal ? Sqrt2 : 1f) * grid.CellSize;
                    if (gStamp[nextIndex] == stamp && cost >= gScore[nextIndex]) continue;

                    gStamp[nextIndex] = stamp;
                    gScore[nextIndex] = cost;
                    parent[nextIndex] = node;
                    open.Push(nextIndex, cost + Heuristic(next, goal));
                }
            }

            if (!reached)
            {
                cells = null;
                return false;
            }

            cells = new List<GridCoordinate>();
            for (var index = goalIndex; index != -1; index = parent[index]) cells.Add(grid.FromIndex(index));
            cells.Reverse();
            return true;
        }

        // Octile distance: admissible and consistent for 8-connected movement with sqrt(2) diagonals.
        private float Heuristic(GridCoordinate a, GridCoordinate b)
        {
            var dx = Mathf.Abs(a.X - b.X);
            var dz = Mathf.Abs(a.Z - b.Z);
            return grid.CellSize * (Mathf.Max(dx, dz) + (Sqrt2 - 1f) * Mathf.Min(dx, dz));
        }

        /// <summary>Minimal binary min-heap keyed on f, ties broken by insertion order so searches are
        /// deterministic.</summary>
        private sealed class MinHeap
        {
            private struct Entry
            {
                public int Node;
                public float F;
                public int Sequence;
            }

            private readonly List<Entry> items = new();
            private int sequence;

            public int Count => items.Count;

            public void Clear()
            {
                items.Clear();
                sequence = 0;
            }

            public void Push(int node, float f)
            {
                items.Add(new Entry { Node = node, F = f, Sequence = sequence++ });
                var i = items.Count - 1;
                while (i > 0)
                {
                    var p = (i - 1) / 2;
                    if (!Less(items[i], items[p])) break;
                    (items[i], items[p]) = (items[p], items[i]);
                    i = p;
                }
            }

            public int Pop()
            {
                var top = items[0].Node;
                var last = items.Count - 1;
                items[0] = items[last];
                items.RemoveAt(last);

                var i = 0;
                while (true)
                {
                    var left = i * 2 + 1;
                    var right = left + 1;
                    var smallest = i;
                    if (left < items.Count && Less(items[left], items[smallest])) smallest = left;
                    if (right < items.Count && Less(items[right], items[smallest])) smallest = right;
                    if (smallest == i) break;
                    (items[i], items[smallest]) = (items[smallest], items[i]);
                    i = smallest;
                }

                return top;
            }

            private static bool Less(Entry a, Entry b) => a.F < b.F || (a.F == b.F && a.Sequence < b.Sequence);
        }
    }
}
