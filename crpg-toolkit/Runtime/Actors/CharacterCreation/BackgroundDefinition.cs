using System.Collections.Generic;
using CRPG.Core;
using UnityEngine;

namespace CRPG.Actors
{
    [CreateAssetMenu(menuName = "CRPG Toolkit/Actors/Background Definition")]
    public sealed class BackgroundDefinition : DefinitionAsset
    {
        [SerializeField] private string displayName;
        [SerializeField, TextArea] private string description;
        [SerializeField] private List<DefinitionId> grantedFeatureIds = new();

        public string DisplayName => displayName;
        public string Description => description;
        public IReadOnlyList<DefinitionId> GrantedFeatureIds => grantedFeatureIds;
    }
}
