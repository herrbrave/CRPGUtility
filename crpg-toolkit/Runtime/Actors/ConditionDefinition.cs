using System.Collections.Generic;
using CRPG.Core;
using UnityEngine;

namespace CRPG.Actors
{
    /// <summary>
    /// A status effect that can be applied to a character (Poisoned, Prone, a homebrew "Bleeding", ...).
    /// <see cref="Modifiers"/> is how it contributes to rule queries (spec "Conditions contribute modifiers to
    /// RuleQueries") -- see <see cref="FeatureModifierCollector"/>. <see cref="ConditionInstance"/> is the
    /// runtime application of it.
    /// </summary>
    [CreateAssetMenu(menuName = "CRPG Toolkit/Actors/Condition Definition")]
    public sealed class ConditionDefinition : DefinitionAsset
    {
        [SerializeField] private string displayName;
        [SerializeField, TextArea] private string description;
        [SerializeField] private List<FeatureModifier> modifiers = new();

        /// <summary>True for Incapacitated/Paralyzed/Petrified/Stunned/Unconscious-style conditions that stop
        /// a character from taking actions or reactions at all. Generalizes what used to be a CharacterState.
        /// DeathSaves-only check in AbilityResolutionService.CanUse into a real, data-driven condition property.</summary>
        [SerializeField] private bool preventsActions;

        public string DisplayName => displayName;
        public string Description => description;
        public IReadOnlyList<FeatureModifier> Modifiers => modifiers;
        public bool PreventsActions => preventsActions;
    }
}
