using System;
using System.Collections.Generic;
using CRPG.Core;
using CRPG.Gameplay;
using CRPG.Party;
using NUnit.Framework;
using UnityEngine;

namespace CRPG.Tests.EditMode
{
    /// <summary>The select filter and input arbitration both games wrote by hand (Barrow's End's GateCommandSource and Viking Raid's VikingCommandSource), pinned in one place.</summary>
    public class PartyCommandFilterTests
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

        [Test]
        public void AMultiHitSelectCommandForwardsEveryMatchingPartyMemberWithTheRealSelectionMode()
        {
            var memberA = RuntimeEntityId.NewId();
            var memberB = RuntimeEntityId.NewId();
            var hostile = RuntimeEntityId.NewId();
            var party = new PartyState(new[] { memberA, memberB });
            var inner = new FakeCommandSource();
            using var filter = new PartyCommandFilter(inner, () => party);
            SelectCommand? received = null;
            filter.Select += cmd => received = cmd;

            inner.RaiseSelect(new SelectCommand(new List<RuntimeEntityId> { memberA, memberB, hostile }, SelectionMode.Toggle));

            Assert.IsTrue(received.HasValue);
            Assert.AreEqual(SelectionMode.Toggle, received.Value.Mode);
            CollectionAssert.AreEquivalent(new[] { memberA, memberB }, received.Value.Characters);
        }

        [Test]
        public void ASelectCommandThatOnlyHitsANonMemberIsSilentlyDropped()
        {
            var party = new PartyState(new[] { RuntimeEntityId.NewId() });
            var inner = new FakeCommandSource();
            using var filter = new PartyCommandFilter(inner, () => party);
            var raised = false;
            filter.Select += _ => raised = true;

            inner.RaiseSelect(new SelectCommand(new List<RuntimeEntityId> { RuntimeEntityId.NewId() }, SelectionMode.Replace));

            Assert.IsFalse(raised);
        }

        [Test]
        public void TheCurrentPartyIsAskedForEachTimeSoASessionSwapIsFollowed()
        {
            var first = RuntimeEntityId.NewId();
            var second = RuntimeEntityId.NewId();
            var party = new PartyState(new[] { first });
            var inner = new FakeCommandSource();
            using var filter = new PartyCommandFilter(inner, () => party);
            var raised = 0;
            filter.Select += _ => raised++;

            inner.RaiseSelect(new SelectCommand(new List<RuntimeEntityId> { second }, SelectionMode.Replace));
            party = new PartyState(new[] { second });   // a load built a new session and a new party
            inner.RaiseSelect(new SelectCommand(new List<RuntimeEntityId> { second }, SelectionMode.Replace));

            Assert.AreEqual(1, raised);
        }

        [Test]
        public void ANullPartyDropsSelectsRatherThanThrowing()
        {
            var inner = new FakeCommandSource();
            using var filter = new PartyCommandFilter(inner, () => null);
            filter.Select += _ => Assert.Fail("no party, nothing to select");

            Assert.DoesNotThrow(() => inner.RaiseSelect(new SelectCommand(new List<RuntimeEntityId> { RuntimeEntityId.NewId() }, SelectionMode.Replace)));
        }

        [Test]
        public void MoveInteractAndCancelPassThroughUnchangedByDefault()
        {
            var inner = new FakeCommandSource();
            using var filter = new PartyCommandFilter(inner, () => new PartyState(Array.Empty<RuntimeEntityId>()));
            MoveCommand? move = null;
            InteractCommand? interact = null;
            var cancelled = false;
            filter.Move += cmd => move = cmd;
            filter.Interact += cmd => interact = cmd;
            filter.Cancel += () => cancelled = true;
            var moveCommand = new MoveCommand(Vector3.one, Vector3.right);
            var interactCommand = new InteractCommand(RuntimeEntityId.NewId());

            inner.RaiseMove(moveCommand);
            inner.RaiseInteract(interactCommand);
            inner.RaiseCancel();

            Assert.AreEqual(moveCommand.Destination, move.Value.Destination);
            Assert.AreEqual(Vector3.right, move.Value.Facing, "the filter no longer touches the facing");
            Assert.AreEqual(interactCommand.Target, interact.Value.Target);
            Assert.IsTrue(cancelled);
        }

        [Test]
        public void WhileCombatOwnsInputMovesGoToTheOverrideNotTheMoveEventAndInteractIsDropped()
        {
            var inner = new FakeCommandSource();
            using var filter = new PartyCommandFilter(inner, () => new PartyState(Array.Empty<RuntimeEntityId>()));
            var exploration = 0;
            var interactions = 0;
            MoveCommand? overridden = null;
            filter.Move += _ => exploration++;
            filter.Interact += _ => interactions++;

            filter.CombatMoveOverride = cmd => overridden = cmd;
            var destination = new Vector3(3f, 0f, 4f);
            inner.RaiseMove(new MoveCommand(destination, Vector3.forward));
            inner.RaiseInteract(new InteractCommand(RuntimeEntityId.NewId()));

            Assert.AreEqual(0, exploration, "a combat click must never also drive exploration movement");
            Assert.AreEqual(destination, overridden.Value.Destination);
            Assert.AreEqual(0, interactions, "a click on a travel arch or NPC mid-fight must not start an interaction");

            filter.CombatMoveOverride = null;
            inner.RaiseMove(new MoveCommand(destination, Vector3.forward));
            inner.RaiseInteract(new InteractCommand(RuntimeEntityId.NewId()));
            Assert.AreEqual(1, exploration);
            Assert.AreEqual(1, interactions);
        }

        [Test]
        public void MovesLockedSwallowsMoveAndInteractAndTakesPrecedenceOverTheCombatOverride()
        {
            var inner = new FakeCommandSource();
            using var filter = new PartyCommandFilter(inner, () => new PartyState(Array.Empty<RuntimeEntityId>()));
            var moves = 0;
            var overrides = 0;
            var interactions = 0;
            filter.Move += _ => moves++;
            filter.Interact += _ => interactions++;
            filter.CombatMoveOverride = _ => overrides++;
            filter.MovesLocked = true;

            inner.RaiseMove(new MoveCommand(Vector3.one, Vector3.forward));
            inner.RaiseInteract(new InteractCommand(RuntimeEntityId.NewId()));

            Assert.AreEqual(0, moves);
            Assert.AreEqual(0, overrides, "locked wins over the override");
            Assert.AreEqual(0, interactions);

            filter.MovesLocked = false;
            filter.CombatMoveOverride = null;
            inner.RaiseMove(new MoveCommand(Vector3.one, Vector3.forward));
            Assert.AreEqual(1, moves);
        }

        [Test]
        public void CancelStillPassesThroughWhileLockedOrOverridden()
        {
            var inner = new FakeCommandSource();
            using var filter = new PartyCommandFilter(inner, () => new PartyState(Array.Empty<RuntimeEntityId>()));
            var cancelled = 0;
            filter.Cancel += () => cancelled++;
            filter.MovesLocked = true;
            filter.CombatMoveOverride = _ => { };

            inner.RaiseCancel();

            Assert.AreEqual(1, cancelled);
        }

        [Test]
        public void DisposeUnsubscribesFromTheInnerSource()
        {
            var inner = new FakeCommandSource();
            var party = new PartyState(new[] { RuntimeEntityId.NewId() });
            var filter = new PartyCommandFilter(inner, () => party);
            var raised = 0;
            filter.Move += _ => raised++;
            filter.Cancel += () => raised++;

            filter.Dispose();
            inner.RaiseMove(new MoveCommand(Vector3.one, Vector3.forward));
            inner.RaiseCancel();

            Assert.AreEqual(0, raised);
        }

        [Test]
        public void NullConstructorArgumentsAreRejected()
        {
            Assert.Throws<ArgumentNullException>(() => new PartyCommandFilter(null, () => null));
            Assert.Throws<ArgumentNullException>(() => new PartyCommandFilter(new FakeCommandSource(), null));
        }
    }
}
