using System.Collections.Generic;
using CRPG.Core;
using UnityEngine;

namespace CRPG.Navigation.Grid
{
    /// <summary>
    /// Grid-backed <see cref="IMovable"/>. Every <see cref="SetDestination"/> plans its own path through the
    /// ambient <see cref="GridPathfindingService"/> -- exactly like NavMeshAgent pathing internally -- so an
    /// unplanned long-distance click (PartyExplorationController) and a one-cell leg issued by
    /// MoveActionExecution are handled identically. It then follows the corner list at a fixed speed, snapping
    /// EXACTLY onto each corner on arrival (never "close enough": residual drift would push MoveActionExecution's
    /// cumulative Mathf.CeilToInt movement accounting over an integer boundary and cost a full square).
    /// <para>Position is never cached separately from <c>transform.position</c>, so there is no internal state
    /// that can desync from the transform (the NavMeshAgent quirk SessionLoadCoordinator's restore-time Warp
    /// works around simply cannot happen here). Movement is driven by scaled <c>Time.deltaTime</c> only, so
    /// <c>Time.timeScale = 0</c> is a true pause.</para>
    /// <para>Occupancy: a mover holds the cell it stands in, plus the next cell while travelling into it (it
    /// claims a corner's cell BEFORE starting toward it). If the claim fails the mover stops where it is and
    /// reports <see cref="MoveStatus.Blocked"/> -- the same vocabulary as an unreachable NavMesh leg. Claims live
    /// on the active <see cref="WalkabilityGrid"/>, so they are scoped to one location and re-made
    /// automatically when the grid is swapped by scene travel. Released on disable/destroy.</para>
    /// </summary>
    public sealed class GridCharacterMotor : MonoBehaviour, IMovable
    {
        private const float SameSpotEpsilon = 1e-4f;

        [SerializeField] private float speed = 4f;

        private readonly Queue<Vector3> corners = new();
        private readonly List<GridCoordinate> heldCells = new();
        private WalkabilityGrid heldGrid;
        private MoveRequestId currentId = MoveRequestId.None;
        private bool cancelled;
        private bool blocked;
        private bool segmentClaimed;
        private Vector3 velocity;
        private RuntimeEntityId occupantId = RuntimeEntityId.None;

        /// <summary>Units per second along the path.</summary>
        public float Speed
        {
            get => speed;
            set => speed = value;
        }

        /// <summary>The pathfinder (and, through it, the active per-location grid). Supplied by the composition
        /// root -- WorldSpawnService hands it to every spawned motor at bind time -- never a static lookup.</summary>
        public GridPathfindingService Pathfinding { get; set; }

        /// <summary>Who holds this motor's cells on the grid. WorldSpawnService sets it to the character's entity
        /// id; if nothing does, a unique id is generated so occupancy still works.</summary>
        public RuntimeEntityId OccupantId
        {
            get => occupantId == RuntimeEntityId.None ? occupantId = RuntimeEntityId.NewId() : occupantId;
            set => occupantId = value;
        }

        private WalkabilityGrid ActiveGrid => Pathfinding != null ? Pathfinding.ActiveGrid : null;

        public MoveResult SetDestination(Vector3 destination)
        {
            var grid = ActiveGrid;
            if (grid == null || !isActiveAndEnabled) return MoveResult.Invalid();
            if (!Pathfinding.TryCalculatePath(transform.position, destination, out var path)) return MoveResult.Invalid();

            EnsureHeld(grid);
            ReleaseAllExcept(grid.ToGrid(transform.position));

            // Drop the leading "from". The final corner becomes the exact requested point -- unless the request
            // was snapped (it was inside a wall), in which case the snapped cell centre stands.
            var planned = new List<Vector3>(path.Length);
            for (var i = 1; i < path.Length; i++) planned.Add(path[i]);

            var exact = new Vector3(destination.x, grid.GroundY, destination.z);
            var destinationCell = grid.ToGrid(destination);
            if (grid.IsWalkable(destinationCell))
            {
                if (planned.Count > 0)
                {
                    if (grid.ToGrid(planned[planned.Count - 1]) == destinationCell) planned[planned.Count - 1] = exact;
                }
                else if (Vector3.Distance(new Vector3(transform.position.x, grid.GroundY, transform.position.z), exact) > SameSpotEpsilon)
                {
                    planned.Add(exact); // already on the centre of the destination cell, but the point is elsewhere in it
                }
            }

            corners.Clear();
            foreach (var corner in planned) corners.Enqueue(corner);
            segmentClaimed = false;
            currentId = MoveRequestId.NewId();
            cancelled = false;
            blocked = false;
            return new MoveResult(MoveStatus.Pending, currentId);
        }

        public void Stop()
        {
            corners.Clear();
            velocity = Vector3.zero;
            segmentClaimed = false;
            if (currentId.IsValid) cancelled = true;

            var grid = ActiveGrid;
            if (grid != null && heldGrid == grid) ReleaseAllExcept(grid.ToGrid(transform.position));
        }

        /// <summary>Refuses (leaving the mover, its claims and its request completely untouched) unless
        /// <paramref name="position"/> is on a walkable, unoccupied cell of the active grid. No snapping: unlike a
        /// NavMesh, a grid has nothing "near but off" to snap to -- callers (scene travel) pass already-sampled cell
        /// centres.</summary>
        public bool Warp(Vector3 position)
        {
            var grid = ActiveGrid;
            if (grid == null || !isActiveAndEnabled) return false;

            var cell = grid.ToGrid(position);
            if (!grid.IsWalkable(cell) || !grid.TryOccupy(cell, OccupantId)) return false;

            // The claim succeeded: only now let go of whatever was held before (possibly in another grid).
            if (heldGrid != grid) ReleaseAll();
            heldGrid = grid;
            if (!heldCells.Contains(cell)) heldCells.Add(cell);
            ReleaseAllExcept(cell);

            transform.position = new Vector3(position.x, grid.GroundY, position.z);
            corners.Clear();
            velocity = Vector3.zero;
            segmentClaimed = false;
            blocked = false;
            if (currentId.IsValid) cancelled = true;
            return true;
        }

        public MotionSnapshot Snapshot
        {
            get
            {
                if (!currentId.IsValid) return MotionSnapshot.None;

                var grid = ActiveGrid;
                var onGrid = grid != null && grid.IsWalkableWorld(transform.position);
                if (cancelled) return new MotionSnapshot(Vector3.zero, Vector3.zero, onGrid, 0f, MoveStatus.Cancelled, currentId);
                if (blocked) return new MotionSnapshot(Vector3.zero, Vector3.zero, onGrid, 0f, MoveStatus.Blocked, currentId);
                if (corners.Count == 0) return new MotionSnapshot(Vector3.zero, Vector3.zero, onGrid, 0f, MoveStatus.Arrived, currentId);
                return new MotionSnapshot(velocity, velocity, onGrid, RemainingDistance(), MoveStatus.Moving, currentId);
            }
        }

        private float RemainingDistance()
        {
            var total = 0f;
            var from = transform.position;
            foreach (var corner in corners)
            {
                total += Vector3.Distance(from, corner);
                from = corner;
            }

            return total;
        }

        private void Update()
        {
            var grid = ActiveGrid;
            if (grid != null) EnsureHeld(grid);

            velocity = Vector3.zero;
            if (grid == null || Time.deltaTime <= 0f || cancelled || blocked || corners.Count == 0) return; // paused/idle: a true no-op

            var budget = speed * Time.deltaTime; // scaled time only
            while (corners.Count > 0)
            {
                var target = corners.Peek();

                if (!segmentClaimed)
                {
                    var targetCell = grid.ToGrid(target);
                    if (!heldCells.Contains(targetCell))
                    {
                        if (!grid.TryOccupy(targetCell, OccupantId))
                        {
                            // Someone is standing there: stop where we are, holding only what we already held.
                            corners.Clear();
                            blocked = true;
                            velocity = Vector3.zero;
                            return;
                        }

                        heldCells.Add(targetCell);
                    }

                    segmentClaimed = true;
                }

                var toTarget = target - transform.position;
                toTarget.y = 0f;
                var distance = toTarget.magnitude;
                var direction = distance > 0f ? toTarget / distance : Vector3.zero;

                if (distance <= budget)
                {
                    transform.position = target; // exact -- see the class remarks
                    budget -= distance;
                    corners.Dequeue();
                    segmentClaimed = false;
                    ReleaseAllExcept(grid.ToGrid(target));
                    velocity = direction * speed;
                    continue;
                }

                transform.position += direction * budget;
                velocity = direction * speed;
                break;
            }
        }

        private void OnDisable() => ReleaseAll();

        private void OnDestroy() => ReleaseAll();

        /// <summary>Makes sure the cell we stand in is held on the CURRENT grid: covers first activation, a
        /// re-enable, and scene travel swapping the grid out from under us.</summary>
        private void EnsureHeld(WalkabilityGrid grid)
        {
            if (heldGrid == grid && heldCells.Count > 0) return;

            ReleaseAll();
            var cell = grid.ToGrid(transform.position);
            if (!grid.IsInBounds(cell) || !grid.TryOccupy(cell, OccupantId)) return; // retried next frame

            heldGrid = grid;
            heldCells.Add(cell);
        }

        private void ReleaseAll()
        {
            if (heldGrid != null)
            {
                foreach (var cell in heldCells) heldGrid.Release(cell, OccupantId);
            }

            heldCells.Clear();
            heldGrid = null;
        }

        private void ReleaseAllExcept(GridCoordinate keep)
        {
            if (heldGrid == null) return;

            for (var i = heldCells.Count - 1; i >= 0; i--)
            {
                if (heldCells[i] == keep) continue;
                heldGrid.Release(heldCells[i], OccupantId);
                heldCells.RemoveAt(i);
            }
        }
    }
}
