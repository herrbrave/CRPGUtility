using System;
using System.Collections.Generic;
using BarrowsEnd.Gate;
using CRPG.Core;
using CRPG.Party;
using NUnit.Framework;

namespace BarrowsEnd.Tests
{
    /// <summary>2D-Campaign-ExplorationUX-Followups.md D4: GateCommandSource.OnSelect used to cut every
    /// SelectCommand down to exactly one matching party member and force SelectionMode.Replace regardless of
    /// the real mode -- a real limitation, not an intentional design constraint, and never covered by a test
    /// under this class's own name before now. These tests pin the fixed behavior: every matching member
    /// forwarded, real mode preserved, non-members (the hostile NPC) filtered out.</summary>
    public class GateCommandSourceTests
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
            var hostileNpc = RuntimeEntityId.NewId(); // not a party member -- e.g. the gate's skeleton
            var party = new PartyState(new[] { memberA, memberB });

            var inner = new FakeCommandSource();
            using var gate = new GateCommandSource(inner, () => party);
            SelectCommand? received = null;
            gate.Select += cmd => received = cmd;

            inner.RaiseSelect(new SelectCommand(new List<RuntimeEntityId> { memberA, memberB, hostileNpc }, SelectionMode.Toggle));

            Assert.IsTrue(received.HasValue);
            Assert.AreEqual(SelectionMode.Toggle, received.Value.Mode);
            CollectionAssert.AreEquivalent(new[] { memberA, memberB }, received.Value.Characters);
        }

        [Test]
        public void ASelectCommandThatOnlyHitsTheHostileNpcIsSilentlyDropped()
        {
            var member = RuntimeEntityId.NewId();
            var hostileNpc = RuntimeEntityId.NewId();
            var party = new PartyState(new[] { member });

            var inner = new FakeCommandSource();
            using var gate = new GateCommandSource(inner, () => party);
            var raised = false;
            gate.Select += _ => raised = true;

            inner.RaiseSelect(new SelectCommand(new List<RuntimeEntityId> { hostileNpc }, SelectionMode.Replace));

            Assert.IsFalse(raised);
        }

        [Test]
        public void MoveInteractAndCancelPassThroughUnchanged()
        {
            var party = new PartyState(Array.Empty<RuntimeEntityId>());
            var inner = new FakeCommandSource();
            using var gate = new GateCommandSource(inner, () => party);

            MoveCommand? move = null;
            InteractCommand? interact = null;
            var cancelled = false;
            gate.Move += cmd => move = cmd;
            gate.Interact += cmd => interact = cmd;
            gate.Cancel += () => cancelled = true;

            var moveCommand = new MoveCommand(UnityEngine.Vector3.one, UnityEngine.Vector3.forward);
            var interactCommand = new InteractCommand(RuntimeEntityId.NewId());
            inner.RaiseMove(moveCommand);
            inner.RaiseInteract(interactCommand);
            inner.RaiseCancel();

            Assert.AreEqual(moveCommand.Destination, move.Value.Destination);
            Assert.AreEqual(interactCommand.Target, interact.Value.Target);
            Assert.IsTrue(cancelled);
        }
    }
}
