using CRPG.Core;
using CRPG.Party;
using NUnit.Framework;
using UnityEngine;

namespace CRPG.Tests.EditMode
{
    public class FormationSolverTests
    {
        private static readonly Vector3 Destination = new Vector3(10f, 0f, 5f);
        private static readonly Vector3 Facing = Vector3.forward;

        [Test]
        public void ClusterSolverPlacesFirstMemberExactlyAtDestination()
        {
            var solver = new ClusterFormationSolver();

            var points = solver.Solve(Destination, Facing, memberCount: 4, spacing: 1.5f);

            Assert.AreEqual(4, points.Count);
            Assert.AreEqual(Destination, points[0]);
        }

        [Test]
        public void LineSolverIsSymmetricAroundTheDestination()
        {
            var solver = new LineFormationSolver();

            var points = solver.Solve(Destination, Facing, memberCount: 3, spacing: 2f);

            Assert.AreEqual(3, points.Count);
            var center = (points[0] + points[2]) / 2f;
            Assert.Less(Vector3.Distance(center, points[1]), 0.001f);
            Assert.Less(Vector3.Distance(points[1], Destination), 0.001f);
        }

        [Test]
        public void ColumnSolverSpacesMembersBehindTheDestination()
        {
            var solver = new ColumnFormationSolver();

            var points = solver.Solve(Destination, Facing, memberCount: 3, spacing: 2f);

            Assert.AreEqual(Destination, points[0]);
            Assert.AreEqual(2f, Vector3.Distance(points[0], points[1]), 0.001f);
            Assert.AreEqual(4f, Vector3.Distance(points[0], points[2]), 0.001f);
        }

        [Test]
        public void WedgeSolverPlacesFirstMemberAtDestination()
        {
            var solver = new WedgeFormationSolver();

            var points = solver.Solve(Destination, Facing, memberCount: 5, spacing: 1.5f);

            Assert.AreEqual(5, points.Count);
            Assert.AreEqual(Destination, points[0]);
        }

        [Test]
        public void PartyMoveOrderAssignsEachMemberADistinctFormationPoint()
        {
            var members = new[] { RuntimeEntityId.NewId(), RuntimeEntityId.NewId(), RuntimeEntityId.NewId() };

            var order = new PartyMoveOrder(members, Destination, Facing, new LineFormationSolver(), spacing: 2f);

            Assert.AreEqual(3, order.Assignments.Count);
            foreach (var member in members) Assert.IsTrue(order.Assignments.ContainsKey(member));
        }
    }
}
