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
    public sealed class PartyExplorationController
    {
        private readonly PartyState party;
        private readonly PartyMovementService movementService;
        private readonly IFormationSolver formationSolver;
        private readonly float formationSpacing;
        private readonly Dictionary<RuntimeEntityId, IMovable> motors = new();

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

            commandSource.Select += HandleSelect;
            commandSource.Move += HandleMove;
        }

        public void RegisterMotor(RuntimeEntityId character, IMovable motor) => motors[character] = motor;

        public void UnregisterMotor(RuntimeEntityId character) => motors.Remove(character);

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
                if (motors.TryGetValue(assignment.Key, out var motor))
                    motor.SetDestination(assignment.Value);
            }
        }
    }
}
