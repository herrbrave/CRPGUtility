using System;
using System.Collections.Generic;

namespace CRPG.Abilities
{
    /// <summary>
    /// One reusable step of an ability's resolution (spec "Reusable effect definitions"): Damage, Heal, Apply
    /// Condition, Grant Feature, a Sequence of other effects, ... AbilityDefinition holds these polymorphically
    /// via [SerializeReference] rather than as separate ScriptableObject sub-assets, so an ability's whole
    /// effect list is authored inline in one Inspector.
    /// </summary>
    [Serializable]
    public abstract class AbilityEffect
    {
        public abstract void Apply(AbilityExecutionContext context);

        /// <summary>Pure effect preflight, before any costs are spent. Null Targets means only caster readiness is being queried.</summary>
        public virtual AbilityValidationResult Validate(AbilityExecutionContext context) => AbilityValidationResult.Ok();

        /// <summary>Task 4.2: the effect(s) this one wraps and forwards Apply to, if any -- lets
        /// AbilityResolutionService's required-effect-service preflight walk a wrapped effect (SequenceEffect,
        /// ConditionalEffect, AttackRollEffect's onHit) without needing reflection into each wrapper's private
        /// fields. Empty by default; a wrapper effect overrides it.</summary>
        public virtual IEnumerable<AbilityEffect> GetChildEffects() => Array.Empty<AbilityEffect>();
    }
}
