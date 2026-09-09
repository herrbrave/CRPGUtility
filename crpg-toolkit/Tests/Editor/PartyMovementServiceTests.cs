using CRPG.Core;
using CRPG.Exploration;
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

            public bool TrySamplePosition(Vector3 point, out Vector3 sampledPosition, float maxDistance = 5f, int areaMask = -1)
            {
                sampledPosition = point + SampledOffset;
                return SamplingSucceeds;
            }

            public bool TryCalculatePathLength(Vector3 from, Vector3 to, out float length, int areaMask = -1)
            {
                length = Vector3.Distance(from, to);
                return true;
            }
        }

        [Test]
        public void ResolveDestinationsUsesSampledPositionWhenSamplingSucceeds()
        {
            var navMesh = new FakeNavMeshService { SamplingSucceeds = true, SampledOffset = new Vector3(0f, 0.5f, 0f) };
            var service = new PartyMovementService(navMesh);
            var members = new[] { RuntimeEntityId.NewId() };
            var order = new PartyMoveOrder(members, new Vector3(1f, 0f, 1f), Vector3.forward, new ClusterFormationSolver());

            var resolved = service.ResolveDestinations(order);

            var expected = order.Assignments[members[0]] + new Vector3(0f, 0.5f, 0f);
            Assert.AreEqual(expected, resolved[members[0]]);
        }

        [Test]
        public void ResolveDestinationsFallsBackToRawPointWhenSamplingFails()
        {
            var navMesh = new FakeNavMeshService { SamplingSucceeds = false };
            var service = new PartyMovementService(navMesh);
            var members = new[] { RuntimeEntityId.NewId() };
            var order = new PartyMoveOrder(members, new Vector3(2f, 0f, 2f), Vector3.forward, new ClusterFormationSolver());

            var resolved = service.ResolveDestinations(order);

            Assert.AreEqual(order.Assignments[members[0]], resolved[members[0]]);
        }
    }
}
