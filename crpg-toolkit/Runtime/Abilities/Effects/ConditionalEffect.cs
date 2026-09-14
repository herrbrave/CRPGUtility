using System;
using System.Collections.Generic;
using System.Linq;
using CRPG.Actors;
using UnityEngine;

namespace CRPG.Abilities
{
    /// <summary>Applies the wrapped effect only to targets that do (or don't) have a specific condition --
    /// e.g. "extra damage if the target is Prone". A full predicate/expression language isn't modeled; this
    /// covers the common condition-gated case with what CharacterState already exposes.</summary>
    [Serializable]
    public sealed class ConditionalEffect : AbilityEffect
    {
        [SerializeField] private ConditionDefinition requiredCondition;
        [SerializeField] private bool requireAbsence;
        [SerializeReference] private AbilityEffect thenEffect;

        public override void Apply(AbilityExecutionContext context)
        {
            if (thenEffect == null) return;

            var eligibleTargets = context.Targets.Where(id =>
            {
                if (!context.Characters.TryGetCharacter(id, out var character)) return false;
                var hasCondition = character.HasCondition(requiredCondition.Id);
                return requireAbsence ? !hasCondition : hasCondition;
            }).ToList();

            if (eligibleTargets.Count == 0) return;

            // Forwards every rule/state field, not just the ones this class originally needed -- a
            // ConditionalEffect wrapping an AttackRollEffect/SavingThrowEffect/DamageEffect/ApplyConditionEffect
            // must not silently drop the rules (or the ownership tracking) those need to resolve correctly.
            // OwnedTargets in particular MUST be the SAME instance, not a fresh one -- see its own remarks.
            var filteredContext = new AbilityExecutionContext(
                context.Source, eligibleTargets, context.Characters, context.DiceService, context.CombatLog,
                context.EventBus, context.Positions, context.InfluenceMap, context.AttackBonusRule, context.ArmorClassRule,
                context.CriticalTargets, context.SavingThrowRule, context.DamageResistanceRule, context.UpcastLevels,
                context.OwnerId, context.OwnedTargets);
            thenEffect.Apply(filteredContext);
        }

        public override IEnumerable<AbilityEffect> GetChildEffects() =>
            thenEffect != null ? new[] { thenEffect } : Array.Empty<AbilityEffect>();
    }
}
