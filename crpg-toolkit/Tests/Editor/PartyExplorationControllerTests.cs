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

            public bool TryCalculatePath(Vector3 from, Vector3 to, out Vector3[] corners, int areaMask = -1)
            {
                corners = new[] { from, to };
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
            public bool Warp(Vector3 position) => true;
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
        public void AMemberTheCanMoveFilterRejectsIsLeftWhereTheyAreWhileTheRestOfTheSelectionMoves()
        {
            var fallen = RuntimeEntityId.NewId();
            var standing = RuntimeEntityId.NewId();
            var party = new PartyState(new[] { fallen, standing }); // both selected
            var commandSource = new FakeCommandSource();
            var selectableSource = new FakeSelectableSource();
            foreach (var member in party.Members) selectableSource.Add(member, Vector3.zero);
            var controller = new PartyExplorationController(
                party, new PartyMovementService(new FakeNavMeshService(), selectableSource), new LineFormationSolver(), commandSource,
                canMove: id => id != fallen);

            var fallenMotor = new FakeMovable();
            var standingMotor = new FakeMovable();
            controller.RegisterMotor(fallen, fallenMotor);
            controller.RegisterMotor(standing, standingMotor);

            commandSource.RaiseMove(new MoveCommand(new Vector3(10f, 0f, 10f), Vector3.forward));

            Assert.IsNull(fallenMotor.LastDestination, "a member who cannot move is not ordered anywhere");
            Assert.IsNotNull(standingMotor.LastDestination, "the others still go");
        }

        private sealed class RecordingSolver : IFormationSolver
        {
            public readonly List<Vector3> Facings = new();

            public IReadOnlyList<Vector3> Solve(Vector3 destination, Vector3 facing, int memberCount, float spacing)
            {
                Facings.Add(facing);
                var points = new List<Vector3>();
                for (var i = 0; i < memberCount; i++) points.Add(destination + Vector3.right * i);
                return points;
            }
        }

        private static (PartyExplorationController Controller, FakeCommandSource Source, RecordingSolver Solver, FakeSelectableSource Positions) CreateDeriving(
            PartyState party, Func<RuntimeEntityId, bool> canMove = null, bool derive = true)
        {
            var commandSource = new FakeCommandSource();
            var positions = new FakeSelectableSource();
            var solver = new RecordingSolver();
            var controller = new PartyExplorationController(party, new PartyMovementService(new FakeNavMeshService(), positions), solver, commandSource,
                canMove: canMove, facingSource: derive ? positions : null);
            foreach (var member in party.Members) controller.RegisterMotor(member, new FakeMovable());
            return (controller, commandSource, solver, positions);
        }

        [Test]
        public void WithAFacingSourceAPartySentWestIsOrderedToFaceWestNotWorldForward()
        {
            var a = RuntimeEntityId.NewId();
            var b = RuntimeEntityId.NewId();
            var party = new PartyState(new[] { a, b });
            var (controller, source, solver, positions) = CreateDeriving(party);
            positions.Add(a, new Vector3(10f, 0f, 1f));
            positions.Add(b, new Vector3(10f, 0f, -1f));

            source.RaiseMove(new MoveCommand(new Vector3(0f, 0f, 0f), Vector3.forward));

            Assert.AreEqual(1, solver.Facings.Count);
            Assert.Less(Vector3.Distance(solver.Facings[0], Vector3.left), 0.001f, "from the party centre (10,0,0) to (0,0,0) is west");
            Assert.AreEqual(solver.Facings[0], controller.LastOrderFacing);
        }

        [Test]
        public void TheCentreIsTheOrderedMembersOnlyNotUnselectedOnesOrOnesTheCanMoveFilterDrops()
        {
            var a = RuntimeEntityId.NewId();
            var unselected = RuntimeEntityId.NewId();
            var fallen = RuntimeEntityId.NewId();
            var party = new PartyState(new[] { a, unselected, fallen });
            party.SelectMany(new[] { a, fallen });
            var (_, source, solver, positions) = CreateDeriving(party, canMove: id => id != fallen);
            positions.Add(a, Vector3.zero);
            positions.Add(unselected, new Vector3(500f, 0f, 0f));
            positions.Add(fallen, new Vector3(100f, 0f, 0f));   // asymmetric on purpose: an average over everyone would be (200, 0, 0) and point nowhere near +Z

            source.RaiseMove(new MoveCommand(new Vector3(0f, 0f, 10f), Vector3.right));

            Assert.Less(Vector3.Distance(solver.Facings[0], Vector3.forward), 0.001f, "only the one ordered member counts: straight along +Z");
        }

        [Test]
        public void AnOrderTooShortToHaveADirectionKeepsTheCommandFacingAndTheLastDerivedOne()
        {
            var a = RuntimeEntityId.NewId();
            var party = new PartyState(new[] { a });
            var (controller, source, solver, positions) = CreateDeriving(party);
            positions.Add(a, Vector3.zero);
            source.RaiseMove(new MoveCommand(new Vector3(0f, 0f, 5f), Vector3.right));
            var derived = controller.LastOrderFacing;

            source.RaiseMove(new MoveCommand(new Vector3(0.02f, 0f, 0.02f), Vector3.right));   // a click on the party's own feet

            Assert.AreEqual(Vector3.right, solver.Facings[1], "no direction to derive: the command's own facing is used");
            Assert.AreEqual(derived, controller.LastOrderFacing, "LastOrderFacing only ever records a derived direction, so a stopped party does not snap to world forward");
        }

        [Test]
        public void WithoutAFacingSourceTheCommandFacingPassesThroughUnchangedAndLastOrderFacingStaysZero()
        {
            var a = RuntimeEntityId.NewId();
            var party = new PartyState(new[] { a });
            var (controller, source, solver, positions) = CreateDeriving(party, derive: false);
            positions.Add(a, new Vector3(10f, 0f, 0f));

            source.RaiseMove(new MoveCommand(Vector3.zero, Vector3.forward));

            Assert.AreEqual(Vector3.forward, solver.Facings[0]);
            Assert.AreEqual(Vector3.zero, controller.LastOrderFacing);
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
