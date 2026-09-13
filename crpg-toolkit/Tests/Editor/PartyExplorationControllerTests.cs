using System;
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
    public class PartyExplorationControllerTests
    {
        private sealed class FakeCommandSource : IPlayerCommandSource
        {
            public event Action<SelectCommand> Select;
            public event Action<MoveCommand> Move;
            public event Action<InteractCommand> Interact;
            public event Action Cancel;

            public void RaiseSelect(SelectCommand command) => Select?.Invoke(command);
            public void RaiseMove(MoveCommand command) => Move?.Invoke(command);
            public void RaiseInteract(InteractCommand command) => Interact?.Invoke(command);
            public void RaiseCancel() => Cancel?.Invoke();
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

            public MoveResult SetDestination(Vector3 destination)
            {
                LastDestination = destination;
                return new MoveResult(MoveStatus.Pending, MoveRequestId.NewId());
            }

            public void Stop() => LastDestination = null;
            public MotionSnapshot Snapshot => MotionSnapshot.None;
        }

        private sealed class FakeSelectableSource : ISelectableSource
        {
            private readonly List<SelectableEntity> entities = new();

            public void Add(RuntimeEntityId id, Vector3 position) => entities.Add(new SelectableEntity(id, position));

            public IEnumerable<SelectableEntity> GetSelectables() => entities;
        }

        // Every party member gets a placeholder position (origin) so PartyMovementService's reachability
        // check always has a known starting point for these tests -- none of them care about real distances,
        // only whether movement was routed at all.
        private static PartyExplorationController CreateController(PartyState party, FakeCommandSource commandSource)
        {
            var selectableSource = new FakeSelectableSource();
            foreach (var member in party.Members) selectableSource.Add(member, Vector3.zero);

            return new PartyExplorationController(
                party, new PartyMovementService(new FakeNavMeshService(), selectableSource), new LineFormationSolver(), commandSource);
        }

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

        // --- Task 1.1 (Production Foundation): PartyExplorationController subscribed to its IPlayerCommandSource
        // in the constructor with no disposal path at all (Docs/Design/Production-Foundation.md's gap table:
        // "session rebuild can retain command handlers"). These tests require Dispose() to exist.

        [Test]
        public void DisposedControllerDoesNotReceiveFurtherMoveCommands()
        {
            var fighter = RuntimeEntityId.NewId();
            var party = new PartyState(new[] { fighter });
            var commandSource = new FakeCommandSource();
            var controller = CreateController(party, commandSource);
            var motor = new FakeMovable();
            controller.RegisterMotor(fighter, motor);

            controller.Dispose();
            commandSource.RaiseMove(new MoveCommand(new Vector3(5f, 0f, 5f), Vector3.forward));

            Assert.IsNull(motor.LastDestination);
        }

        [Test]
        public void DisposedControllerDoesNotReceiveFurtherSelectCommands()
        {
            var fighter = RuntimeEntityId.NewId();
            var party = new PartyState(new[] { fighter });
            var commandSource = new FakeCommandSource();
            var controller = CreateController(party, commandSource);

            controller.Dispose();
            party.ClearSelection();
            commandSource.RaiseSelect(new SelectCommand(new[] { fighter }, SelectionMode.Replace));

            Assert.IsFalse(party.IsSelected(fighter));
        }

        [Test]
        public void DisposingTwiceDoesNotThrow()
        {
            var party = new PartyState();
            var commandSource = new FakeCommandSource();
            var controller = CreateController(party, commandSource);

            controller.Dispose();
            Assert.DoesNotThrow(() => controller.Dispose());
        }

        [Test]
        public void DisposingOneControllerDoesNotAffectAnotherOnTheSameCommandSource()
        {
            var fighter = RuntimeEntityId.NewId();
            var partyA = new PartyState(new[] { fighter });
            var partyB = new PartyState(new[] { fighter });
            var commandSource = new FakeCommandSource();

            var controllerA = CreateController(partyA, commandSource);
            controllerA.Dispose();
            var controllerB = CreateController(partyB, commandSource);
            var motorB = new FakeMovable();
            controllerB.RegisterMotor(fighter, motorB);

            commandSource.RaiseMove(new MoveCommand(new Vector3(3f, 0f, 3f), Vector3.forward));

            Assert.IsNotNull(motorB.LastDestination);
        }
    }
}
