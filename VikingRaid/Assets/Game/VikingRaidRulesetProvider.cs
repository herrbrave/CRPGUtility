using System.Collections.Generic;
using CRPG.Actors;
using CRPG.Core;
using CRPG.Rules.SRD5;
using UnityEngine;

namespace VikingRaid.Game
{
    /// <summary>
    /// Viking Raid's ruleset: everything SRD5 registers (content and default policies) plus this game's own content (Movement and verse-use
    /// resources, the Reckless condition, armour and multiattack features, abilities, the 15 combatant NpcDefinitions and the goblin behaviour sets).
    /// Populated by <c>GameContentAuthor</c> (Editor). Same shape as BarrowsEnd's provider; a generic "extended ruleset provider" is a toolkit promotion
    /// candidate (every consumer game writes this same 20 lines).
    /// </summary>
    [CreateAssetMenu(menuName = "Viking Raid/Ruleset Provider")]
    public sealed class VikingRaidRulesetProvider : ScriptableObject, IRulesetDefinition
    {
        [SerializeField] private SRD5RulesetProvider srd5;
        [SerializeField] private List<DefinitionAsset> extraContent = new();

        public string RulesetId => "vikingraid-srd5e2014";

        public SRD5RulesetProvider Srd5 => srd5;
        public IReadOnlyList<DefinitionAsset> ExtraContent => extraContent;

        public void Register(IRulesetRegistrationBuilder builder)
        {
            if (srd5 == null) throw new System.InvalidOperationException($"{name}: the SRD5 provider reference is missing -- run 'Viking Raid/Task 2/Author Content'.");
            srd5.Register(builder);
            builder.RegisterDefinitions(extraContent);
        }
    }
}
