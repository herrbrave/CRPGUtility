using System.Collections.Generic;
using System.Linq;
using CRPG.Core;
using UnityEngine;

namespace CRPG.Actors
{
    /// <summary>Shared shape for ClassDefinition and SubclassDefinition: a list of features granted at each level.</summary>
    public abstract class LeveledFeatureDefinition : DefinitionAsset
    {
        [SerializeField] private string displayName;
        [SerializeField, TextArea] private string description;
        [SerializeField] private List<ClassLevelGrant> levelProgression = new();

        public string DisplayName => displayName;
        public string Description => description;
        public IReadOnlyList<ClassLevelGrant> LevelProgression => levelProgression;

        public IEnumerable<DefinitionId> FeaturesGrantedThroughLevel(int level) =>
            levelProgression.Where(grant => grant.Level <= level).SelectMany(grant => grant.FeatureIds);
    }
}
