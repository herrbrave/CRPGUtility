using System.Collections.Generic;
using CRPG.Core;
using UnityEngine;

namespace CRPG.Actors
{
    /// <summary>
    /// A playable race, modeled as nothing more than "grants these features". Subraces aren't a separate
    /// definition type -- a race that offers subraces just grants a feature carrying a
    /// SelectFeature ChoiceRequirement whose Options are the candidate subrace features (each its own
    /// FeatureDefinition with its own stat bonuses/traits). That reuses the generic choice system instead of
    /// inventing race-specific branching.
    /// </summary>
    [CreateAssetMenu(menuName = "CRPG Toolkit/Actors/Race Definition")]
    public sealed class RaceDefinition : DefinitionAsset
    {
        [SerializeField] private string displayName;
        [SerializeField, TextArea] private string description;
        [SerializeField] private List<DefinitionId> grantedFeatureIds = new();

        public string DisplayName => displayName;
        public string Description => description;
        public IReadOnlyList<DefinitionId> GrantedFeatureIds => grantedFeatureIds;
    }
}
