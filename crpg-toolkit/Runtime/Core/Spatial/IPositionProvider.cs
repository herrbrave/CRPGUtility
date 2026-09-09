using UnityEngine;

namespace CRPG.Core
{
    /// <summary>Resolves a combatant's world position -- mirrors ICharacterRegistry's shape (id -> data).
    /// Lives in CRPG.Core alongside InfluenceMap: both CRPG.Abilities (CreateInfluenceSourceEffect needs a
    /// position to place a zone at) and CRPG.AI (Distance/InfluenceMap Consideration inputs) need it, and
    /// CRPG.AI already depends on CRPG.Abilities, so this can't live in either without a circular reference.</summary>
    public interface IPositionProvider
    {
        Vector3 GetPosition(RuntimeEntityId entity);
    }
}
