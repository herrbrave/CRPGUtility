using CRPG.Core;
using UnityEngine;

namespace CRPG.Actors
{
    /// <summary>
    /// A consumable/replenishing pool a character can spend from (hit points, spell slots, ki, a homebrew
    /// ruleset's "Stamina", ...). The engine only knows it's a named pool with a maximum; how it's spent and
    /// what spending it does are contributed by feature/effect data (Abilities phase). <see cref="RestTrigger"/>
    /// is the one piece of restoration policy the engine does own generically, since "restores on a rest" is
    /// common enough across rulesets to be worth a real field rather than bespoke per-ruleset code (see RestService).
    /// </summary>
    [CreateAssetMenu(menuName = "CRPG Toolkit/Actors/Resource Definition")]
    public sealed class ResourceDefinition : DefinitionAsset
    {
        [SerializeField] private string displayName;
        [SerializeField, TextArea] private string description;
        [SerializeField] private RestTrigger restTrigger = RestTrigger.None;

        /// <summary>0 for a non-slot resource; a spell slot's real SRD level (1-9) otherwise. Lets
        /// AbilityResolutionService.Execute's upcastWithSlot parameter compute how many levels above an
        /// ability's own SpellLevel a slot was spent at.</summary>
        [SerializeField] private int spellSlotLevel;

        public string DisplayName => displayName;
        public string Description => description;
        public RestTrigger RestTrigger => restTrigger;
        public int SpellSlotLevel => spellSlotLevel;
    }
}
