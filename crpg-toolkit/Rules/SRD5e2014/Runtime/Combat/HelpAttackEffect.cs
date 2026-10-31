using System;
using CRPG.Abilities;
using CRPG.Actors;
using CRPG.Core;
using UnityEngine;

namespace CRPG.Rules.SRD5
{
    /// <summary>Ordered targets: the enemy being distracted, then the ally receiving one attack against that enemy.</summary>
    [Serializable]
    public sealed class HelpAttackEffect : AbilityEffect
    {
        [SerializeField] private ConditionDefinition helped;
        [SerializeField] private ConditionDefinition distracted;

        public override AbilityValidationResult Validate(AbilityExecutionContext context)
        {
            if (helped == null || distracted == null)
                return AbilityValidationResult.Reject(AbilityRejectionReason.MissingRequiredEffectService, "Help requires its beneficiary and distraction conditions.");
            if (context.Targets != null && context.Targets.Count > 1 && context.Targets[1] == context.Source)
                return AbilityValidationResult.Reject(AbilityRejectionReason.WrongRelation, "Choose another ally to help, not yourself.");
            return AbilityValidationResult.Ok();
        }

        public override void Apply(AbilityExecutionContext context)
        {
            Apply(context, context.Targets[0], distracted);
            Apply(context, context.Targets[1], helped);
        }

        private static void Apply(AbilityExecutionContext context, RuntimeEntityId targetId, ConditionDefinition condition)
        {
            context.Characters.TryGetCharacter(targetId, out CharacterState target);
            target.ApplyCondition(new ConditionInstance(condition.Id, context.Source, targetId, 1,
                ownerId: context.OwnerId, boundary: DurationBoundary.StartOfSourceTurn));
            context.OwnedTargets.Add(targetId);
            context.CombatLog?.Log(new ConditionLogEntry(context.Source, targetId, condition.Id, applied: true));
            context.EventBus?.Publish(new GameEventContext(GameEvent.ConditionApplied, context.Source, targetId));
        }
    }
}
