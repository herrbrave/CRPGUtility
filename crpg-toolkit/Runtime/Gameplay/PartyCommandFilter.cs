using System;
using System.Collections.Generic;
using CRPG.Core;
using CRPG.Party;

namespace CRPG.Gameplay
{
    /// <summary>
    /// Wraps the real <see cref="IPlayerCommandSource"/> (<c>InputSystemCommandSource</c>) for a scene that mixes party members with other characters (hostiles, NPCs) and has a tactical
    /// mode that must own the input. Two things the stock stack does not do:
    /// <list type="bullet">
    /// <item><b>Select is filtered to the CURRENT party's members</b>, with the real <see cref="SelectionMode"/> and every matching member preserved. The router's
    /// <c>SceneSelectableSource</c> offers every live agent, and <c>PartyState.Select</c> throws for a non-member; a command that selects no party member (a click on a goblin) is dropped
    /// silently. The party is asked for each time, so the filter survives a save/load session swap.</item>
    /// <item><b>Input arbitration.</b> While <see cref="MovesLocked"/> is set (a scripted walk is under way) Move AND Interact are swallowed -- checked first. While
    /// <see cref="CombatMoveOverride"/> is set every Move goes to it instead of being raised on <see cref="Move"/> (so a ground click during combat can never also drive exploration
    /// movement and let the party walk around outside turn order), and Interact is dropped (a click on a travel arch or an NPC mid-fight would walk a member there outside turn order).
    /// Cancel always passes through.</item>
    /// </list>
    /// </summary>
    public sealed class PartyCommandFilter : IPlayerCommandSource, IDisposable
    {
        private readonly IPlayerCommandSource inner;
        private readonly Func<PartyState> currentParty;

        public PartyCommandFilter(IPlayerCommandSource inner, Func<PartyState> currentParty)
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

        /// <summary>While set, every Move command goes here instead of <see cref="Move"/> and Interact is dropped. Null (the default) passes Move through.</summary>
        public Action<MoveCommand> CombatMoveOverride { get; set; }

        /// <summary>While set, an Interact command goes to it instead of being dropped by a fight (<see cref="CombatMoveOverride"/> alone drops it): the game decides what a click on an interactable means mid-fight (the ship lets the current hero open the cell gate).</summary>
        public Action<InteractCommand> CombatInteractOverride { get; set; }

        /// <summary>While true, Move and Interact are swallowed entirely (takes precedence over <see cref="CombatMoveOverride"/>): a click during a walk the game itself ordered would override it.</summary>
        public bool MovesLocked { get; set; }

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

            // No matching member is a legal "selected only a hostile / nothing" outcome: silent, like PlayerCommandRouter's own empty drag-select.
            if (members == null) return;

            Select?.Invoke(new SelectCommand(members, command.Mode));
        }

        private void OnMove(MoveCommand command)
        {
            if (MovesLocked) return;
            if (CombatMoveOverride != null) { CombatMoveOverride(command); return; }
            Move?.Invoke(command);
        }

        private void OnInteract(InteractCommand command)
        {
            if (MovesLocked) return;
            if (CombatInteractOverride != null) { CombatInteractOverride(command); return; }
            if (CombatMoveOverride != null) return;
            Interact?.Invoke(command);
        }

        private void OnCancel() => Cancel?.Invoke();
    }
}
