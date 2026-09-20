using System;
using System.Collections.Generic;
using CRPG.Core;
using CRPG.Party;

namespace BarrowsEnd.Gate
{
    /// <summary>
    /// Wraps the real command source (InputSystemCommandSource) for the gate scene. Two things the stock stack does not do for a scene
    /// that mixes party members with a hostile NPC:
    /// <list type="bullet">
    /// <item>Select commands are filtered to party members and cut to ONE character. The router's SceneSelectableSource offers every live
    /// agent (the skeleton included) and PartyState.Select throws for a non-member; and PartyExplorationController applies formation
    /// offsets to every selected member, so a multi-select would scatter the party.</item>
    /// <item>Move / Interact / Cancel pass straight through.</item>
    /// </list>
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

            foreach (var character in command.Characters)
            {
                if (!party.IsMember(character)) continue;
                Select?.Invoke(new SelectCommand(new List<RuntimeEntityId> { character }, SelectionMode.Replace));
                return; // exactly one
            }
        }

        private void OnMove(MoveCommand command) => Move?.Invoke(command);
        private void OnInteract(InteractCommand command) => Interact?.Invoke(command);
        private void OnCancel() => Cancel?.Invoke();
    }
}
