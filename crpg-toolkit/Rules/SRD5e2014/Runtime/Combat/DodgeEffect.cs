using System;
using CRPG.Abilities;
using CRPG.Actors;
using CRPG.Core;
using UnityEngine;

namespace CRPG.Rules.SRD5
{
    [Serializable]
    public sealed class DodgeEffect : AbilityEffect
    {
        [SerializeField] private ConditionDefinition dodging;

        public override AbilityValidationResult Validate(AbilityExecutionContext context)
        {
            if (dodging == null || context.MovementSpeedRule == null)
                return AbilityValidationResult.Reject(AbilityRejectionReason.MissingRequiredEffectService, "Dodge requires its condition and a speed rule.");
            if (!context.Characters.TryGetCharacter(context.Source, out CharacterState character)
                || !character.TryGetResource(CommonActions.MovementId, out ResourceState movement)
                || context.MovementSpeedRule.ComputeEffectiveSpeed(context.Source, movement.Max) <= 0f)
                return AbilityValidationResult.Reject(AbilityRejectionReason.ActionRestricted, "Cannot Dodge while your speed is zero.");
            return AbilityValidationResult.Ok();
        }

        public override void Apply(AbilityExecutionContext context)
        {
            context.Characters.TryGetCharacter(context.Source, out CharacterState character);
            character.ApplyCondition(new ConditionInstance(this.dodging.Id, context.Source, context.Source, 1,
                ownerId: context.OwnerId, boundary: DurationBoundary.StartOfSourceTurn));
            context.OwnedTargets.Add(context.Source);
            context.CombatLog?.Log(new ConditionLogEntry(context.Source, context.Source, this.dodging.Id, applied: true));
            context.EventBus?.Publish(new GameEventContext(GameEvent.ConditionApplied, context.Source, context.Source));
        }
    }
}
