using System.Collections.Generic;
using CRPG.Core;
using UnityEngine;

namespace CRPG.Abilities
{
    /// <summary>
    /// Spells and mundane actions use the same ability system (spec "Ability framework") -- a Fire Bolt and a
    /// Second Wind are both just an AbilityDefinition with different costs/targeting/effects, not two
    /// different systems.
    /// </summary>
    [CreateAssetMenu(menuName = "CRPG Toolkit/Abilities/Ability Definition")]
    public sealed class AbilityDefinition : DefinitionAsset
    {
        [SerializeField] private string displayName;
        [SerializeField, TextArea] private string description;
        [SerializeField] private AbilityTag tags;
        [SerializeField] private List<AbilityCost> costs = new();
        [SerializeField] private TargetingDefinition targeting = new();
        [SerializeReference] private List<AbilityEffect> effects = new();

        /// <summary>0 for a non-spell ability or a cantrip; a spell's real SRD level (1-9) otherwise. Only
        /// meaningful for upcasting -- see AbilityResolutionService.Execute's upcastWithSlot parameter and
        /// ResourceDefinition.SpellSlotLevel.</summary>
        [SerializeField] private int spellLevel;

        public string DisplayName => displayName;
        public string Description => description;
        public AbilityTag Tags => tags;
        public IReadOnlyList<AbilityCost> Costs => costs;
        public TargetingDefinition Targeting => targeting;
        public IReadOnlyList<AbilityEffect> Effects => effects;
        public int SpellLevel => spellLevel;
    }
}
