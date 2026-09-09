using CRPG.Core;
using UnityEngine;

namespace CRPG.Actors
{
    /// <summary>A named damage type (fire, poison, slashing, ...) a DamageEffect can be tagged with. Just an id
    /// -- DamageEffect stores a bare DefinitionId, not a direct reference, so CRPG.Abilities needs no dependency
    /// on this type at all. Untagged (DefinitionId.None) damage, as every DamageEffect authored before this
    /// existed already uses, simply never triggers resistance/immunity/vulnerability -- see
    /// IDamageResistanceRule.</summary>
    [CreateAssetMenu(menuName = "CRPG Toolkit/Actors/Damage Type Definition")]
    public sealed class DamageTypeDefinition : DefinitionAsset
    {
        [SerializeField] private string displayName;

        public string DisplayName => displayName;
    }
}
