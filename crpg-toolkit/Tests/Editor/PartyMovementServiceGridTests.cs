using System.Collections.Generic;
using System.Linq;
using CRPG.Core;
using CRPG.Exploration;
using CRPG.Gameplay;
using CRPG.Navigation;
using CRPG.Navigation.Grid;
using CRPG.Party;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.AI;

namespace CRPG.Tests.EditMode
{
    /// <summary>
    /// Task 1.4: party formation resolution on a grid. Each member is resolved independently, and formation
    /// slots can be closer together than one cell, so without slot avoidance several members snap to the SAME
    /// cell and every one but the first is then blocked from ever arriving (occupancy: one character per cell).
    /// </summary>
    public class PartyMovementServiceGridTests
    {
        private static readonly Vector3 Origin = new(100f, 2f, -40f);

        private sealed class FakeSelectableSource : ISelectableSource
        {
            private readonly List<SelectableEntity> entities = new();
            public void Add(RuntimeEntityId id, Vector3 position) => entities.Add(new SelectableEntity(id, position));
            public IEnumerable<SelectableEntity> GetSelectables() => entities;
        }

        private sealed class PointsFormationSolver : IFormationSolver
        {
            private readonly Vector3[] points;
            public PointsFormationSolver(params Vector3[] points) => this.points = points;
            public IReadOnlyList<Vector3> Solve(Vector3 destination, Vector3 facing, int memberCount, float spacing) => points.Take(memberCount).ToArray();
        }

        /// <summary>Hides the slot-avoiding capability, like a NavMesh backend would -- the "before" behaviour.</summary>
        private sealed class PlainOnlyService : INavMeshService
        {
            private readonly INavMeshService inner;
            public PlainOnlyService(INavMeshService inner) => this.inner = inner;

            public bool TrySamplePosition(Vector3 point, out Vector3 sampledPosition, float maxDistance = 5f, int areaMask = NavMesh.AllAreas) =>
                inner.TrySamplePosition(point, out sampledPosition, maxDistance, areaMask);

            public bool TryCalculatePathLength(Vector3 from, Vector3 to, out float length, int areaMask = NavMesh.AllAreas) =>
                inner.TryCalculatePathLength(from, to, out length, areaMask);

            public bool TryCalculatePath(Vector3 from, Vector3 to, out Vector3[] corners, int areaMask = NavMesh.AllAreas) =>
                inner.TryCalculatePath(from, to, out corners, areaMask);
        }

        private static (GridPathfindingService service, WalkabilityGrid grid) MakeGrid(int width, int depth)
        {
            var grid = new WalkabilityGrid(Origin, 1f, width, depth);
            var service = new GridPathfindingService();
            service.SetActiveGrid(grid);
            return (service, grid);
        }

        private static RuntimeEntityId[] Members(int count) => Enumerable.Range(0, count).Select(_ => RuntimeEntityId.NewId()).ToArray();

        private static PartyMoveOrder CollapsingFourSlotOrder(WalkabilityGrid grid, RuntimeEntityId[] members, out FakeSelectableSource selectables)
        {
            selectables = new FakeSelectableSource();
            for (var i = 0; i < members.Length; i++) selectables.Add(members[i], grid.ToWorld(new GridCoordinate(1 + i, 1)));

            var centre = grid.ToWorld(new GridCoordinate(10, 10));
            // Four slots 0.1-0.3 apart: all inside one 1x1 cell.
            var solver = new PointsFormationSolver(
                centre, centre + new Vector3(0.1f, 0f, 0f), centre + new Vector3(-0.1f, 0f, 0.1f), centre + new Vector3(0.2f, 0f, -0.2f));
            return new PartyMoveOrder(members, centre, Vector3.forward, solver);
        }

        [Test]
        public void SlotsThatCollapseIntoOneCellResolveToDistinctFreeCellsAndOnlyTheFirstIsExact()
        {
            var (service, grid) = MakeGrid(20, 20);
            var members = Members(4);
            var order = CollapsingFourSlotOrder(grid, members, out var selectables);

            var resolved = new PartyMovementService(service, selectables).ResolveDestinations(order);

            var cells = members.Select(m => grid.ToGrid(resolved[m].ResolvedDestination.Value)).ToList();
            Assert.AreEqual(4, cells.Distinct().Count(), $"Every member needs its own cell, got: {string.Join(", ", cells)}");
            var centre = grid.ToWorld(new GridCoordinate(10, 10));
            foreach (var m in members)
            {
                Assert.AreNotEqual(FormationResolutionOutcome.Unreachable, resolved[m].Outcome);
                Assert.IsTrue(grid.IsWalkableWorld(resolved[m].ResolvedDestination.Value));
                Assert.LessOrEqual(Vector3.Distance(resolved[m].ResolvedDestination.Value, centre), 2f, "Displaced slots stay close to where the group is going.");
            }

            Assert.AreEqual(FormationResolutionOutcome.Exact, resolved[members[0]].Outcome);
            for (var i = 1; i < 4; i++)
                Assert.AreEqual(FormationResolutionOutcome.Fallback, resolved[members[i]].Outcome, "A displaced slot is a Fallback, not the slot as requested.");
        }

        [Test]
        public void WithoutSlotAvoidanceTheSameSlotsCollapseOntoOneCell()
        {
            // The control: what happened before this task, and what still happens on a backend without the capability.
            var (service, grid) = MakeGrid(20, 20);
            var members = Members(4);
            var order = CollapsingFourSlotOrder(grid, members, out var selectables);

            var resolved = new PartyMovementService(new PlainOnlyService(service), selectables).ResolveDestinations(order);

            var cells = members.Select(m => grid.ToGrid(resolved[m].ResolvedDestination.Value)).Distinct().ToList();
            Assert.AreEqual(1, cells.Count, "All four resolve to the same cell without avoidance -- three of them would then be blocked.");
        }

        [Test]
        public void ACharacterStandingStillOutsideTheOrderKeepsItsCell()
        {
            var (service, grid) = MakeGrid(12, 12);
            var mover = RuntimeEntityId.NewId();
            var bystander = RuntimeEntityId.NewId(); // an unselected party member or NPC
            var selectables = new FakeSelectableSource();
            selectables.Add(mover, grid.ToWorld(new GridCoordinate(1, 1)));
            selectables.Add(bystander, grid.ToWorld(new GridCoordinate(5, 5)));
            var request = grid.ToWorld(new GridCoordinate(5, 5));
            var order = new PartyMoveOrder(new[] { mover }, request, Vector3.forward, new PointsFormationSolver(request));

            var resolution = new PartyMovementService(service, selectables).ResolveDestinations(order)[mover];

            Assert.AreNotEqual(new GridCoordinate(5, 5), grid.ToGrid(resolution.ResolvedDestination.Value), "The bystander's cell must not be handed to the mover.");
            Assert.AreEqual(FormationResolutionOutcome.Fallback, resolution.Outcome);
        }

        [Test]
        public void ASlotWithNothingInTheWayStaysExactAtItsCellCentre()
        {
            var (service, grid) = MakeGrid(12, 12);
            var member = RuntimeEntityId.NewId();
            var selectables = new FakeSelectableSource();
            selectables.Add(member, grid.ToWorld(new GridCoordinate(1, 1)));
            var request = grid.ToWorld(new GridCoordinate(6, 6));
            var order = new PartyMoveOrder(new[] { member }, request, Vector3.forward, new PointsFormationSolver(request));

            var resolution = new PartyMovementService(service, selectables).ResolveDestinations(order)[member];

            Assert.AreEqual(FormationResolutionOutcome.Exact, resolution.Outcome);
            Assert.AreEqual(request, resolution.ResolvedDestination.Value);
        }

        [Test]
        public void AnUnreachableMembersCurrentCellIsNotHandedToALaterMember()
        {
            // A wall down x = 8 splits the map. A stands on the left but its slot is on the right (unreachable), so
            // A stays put -- and B, who asks for exactly A's cell, must not be sent onto it.
            var grid = new WalkabilityGrid(Origin, 1f, 12, 12, Enumerable.Range(0, 12).Select(z => new GridCoordinate(8, z)));
            var service = new GridPathfindingService();
            service.SetActiveGrid(grid);
            var a = RuntimeEntityId.NewId();
            var b = RuntimeEntityId.NewId();
            var selectables = new FakeSelectableSource();
            selectables.Add(a, grid.ToWorld(new GridCoordinate(2, 5)));
            selectables.Add(b, grid.ToWorld(new GridCoordinate(4, 5)));
            var slotA = grid.ToWorld(new GridCoordinate(10, 5));
            var slotB = grid.ToWorld(new GridCoordinate(2, 5)); // A's own cell
            var order = new PartyMoveOrder(new[] { a, b }, slotA, Vector3.forward, new PointsFormationSolver(slotA, slotB));

            var resolved = new PartyMovementService(service, selectables).ResolveDestinations(order);

            Assert.AreEqual(FormationResolutionOutcome.Unreachable, resolved[a].Outcome);
            Assert.AreNotEqual(FormationResolutionOutcome.Unreachable, resolved[b].Outcome);
            Assert.AreNotEqual(new GridCoordinate(2, 5), grid.ToGrid(resolved[b].ResolvedDestination.Value), "A is staying in that cell.");
            Assert.AreEqual(FormationResolutionOutcome.Fallback, resolved[b].Outcome);
        }

        [Test]
        public void MembersInsideTheOrderAreNotTreatedAsStandingInTheWayOfTheirTeammates()
        {
            // A party stepping forward one cell: each member's slot is the cell its teammate is leaving. Pre-seeding
            // the in-order members' current cells would displace everyone; they must all keep their exact slots.
            var (service, grid) = MakeGrid(12, 12);
            var members = Members(3);
            var selectables = new FakeSelectableSource();
            var slots = new Vector3[3];
            for (var i = 0; i < 3; i++)
            {
                selectables.Add(members[i], grid.ToWorld(new GridCoordinate(2 + i, 5)));
                slots[i] = grid.ToWorld(new GridCoordinate(3 + i, 5)); // one cell forward = the next member's current cell
            }

            var order = new PartyMoveOrder(members, slots[1], Vector3.forward, new PointsFormationSolver(slots));

            var resolved = new PartyMovementService(service, selectables).ResolveDestinations(order);

            for (var i = 0; i < 3; i++)
            {
                Assert.AreEqual(FormationResolutionOutcome.Exact, resolved[members[i]].Outcome);
                Assert.AreEqual(slots[i], resolved[members[i]].ResolvedDestination.Value);
            }
        }

        [Test]
        public void WhenNoFreeSpotExistsWithinRangeTheMemberIsReportedUnreachableNotStackedOnSomeoneElse()
        {
            var grid = new WalkabilityGrid(Origin, 1f, 2, 1); // exactly two cells
            var service = new GridPathfindingService();
            service.SetActiveGrid(grid);
            var mover = RuntimeEntityId.NewId();
            var selectables = new FakeSelectableSource();
            selectables.Add(mover, new Vector3(Origin.x + 50f, Origin.y, Origin.z)); // off the grid
            selectables.Add(RuntimeEntityId.NewId(), grid.ToWorld(new GridCoordinate(0, 0)));
            selectables.Add(RuntimeEntityId.NewId(), grid.ToWorld(new GridCoordinate(1, 0)));
            var request = grid.ToWorld(new GridCoordinate(0, 0));
            var order = new PartyMoveOrder(new[] { mover }, request, Vector3.forward, new PointsFormationSolver(request));

            var resolution = new PartyMovementService(service, selectables).ResolveDestinations(order)[mover];

            Assert.AreEqual(FormationResolutionOutcome.Unreachable, resolution.Outcome);
            Assert.IsFalse(resolution.ResolvedDestination.HasValue);
        }
    }
}
