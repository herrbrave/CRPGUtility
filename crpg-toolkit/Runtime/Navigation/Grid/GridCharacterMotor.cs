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
    /// claims a corner's cell BEFORE starting toward it) and lets go of the cell behind it the moment it crosses
    /// the boundary. A failed claim first WAITS (<see cref="ClaimWaitSeconds"/>, scaled time): the occupant is often
    /// just a mover in the middle of leaving that cell -- a party walking in single file has every follower one
    /// cell behind the next -- and head-on movers must still resolve rather than hang. Only after the wait does it
    /// re-plan around whoever is standing in the way (bounded), and only if no other route exists stop where it is
    /// and report <see cref="MoveStatus.Blocked"/> -- the same vocabulary as an unreachable NavMesh leg. Claims live on
    /// the active <see cref="WalkabilityGrid"/>, so they are scoped to one location and re-made automatically when the grid is swapped by scene travel. Released on disable/destroy.</para>
    /// </summary>
    public sealed class GridCharacterMotor : MonoBehaviour, IMovable
    {
        private const float SameSpotEpsilon = 1e-4f;
        private const int MaxReplans = 3;

        [SerializeField] private float speed = 4f;

        [Tooltip("How long (scaled seconds) to wait for an occupied next cell to free up before giving up as Blocked.")]
        [SerializeField] private float claimWaitSeconds = 0.5f;

        private readonly Queue<Vector3> corners = new();
        private readonly List<GridCoordinate> heldCells = new();
        private WalkabilityGrid heldGrid;
        private MoveRequestId currentId = MoveRequestId.None;
        private bool cancelled;
        private bool blocked;
        private bool segmentClaimed;
        private float claimWaited;
        private Vector3 finalDestination;
        private int replans;
        private readonly HashSet<GridCoordinate> avoidedCells = new(); // scratch for TryReplanAroundOccupants
        private Vector3 velocity;
        private RuntimeEntityId occupantId = RuntimeEntityId.None;

        /// <summary>Units per second along the path.</summary>
        public float Speed
        {
            get => speed;
            set => speed = value;
        }

        /// <summary>How long to wait for an occupied next cell to free up before reporting
        /// <see cref="MoveStatus.Blocked"/>. 0 blocks immediately.</summary>
        public float ClaimWaitSeconds
        {
            get => claimWaitSeconds;
            set => claimWaitSeconds = value;
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
            SettleHeldCells(grid);

            LoadCorners(grid, path, destination);
            finalDestination = destination;
            replans = 0;
            segmentClaimed = false;
            claimWaited = 0f;
            currentId = MoveRequestId.NewId();
            cancelled = false;
            blocked = false;
            return new MoveResult(MoveStatus.Pending, currentId);
        }

        /// <summary>Replaces the queued corners with <paramref name="path"/> minus its leading "from". The final
        /// corner becomes the exact requested point -- unless the request was snapped (it was inside a wall), in
        /// which case the snapped cell centre stands.</summary>
        private void LoadCorners(WalkabilityGrid grid, Vector3[] path, Vector3 destination)
        {
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
        }

        /// <summary>After the wait for an occupied cell runs out: route around everyone currently standing in the
        /// way, toward the request's original destination. Bounded, and refuses (so the caller reports Blocked) when
        /// there is no other way -- including when the destination cell itself is the one occupied.</summary>
        private bool TryReplanAroundOccupants(WalkabilityGrid grid)
        {
            if (replans >= MaxReplans) return false;

            avoidedCells.Clear();
            grid.CollectOccupiedCells(OccupantId, avoidedCells);
            if (!Pathfinding.TryCalculatePathAvoiding(transform.position, finalDestination, out var path, avoidedCells)) return false;

            LoadCorners(grid, path, finalDestination);
            replans++;
            segmentClaimed = false;
            claimWaited = 0f;
            return true;
        }

        public void Stop()
        {
            corners.Clear();
            velocity = Vector3.zero;
            segmentClaimed = false;
            claimWaited = 0f;
            if (currentId.IsValid) cancelled = true;

            var grid = ActiveGrid;
            if (grid != null) SettleHeldCells(grid);
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
            claimWaited = 0f;
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
                            // Someone is in that cell -- often a mover mid-way out of it. Wait (idle, velocity zero)
                            // for it to free up. Only after ClaimWaitSeconds: try a route around whoever is standing
                            // in the way, and only if there is none give up -- stop where we are, holding only what
                            // we already held.
                            claimWaited += Time.deltaTime;
                            velocity = Vector3.zero;
                            if (claimWaited >= claimWaitSeconds && !TryReplanAroundOccupants(grid))
                            {
                                corners.Clear();
                                blocked = true;
                            }

                            return;
                        }

                        heldCells.Add(targetCell);
                    }

                    claimWaited = 0f;
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
                ReleaseCellsBehind(grid, grid.ToGrid(target));
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

        /// <summary>Lets go of every held cell that is neither the one we are physically in nor the one we are
        /// heading into -- so a follower can enter the cell we have just crossed out of.</summary>
        private void ReleaseCellsBehind(WalkabilityGrid grid, GridCoordinate heading)
        {
            if (heldGrid != grid) return;

            var here = grid.ToGrid(transform.position);
            for (var i = heldCells.Count - 1; i >= 0; i--)
            {
                if (heldCells[i] == here || heldCells[i] == heading) continue;
                heldGrid.Release(heldCells[i], OccupantId);
                heldCells.RemoveAt(i);
            }
        }

        /// <summary>Narrows what we hold down to the cell we are physically standing in. A diagonal hop to an
        /// off-centre point can transit a corner cell we never claimed; if that is where a Stop/new request finds us,
        /// claim it -- and if someone else holds it, keep what we already hold rather than dropping every claim and
        /// standing in a cell nobody reserved for us.</summary>
        private void SettleHeldCells(WalkabilityGrid grid)
        {
            if (heldGrid != grid) return;

            var here = grid.ToGrid(transform.position);
            if (!heldCells.Contains(here))
            {
                if (!grid.TryOccupy(here, OccupantId)) return;
                heldCells.Add(here);
            }

            ReleaseAllExcept(here);
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
