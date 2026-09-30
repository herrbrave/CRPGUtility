using System;
using System.Collections.Generic;
using CRPG.Core;
using CRPG.Party;

namespace BarrowsEnd.Gate
{
    /// <summary>
    /// Wraps the real command source (InputSystemCommandSource) for the gate scene. One thing the stock stack does not do for a scene
    /// that mixes party members with a hostile NPC:
    /// <list type="bullet">
    /// <item>Select commands are filtered to party members only -- the router's SceneSelectableSource offers every live agent (the
    /// skeleton included), and PartyState.Select throws for a non-member.</item>
    /// <item>Move / Interact / Cancel pass straight through.</item>
    /// </list>
    /// Real multi-select (2D-Campaign-ExplorationUX-Followups.md, D4): every matching party member in a SelectCommand is forwarded, with
    /// the original SelectionMode preserved -- an earlier version of this class cut a multi-hit drag-select down to exactly one member
    /// and forced Replace regardless of the real mode, which was never an intentional design constraint, just an unexamined default.
    /// Session independent: it asks for the CURRENT party each time, so it survives a save/load session swap.
    /// </summary>
    public sealed class GateCommandSource : IPlayerCommandSource, IDisposable
    {
        private readonly IPlayerCommandSource inner;
        private readonly Func<PartyState> currentParty;

        public GateCommandSource(IPlayerCommandSource inner, Func<PartyState> currentParty)
        {
            this.inner = inner ?? throw new ArgumentNullException(nameof(inner));
            this.currentParty = currentParty ?? throw new ArgumentNullException(nameof(currentParty));
            inner.Select += OnSelect;
            inner.Move += OnMove;
            inner.Interact += OnInteract;
            inner.Cancel += OnCancel;
        }

        public event Action<SelectCommand> Select;
        public event Action<MoveCommand> Move;
        public event Action<InteractCommand> Interact;
        public event Action Cancel;

        /// <summary>Task 6.1: while set, every Move command is redirected here instead of raised on
        /// <see cref="Move"/> -- the toolkit's own <c>TacticalPlayerMoveAdapter</c> doc comment names exactly
        /// this as unsolved by any shipped example ("there is exactly one movement mode during a tactical
        /// encounter, so there's no dual-ownership of the Move event to arbitrate"): without this, a ground
        /// click during combat would reach BOTH <c>PartyExplorationController</c> (subscribed to this class's own
        /// <see cref="Move"/>) and a tactical move adapter (if wired directly to the same underlying
        /// <c>InputSystemCommandSource</c>), letting the player freely walk the whole party around mid-fight,
        /// bypassing turn order and the Movement resource entirely. Null (the default) preserves the original
        /// pass-through-to-<see cref="Move"/> behavior.</summary>
        public Action<MoveCommand> CombatMoveOverride { get; set; }

        public void Dispose()
        {
            inner.Select -= OnSelect;
            inner.Move -= OnMove;
            inner.Interact -= OnInteract;
            inner.Cancel -= OnCancel;
        }

        private void OnSelect(SelectCommand command)
        {
            var party = currentParty();
            if (party == null) return;

            List<RuntimeEntityId> members = null;
            foreach (var character in command.Characters)
            {
                if (!party.IsMember(character)) continue;
                members ??= new List<RuntimeEntityId>();
                members.Add(character);
            }

            // No matching member is a legal "selected only the hostile NPC / nothing" outcome -- stays silent
            // rather than routing an empty command through, same convention as PlayerCommandRouter.ResolveDragSelect.
            if (members == null) return;

            Select?.Invoke(new SelectCommand(members, command.Mode));
        }

        private void OnMove(MoveCommand command)
        {
            if (CombatMoveOverride != null) { CombatMoveOverride(command); return; }
            Move?.Invoke(command);
        }
        private void OnInteract(InteractCommand command) => Interact?.Invoke(command);
        private void OnCancel() => Cancel?.Invoke();
    }
}
