using System.Collections.Generic;
using CRPG.Core;
using CRPG.Exploration;
using CRPG.Gameplay;
using CRPG.Navigation;
using CRPG.Party;
using NUnit.Framework;
using UnityEngine;

namespace CRPG.Tests.EditMode
{
    public class PartyMovementServiceTests
    {
        private sealed class FakeNavMeshService : INavMeshService
        {
            public bool SamplingSucceeds = true;
            public Vector3 SampledOffset = Vector3.zero;

            /// <summary>When set, TryCalculatePathLength only succeeds for candidates whose sampled position
            /// is within this distance of AcceptOnlyNear -- lets a test force early retry attempts to fail
            /// (simulating an unreachable slot) while a later, more-compressed-toward-destination attempt
            /// succeeds.</summary>
            public Vector3? AcceptOnlyNear;
            public float AcceptRadius = 0.5f;
            public bool PathLengthAlwaysFails;

            public bool TrySamplePosition(Vector3 point, out Vector3 sampledPosition, float maxDistance = 5f, int areaMask = -1)
            {
                sampledPosition = point + SampledOffset;
                return SamplingSucceeds;
            }

            public bool TryCalculatePathLength(Vector3 from, Vector3 to, out float length, int areaMask = -1)
            {
                length = Vector3.Distance(from, to);
                if (PathLengthAlwaysFails) return false;
                if (AcceptOnlyNear.HasValue) return Vector3.Distance(to, AcceptOnlyNear.Value) <= AcceptRadius;
                return true;
            }

            public bool TryCalculatePath(Vector3 from, Vector3 to, out Vector3[] corners, int areaMask = -1)
            {
                corners = new[] { from, to };
                if (PathLengthAlwaysFails) return false;
                if (AcceptOnlyNear.HasValue) return Vector3.Distance(to, AcceptOnlyNear.Value) <= AcceptRadius;
                return true;
            }
        }

        private sealed class FakeSelectableSource : ISelectableSource
        {
            private readonly List<SelectableEntity> entities = new();
            public int CallCount { get; private set; }

            public void Add(RuntimeEntityId id, Vector3 position) => entities.Add(new SelectableEntity(id, position));

            public IEnumerable<SelectableEntity> GetSelectables()
            {
                CallCount++;
                return entities;
            }
        }

        [Test]
        public void AttemptZeroSucceedingReportsExact()
        {
            var navMesh = new FakeNavMeshService { SamplingSucceeds = true, SampledOffset = new Vector3(0f, 0.5f, 0f) };
            var member = RuntimeEntityId.NewId();
            var selectables = new FakeSelectableSource();
            selectables.Add(member, Vector3.zero);
            var service = new PartyMovementService(navMesh, selectables);
            var order = new PartyMoveOrder(new[] { member }, new Vector3(1f, 0f, 1f), Vector3.forward, new ClusterFormationSolver());

            var resolved = service.ResolveDestinations(order);

            var resolution = resolved[member];
            Assert.AreEqual(FormationResolutionOutcome.Exact, resolution.Outcome);
            Assert.AreEqual(order.Assignments[member] + new Vector3(0f, 0.5f, 0f), resolution.ResolvedDestination);
        }

        [Test]
        public void ALaterAttemptSucceedingReportsFallbackCompressedTowardTheSharedDestination()
        {
            var member = RuntimeEntityId.NewId();
            var requested = new Vector3(10f, 0f, 0f);
            var sharedDestination = new Vector3(0f, 0f, 0f);

            // Only a candidate near the shared destination is reachable -- the original slot (requested) is
            // far from it, so attempt 0 must fail and a later, more-compressed attempt must succeed.
            var navMesh = new FakeNavMeshService { AcceptOnlyNear = sharedDestination, AcceptRadius = 3f };
            var selectables = new FakeSelectableSource();
            selectables.Add(member, Vector3.zero);
            var service = new PartyMovementService(navMesh, selectables);
            var order = new PartyMoveOrder(new[] { member }, sharedDestination, Vector3.forward, new SingleMemberFormationSolver(requested));

            var resolved = service.ResolveDestinations(order);

            var resolution = resolved[member];
            Assert.AreEqual(FormationResolutionOutcome.Fallback, resolution.Outcome);
            Assert.Less(Vector3.Distance(resolution.ResolvedDestination.Value, sharedDestination), 3f);
        }

        [Test]
        public void ExhaustingTheRetryBudgetReportsUnreachableWithNoResolvedDestination()
        {
            var navMesh = new FakeNavMeshService { PathLengthAlwaysFails = true };
            var member = RuntimeEntityId.NewId();
            var selectables = new FakeSelectableSource();
            selectables.Add(member, Vector3.zero);
            var service = new PartyMovementService(navMesh, selectables);
            var order = new PartyMoveOrder(new[] { member }, new Vector3(2f, 0f, 2f), Vector3.forward, new ClusterFormationSolver());

            var resolved = service.ResolveDestinations(order);

            var resolution = resolved[member];
            Assert.AreEqual(FormationResolutionOutcome.Unreachable, resolution.Outcome);
            Assert.IsFalse(resolution.ResolvedDestination.HasValue);
        }

        [Test]
        public void AMemberMissingFromTheSelectableSourceReportsUnreachableWithoutQueryingTheNavMesh()
        {
            var navMesh = new FakeNavMeshService();
            var member = RuntimeEntityId.NewId();
            var selectables = new FakeSelectableSource(); // deliberately never Add()'d -- simulates a destroyed/unspawned member
            var service = new PartyMovementService(navMesh, selectables);
            var order = new PartyMoveOrder(new[] { member }, new Vector3(2f, 0f, 2f), Vector3.forward, new ClusterFormationSolver());

            var resolved = service.ResolveDestinations(order);

            Assert.AreEqual(FormationResolutionOutcome.Unreachable, resolved[member].Outcome);
        }

        [Test]
        public void GetSelectablesIsCalledExactlyOncePerResolveDestinationsCallRegardlessOfPartySize()
        {
            var navMesh = new FakeNavMeshService();
            var members = new[] { RuntimeEntityId.NewId(), RuntimeEntityId.NewId(), RuntimeEntityId.NewId() };
            var selectables = new FakeSelectableSource();
            foreach (var member in members) selectables.Add(member, Vector3.zero);
            var service = new PartyMovementService(navMesh, selectables);
            var order = new PartyMoveOrder(members, new Vector3(2f, 0f, 2f), Vector3.forward, new ClusterFormationSolver());

            service.ResolveDestinations(order);

            Assert.AreEqual(1, selectables.CallCount);
        }

        // A single-point "formation" -- every member gets exactly the same requested point, so a test can
        // pin down precisely which candidate position gets checked without a real solver's spacing math.
        private sealed class SingleMemberFormationSolver : IFormationSolver
        {
            private readonly Vector3 point;
            public SingleMemberFormationSolver(Vector3 point) => this.point = point;

            public System.Collections.Generic.IReadOnlyList<Vector3> Solve(Vector3 destination, Vector3 facing, int memberCount, float spacing) =>
                new[] { point };
        }
    }
}
