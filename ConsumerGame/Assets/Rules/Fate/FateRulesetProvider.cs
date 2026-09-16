using System;
using System.Collections.Generic;
using CRPG.Abilities;
using CRPG.Actors;
using CRPG.Core;
using UnityEngine;

namespace CRPG.Consumer.Fate
{
    /// <summary>
    /// Gate D's alternate <see cref="IRulesetDefinition"/> -- lives entirely in the consumer project, never in
    /// crpg-toolkit, since Gate D's whole point is proving a consumer can supply a ruleset without
    /// package-source edits. Real ScriptableObject asset (not runtime-reflection-authored like Task 5.1's
    /// test-only fixture) with inspector-authored content, mirroring <c>CRPG.Rules.SRD5.SRD5RulesetProvider</c>'s
    /// exact shape -- authored once via <see cref="Editor.FateContentAuthor"/>, never hand-edited YAML.
    /// </summary>
    [CreateAssetMenu(menuName = "Consumer Game/Fate Ruleset Provider")]
    public sealed class FateRulesetProvider : ScriptableObject, IRulesetDefinition
    {
        [Serializable]
        public struct SkillEntry
        {
            [SerializeField] private string queryKey;
            [SerializeField] private ResourceDefinition resource;

            public string QueryKey => queryKey;
            public ResourceDefinition Resource => resource;
        }

        [SerializeField] private List<DefinitionAsset> content = new();
        [SerializeField] private List<SkillEntry> skills = new();

        // Convenience direct references for the composition root -- this class is entirely consumer-owned,
        // so unlike SRD5RulesetProvider (which only needs the generic Content list) it's free to expose
        // exactly what ConsumerGameBootstrap needs without fragile string/DefinitionId lookups.
        [SerializeField] private ResourceDefinition actionResource;
        [SerializeField] private ResourceDefinition fatePointsResource;
        [SerializeField] private ResourceDefinition stressResource;
        [SerializeField] private AbilityDefinition strikeAbility;
        [SerializeField] private AbilityDefinition strikeInvokingAbility;

        public IReadOnlyList<DefinitionAsset> Content => content;
        public IReadOnlyList<SkillEntry> Skills => skills;
        public ResourceDefinition ActionResource => actionResource;
        public ResourceDefinition FatePointsResource => fatePointsResource;
        public ResourceDefinition StressResource => stressResource;
        public AbilityDefinition StrikeAbility => strikeAbility;
        public AbilityDefinition StrikeInvokingAbility => strikeInvokingAbility;

        public ResourceDefinition SkillResource(string queryKey)
        {
            foreach (var skill in skills)
                if (skill.QueryKey == queryKey) return skill.Resource;
            return null;
        }

        public string RulesetId => "fate-condensed";

        public void Register(IRulesetRegistrationBuilder builder)
        {
            builder.RegisterDefinitions(content);

            var skillsByQueryKey = new Dictionary<string, ResourceDefinition>();
            foreach (var skill in skills) skillsByQueryKey[skill.QueryKey] = skill.Resource;

            builder.RegisterPolicy<ISkillCheckRule>(new FateCheckRule(builder.Characters, skillsByQueryKey));
        }
    }
}
