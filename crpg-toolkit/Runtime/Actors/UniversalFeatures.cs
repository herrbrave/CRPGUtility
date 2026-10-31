using CRPG.Core;

namespace CRPG.Actors
{
    /// <summary>Reconciles ruleset-wide grants for fresh characters and older saves, without duplicating grants or refilling saved resources.</summary>
    public static class UniversalFeatures
    {
        public static void Grant(CharacterState character, IRulesDatabase rules)
        {
            foreach (FeatureDefinition feature in rules.All<FeatureDefinition>())
            {
                if (!feature.Universal || character.HasFeature(feature.Id)) continue;
                character.GrantFeature(feature.Id);
                foreach (ResourceGrant grant in feature.GrantedResources)
                {
                    if (!character.TryGetResource(grant.Resource.Id, out _)) character.AddResource(new ResourceState(grant.Resource.Id, grant.MaxAmount));
                }
                foreach (DefinitionId proficiency in feature.GrantedProficiencies) character.GrantProficiency(proficiency);
            }
        }
    }
}
