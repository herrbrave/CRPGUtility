using System;

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
    }
}
