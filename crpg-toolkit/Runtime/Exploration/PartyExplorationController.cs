using System;
using System.Collections.Generic;
using System.Linq;
using CRPG.Core;
using CRPG.Navigation;
using CRPG.Party;

namespace CRPG.Exploration
{
    /// <summary>
    /// Wires player commands (from any <see cref="IPlayerCommandSource"/> -- mouse, touch, controller cursor,
    /// ...) onto <see cref="PartyState"/> and each selected member's movement. Knows nothing about how a
    /// command was produced, so swapping the Input System adapter for something else never touches this class.
    /// </summary>
    /// <remarks>
    /// Plain C# (not a MonoBehaviour), so nothing unsubscribes it automatically -- callers that rebuild a
    /// session (or otherwise replace this controller) must call <see cref="Dispose"/> on the old instance
    /// first, or its handlers stay live on <paramref name="commandSource"/> alongside the new controller's.
    /// </remarks>
    public sealed class PartyExplorationController : IDisposable
    {
        private readonly PartyState party;
        private readonly PartyMovementService movementService;
        private readonly IFormationSolver formationSolver;
        private readonly float formationSpacing;
        private readonly IPlayerCommandSource commandSource;
        private readonly Dictionary<RuntimeEntityId, IMovable> motors = new();
        private bool disposed;

        public PartyExplorationController(
            PartyState party,
            PartyMovementService movementService,
            IFormationSolver formationSolver,
            IPlayerCommandSource commandSource,
            float formationSpacing = 1.5f)
        {
            this.party = party;
            this.movementService = movementService;
            this.formationSolver = formationSolver;
            this.formationSpacing = formationSpacing;
            this.commandSource = commandSource;

            commandSource.Select += HandleSelect;
            commandSource.Move += HandleMove;
        }

        /// <summary>Raised once per member per <see cref="HandleMove"/> call with the truthful outcome of
        /// that member's move request -- <see cref="FormationResolutionOutcome.Unreachable"/> synthesizes
        /// <see cref="MoveResult.Invalid"/> (already <see cref="MoveStatus.Invalid"/>'s documented meaning);
        /// <see cref="FormationResolutionOutcome.Exact"/>/<see cref="FormationResolutionOutcome.Fallback"/>
        /// forward the real result from <see cref="IMovable.SetDestination"/>. Exists so an unreachable
        /// formation slot is a reportable outcome, not a silently-discarded one.</summary>
        public event Action<RuntimeEntityId, MoveResult> MemberMoveOutcome;

        public void RegisterMotor(RuntimeEntityId character, IMovable motor) => motors[character] = motor;

        public void UnregisterMotor(RuntimeEntityId character) => motors.Remove(character);

        /// <summary>Unsubscribes from <see cref="commandSource"/> so this controller stops reacting to
        /// player input. Idempotent -- safe to call more than once.</summary>
        public void Dispose()
        {
            if (disposed) return;
            disposed = true;

            commandSource.Select -= HandleSelect;
            commandSource.Move -= HandleMove;
        }

        private void HandleSelect(SelectCommand command)
        {
            if (command.Characters.Count == 0) return;

            if (command.Mode == SelectionMode.Toggle)
            {
                foreach (var character in command.Characters) party.ToggleSelect(character);
            }
            else if (command.Characters.Count == 1)
            {
                party.Select(command.Characters[0]);
            }
            else
            {
                party.SelectMany(command.Characters);
            }
        }

        private void HandleMove(MoveCommand command)
        {
            if (party.Selected.Count == 0) return;

            var members = party.Members.Where(party.IsSelected).ToList();
            var order = new PartyMoveOrder(members, command.Destination, command.Facing, formationSolver, formationSpacing);
            var resolved = movementService.ResolveDestinations(order);

            foreach (var assignment in resolved)
            {
                var character = assignment.Key;
                var resolution = assignment.Value;

                if (resolution.Outcome == FormationResolutionOutcome.Unreachable)
                {
                    // Matches SetDestination's own existing contract for a rejected destination: whatever
                    // this member was already doing is left untouched, and every OTHER member in this same
                    // order still gets its own independent SetDestination call below -- one unreachable
                    // follower never freezes the rest of the group.
                    MemberMoveOutcome?.Invoke(character, MoveResult.Invalid());
                    continue;
                }

                if (motors.TryGetValue(character, out var motor))
                {
                    var result = motor.SetDestination(resolution.ResolvedDestination.Value);
                    MemberMoveOutcome?.Invoke(character, result);
                }
            }
        }
    }
}
