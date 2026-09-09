using CRPG.Core;
using UnityEngine;

namespace CRPG.Actors
{
    /// <summary>
    /// A statistic a ruleset defines for its characters (Strength, Sanity, whatever a homebrew ruleset wants).
    /// The engine only knows it's a named, described number; how a raw score becomes a usable modifier is a
    /// ruleset-authored <c>NumericRuleQuery</c> (see CRPG.Rules), never baked in here.
    /// </summary>
    [CreateAssetMenu(menuName = "CRPG Toolkit/Actors/Stat Definition")]
    public sealed class StatDefinition : DefinitionAsset
    {
        [SerializeField] private string displayName;
        [SerializeField] private string abbreviation;
        [SerializeField, TextArea] private string description;

        public string DisplayName => displayName;
        public string Abbreviation => abbreviation;
        public string Description => description;
    }
}
