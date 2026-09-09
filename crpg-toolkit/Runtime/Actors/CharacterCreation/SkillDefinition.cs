using CRPG.Core;
using UnityEngine;

namespace CRPG.Actors
{
    /// <summary>A skill's identity and which stat it's checked against (e.g. Stealth -&gt; Dexterity). A skill
    /// *proficiency* is just a bare DefinitionId in CharacterState.Proficiencies -- this is only the metadata
    /// half, for authoring and for a future skill-check rule query to look up.</summary>
    [CreateAssetMenu(menuName = "CRPG Toolkit/Actors/Skill Definition")]
    public sealed class SkillDefinition : DefinitionAsset
    {
        [SerializeField] private string displayName;
        [SerializeField] private StatDefinition governingStat;

        public string DisplayName => displayName;
        public StatDefinition GoverningStat => governingStat;
    }
}
