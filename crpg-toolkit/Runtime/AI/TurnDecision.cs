using System.Collections.Generic;
using CRPG.Abilities;
using CRPG.Core;

namespace CRPG.AI
{
    /// <summary>What a Brain chose to do this turn -- either use an ability or close distance toward a
    /// target, mirroring the two BehaviorKind values. Callers branch on <see cref="Kind"/> to know which
    /// resolver to hand this to (IAbilityResolutionService vs. MovementResolutionService).</summary>
    public sealed class TurnDecision
    {
        public TurnDecision(AbilityDefinition ability, IReadOnlyList<RuntimeEntityId> targets, float score)
        {
            Kind = BehaviorKind.UseAbility;
            Ability = ability;
            Targets = targets;
            Score = score;
        }

        public TurnDecision(DefinitionId movementResourceId, float stopDistance, IReadOnlyList<RuntimeEntityId> targets, float score)
        {
            Kind = BehaviorKind.Move;
            MovementResourceId = movementResourceId;
            StopDistance = stopDistance;
            Targets = targets;
            Score = score;
        }

        public BehaviorKind Kind { get; }

        /// <summary>Null when <see cref="Kind"/> is <see cref="BehaviorKind.Move"/>.</summary>
        public AbilityDefinition Ability { get; }

        /// <summary>Only meaningful when <see cref="Kind"/> is <see cref="BehaviorKind.Move"/>.</summary>
        public DefinitionId MovementResourceId { get; }
        /// <summary>Only meaningful when <see cref="Kind"/> is <see cref="BehaviorKind.Move"/>.</summary>
        public float StopDistance { get; }

        public IReadOnlyList<RuntimeEntityId> Targets { get; }
        public float Score { get; }
    }
}
