using System.Collections.Generic;
using CRPG.Core;
using UnityEngine;

namespace CRPG.Actors
{
    /// <summary>
    /// The universal building block for races, classes, feats, items, and conditions (spec "Features"):
    /// Darkvision, Second Wind, Sneak Attack, Rage, Extra Attack are all just a FeatureDefinition granting
    /// some combination of passive modifiers, resources, proficiencies and abilities. Granted abilities are
    /// referenced by bare DefinitionId (never a direct AbilityDefinition reference) because CRPG.Abilities
    /// depends on CRPG.Actors, not the other way around -- this keeps that dependency one-directional.
    /// </summary>
    [CreateAssetMenu(menuName = "CRPG Toolkit/Actors/Feature Definition")]
    public sealed class FeatureDefinition : DefinitionAsset
    {
        [SerializeField] private string displayName;
        [SerializeField, TextArea] private string description;
        [SerializeField] private List<FeatureModifier> modifiers = new();
        [SerializeField] private List<DefinitionId> grantedAbilityIds = new();
        [SerializeField] private List<ResourceGrant> grantedResources = new();
        [SerializeField] private List<DefinitionId> grantedProficiencies = new();
        [SerializeField] private List<ChoiceRequirement> choices = new();
        [SerializeField] private bool universal;

        public string DisplayName => displayName;
        public string Description => description;
        public IReadOnlyList<FeatureModifier> Modifiers => modifiers;
        public IReadOnlyList<DefinitionId> GrantedAbilityIds => grantedAbilityIds;
        public IReadOnlyList<ResourceGrant> GrantedResources => grantedResources;
        public IReadOnlyList<DefinitionId> GrantedProficiencies => grantedProficiencies;
        /// <summary>A ruleset default, independent of class/race; also reconciled at registration when loading an older save.</summary>
        public bool Universal => universal;

        /// <summary>Choices this feature introduces (spec "ChoiceRequirement") -- e.g. High Elf granting a
        /// wizard-cantrip pick. Resolving one can reveal further features (a chosen subrace) whose own choices
        /// get picked up on the next <see cref="CharacterBuildService.RefreshPendingChoices"/> pass.</summary>
        public IReadOnlyList<ChoiceRequirement> Choices => choices;
    }
}
