using System;
using CRPG.Abilities;
using CRPG.Core;

namespace CRPG.Rules.SRD5
{
    /// <summary>
    /// The Hide action: one Dexterity (Stealth) check against the passive Perception of every hostile that cannot plainly see you. It is refused, spending nothing, when everyone nearby can see you clearly (with the reason in words); a failed roll still spends the action.
    /// The work lives in the game's <see cref="IStealthService"/> (the ability assembly cannot see the SRD stealth rules); this effect only validates and calls it.
    /// </summary>
    [Serializable]
    public sealed class HideEffect : AbilityEffect
    {
        public override AbilityValidationResult Validate(AbilityExecutionContext context)
        {
            if (context.Stealth == null) return AbilityValidationResult.Reject(AbilityRejectionReason.MissingRequiredEffectService, "Hide requires the stealth rules.");
            return context.Stealth.CanAttemptHide(context.Source, out var reason)
                ? AbilityValidationResult.Ok()
                : AbilityValidationResult.Reject(AbilityRejectionReason.ActionRestricted, reason);
        }

        public override void Apply(AbilityExecutionContext context)
        {
            context.Stealth.Hide(context.Source);
            context.OwnedTargets.Add(context.Source);
        }
    }
}
