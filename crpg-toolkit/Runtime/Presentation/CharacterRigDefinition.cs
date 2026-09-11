using CRPG.Core;
using UnityEngine;

namespace CRPG.Presentation
{
    /// <summary>
    /// Identifies one Humanoid-compatible skeleton family a Model can declare membership in (e.g. "every
    /// Quaternius Universal Base Characters body shares this 65-bone rig") -- the seam Task 2.4's equipment
    /// rig-compatibility validation (Docs/Design/Production-Foundation.md sec.4.5: "compatible rig-family ID
    /// plus explicit source-&gt;target relative bone paths or validated mapping asset") is meant to build on.
    /// Deliberately minimal here: just a stable authored identity plus a reference Avatar for later validation
    /// to compare against, not yet the full bone-mapping/validator asset the design describes -- nothing
    /// consumes this for compatibility checks yet.
    /// </summary>
    [CreateAssetMenu(menuName = "CRPG Toolkit/Presentation/Character Rig Definition")]
    public sealed class CharacterRigDefinition : DefinitionAsset
    {
        [SerializeField] private string displayName;
        [SerializeField] private Avatar referenceAvatar;

        public string DisplayName => displayName;

        /// <summary>A known-good Humanoid Avatar for this rig family -- lets future tooling validate a
        /// candidate Model's own Avatar is actually structurally compatible rather than assuming from naming
        /// conventions or file source alone.</summary>
        public Avatar ReferenceAvatar => referenceAvatar;
    }
}
