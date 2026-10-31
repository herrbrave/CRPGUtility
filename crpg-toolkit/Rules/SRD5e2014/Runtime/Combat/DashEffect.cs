using System;
using CRPG.Abilities;
using CRPG.Actors;
using CRPG.Core;
using UnityEngine;

namespace CRPG.Rules.SRD5
{
    [Serializable]
    public sealed class DashEffect : AbilityEffect
    {
        [SerializeField] private DefinitionId movementResourceId = new("resource-movement");

        public override AbilityValidationResult Validate(AbilityExecutionContext context)
        {
            if (!movementResourceId.IsValid || context.MovementSpeedRule == null)
                return AbilityValidationResult.Reject(AbilityRejectionReason.MissingRequiredEffectService, "Dash requires a movement resource and speed rule.");
            if (!context.Characters.TryGetCharacter(context.Source, out CharacterState character)
                || !character.TryGetResource(movementResourceId, out ResourceState movement) || movement.Max <= 0
                || context.MovementSpeedRule.ComputeEffectiveSpeed(context.Source, movement.Max) <= 0f)
                return AbilityValidationResult.Reject(AbilityRejectionReason.ActionRestricted, "Cannot Dash while your speed is zero.");
            return AbilityValidationResult.Ok();
        }

        public override void Apply(AbilityExecutionContext context)
        {
            context.Characters.TryGetCharacter(context.Source, out CharacterState character);
            character.TryGetResource(movementResourceId, out ResourceState movement);
            // Store an additional base allowance; movement resolution applies the current speed modifiers to every allowance.
            movement.GrantTemporaryMaximum(movement.Max);
            float allowances = 1f + (float)movement.TemporaryMaximum / movement.Max;
            movement.SetEffectiveMaximum(Mathf.CeilToInt(context.MovementSpeedRule.ComputeEffectiveSpeed(context.Source, movement.Max) * allowances));
        }
    }
}
