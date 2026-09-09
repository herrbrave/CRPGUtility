using System;
using CRPG.Core;
using CRPG.Exploration;
using CRPG.Navigation;
using CRPG.Party;
using NUnit.Framework;
using UnityEngine;

namespace CRPG.Tests.EditMode
{
    public class PartyExplorationControllerTests
    {
        private sealed class FakeCommandSource : IPlayerCommandSource
        {
            public event Action<SelectCommand> Select;
            public event Action<MoveCommand> Move;

            public void RaiseSelect(SelectCommand command) => Select?.Invoke(command);
            public void RaiseMove(MoveCommand command) => Move?.Invoke(command);
        }

        private sealed class FakeNavMeshService : INavMeshService
        {
            public bool TrySamplePosition(Vector3 point, out Vector3 sampledPosition, float maxDistance = 5f, int areaMask = -1)
            {
                sampledPosition = point;
                return true;
            }

            public bool TryCalculatePathLength(Vector3 from, Vector3 to, out float length, int areaMask = -1)
            {
                length = Vector3.Distance(from, to);
                return true;
            }
        }

        private sealed class FakeMovable : IMovable
        {
            public Vector3? LastDestination;
            public void SetDestination(Vector3 destination) => LastDestination = destination;
            public void Stop() => LastDestination = null;
            public bool HasArrived => true;
            public float RemainingDistance => 0f;
        }

        private static PartyExplorationController CreateController(PartyState party, FakeCommandSource commandSource) =>
            new(party, new PartyMovementService(new FakeNavMeshService()), new LineFormationSolver(), commandSource);

        [Test]
        public void MoveCommandSendsEachSelectedMemberToADistinctFormationPoint()
        {
            var fighter = RuntimeEntityId.NewId();
            var rogue = RuntimeEntityId.NewId();
            var party = new PartyState(new[] { fighter, rogue }); // both selected by default
            var commandSource = new FakeCommandSource();
            var controller = CreateController(party, commandSource);

            var fighterMotor = new FakeMovable();
            var rogueMotor = new FakeMovable();
            controller.RegisterMotor(fighter, fighterMotor);
            controller.RegisterMotor(rogue, rogueMotor);

            commandSource.RaiseMove(new MoveCommand(new Vector3(10f, 0f, 10f), Vector3.forward));

            Assert.IsNotNull(fighterMotor.LastDestination);
            Assert.IsNotNull(rogueMotor.LastDestination);
            Assert.AreNotEqual(fighterMotor.LastDestination, rogueMotor.LastDestination);
        }

        [Test]
        public void SelectCommandReplaceWithOneCharacterNarrowsSelection()
        {
            var fighter = RuntimeEntityId.NewId();
            var rogue = RuntimeEntityId.NewId();
            var party = new PartyState(new[] { fighter, rogue });
            var commandSource = new FakeCommandSource();
            CreateController(party, commandSource);

            commandSource.RaiseSelect(new SelectCommand(new[] { rogue }, SelectionMode.Replace));

            Assert.AreEqual(1, party.Selected.Count);
            Assert.IsTrue(party.IsSelected(rogue));
        }

        [Test]
        public void SelectCommandToggleFlipsMembership()
        {
            var fighter = RuntimeEntityId.NewId();
            var party = new PartyState(new[] { fighter }); // starts selected
            var commandSource = new FakeCommandSource();
            CreateController(party, commandSource);

            commandSource.RaiseSelect(new SelectCommand(new[] { fighter }, SelectionMode.Toggle));

            Assert.IsFalse(party.IsSelected(fighter));
        }
    }
}
