using System.Collections.Generic;
using CRPG.Actors;
using CRPG.Core;
using CRPG.Rules.SRD5;
using UnityEngine;

namespace BarrowsEnd
{
    /// <summary>
    /// Barrow's End's ruleset: everything SRD5 registers (content and default policies) plus the campaign's own content -- the sprite
    /// layer catalogs, the head equipment slot SRD5 lacks, the gear items and their sprite visual profiles, and the debug abilities.
    /// Populated by <c>BarrowsEndGearAuthor</c> (Editor). A save records this <see cref="RulesetId"/>, and the load-time content
    /// validator resolves every equipped slot id against the registered definitions, so an item's slot MUST be registered here.
    /// </summary>
    [CreateAssetMenu(menuName = "Barrows End/Ruleset Provider")]
    public sealed class BarrowsEndRulesetProvider : ScriptableObject, IRulesetDefinition
    {
        [SerializeField] private SRD5RulesetProvider srd5;
        [SerializeField] private List<DefinitionAsset> extraContent = new();

        public string RulesetId => "barrowsend-srd5e2014";

        public SRD5RulesetProvider Srd5 => srd5;
        public IReadOnlyList<DefinitionAsset> ExtraContent => extraContent;

        public void Register(IRulesetRegistrationBuilder builder)
        {
            if (srd5 == null) throw new System.InvalidOperationException($"{name}: the SRD5 provider reference is missing -- run 'Barrows End/Content/Author Gear And Ruleset'.");
            srd5.Register(builder);
            builder.RegisterDefinitions(extraContent);
        }
    }
}
