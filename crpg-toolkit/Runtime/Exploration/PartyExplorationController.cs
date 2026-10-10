using System;
using System.Collections.Generic;
using System.Linq;
using CRPG.Core;
using CRPG.Gameplay;
using CRPG.Navigation;
using CRPG.Party;
using UnityEngine;

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
        private readonly Func<RuntimeEntityId, bool> canMove;
        private readonly ISelectableSource facingSource;
        private bool disposed;

        public PartyExplorationController(
            PartyState party,
            PartyMovementService movementService,
            IFormationSolver formationSolver,
            IPlayerCommandSource commandSource,
            float formationSpacing = 1.5f,
            Func<RuntimeEntityId, bool> canMove = null,
            ISelectableSource facingSource = null)
        {
            this.facingSource = facingSource;
            this.canMove = canMove;
            this.party = party;
            this.movementService = movementService;
            this.formationSolver = formationSolver;
            this.formationSpacing = formationSpacing;
            this.commandSource = commandSource;

            commandSource.Select += HandleSelect;
            commandSource.Move += HandleMove;
        }

        /// <summary>The facing the last order actually used when it was DERIVED from the party's travel direction (see the <c>facingSource</c> constructor parameter); zero until one was.
        /// A game can turn stopped members to it so a halted party all looks the way it was sent. Never updated by an order too short to have a direction.</summary>
        public Vector3 LastOrderFacing { get; private set; }

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

        /// <summary>The router cannot know where the party is, so it sends <c>Vector3.forward</c> as the facing of every ground click and leaves deriving the real one to whoever consumes it
        /// (nobody did: a wedge always pointed along world +Z). With a <c>facingSource</c>, the facing is the flat direction from the centre of the members being ordered (selected, and allowed
        /// to move) to the destination. Without one, or when the order is shorter than 0.1 m, nothing is derived and the command's own facing stands.</summary>
        private bool TryDeriveFacing(IReadOnlyList<RuntimeEntityId> members, Vector3 destination, out Vector3 facing)
        {
            facing = default;
            if (facingSource == null) return false;

            var sum = Vector3.zero;
            var found = 0;
            foreach (var entity in facingSource.GetSelectables())
            {
                if (!members.Contains(entity.Id)) continue;
                sum += entity.WorldPosition;
                found++;
            }

            if (found == 0) return false;
            var direction = destination - sum / found;
            direction.y = 0f;
            if (direction.sqrMagnitude < 0.01f) return false;
            facing = direction.normalized;
            return true;
        }

        private void HandleMove(MoveCommand command)
        {
            if (party.Selected.Count == 0) return;

            // A downed or dead member stays where they fell: the filter (null = everyone can move) drops them from the order, and the
            // rest of the selection still goes.
            var members = party.Members.Where(m => party.IsSelected(m) && (canMove == null || canMove(m))).ToList();
            if (members.Count == 0) return;
            var facing = command.Facing;
            if (TryDeriveFacing(members, command.Destination, out var derived)) { facing = derived; LastOrderFacing = derived; }
            var order = new PartyMoveOrder(members, command.Destination, facing, formationSolver, formationSpacing);
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
