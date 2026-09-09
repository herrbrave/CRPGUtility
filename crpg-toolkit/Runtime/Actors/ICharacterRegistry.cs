using CRPG.Core;

namespace CRPG.Actors
{
    /// <summary>Looks up a live CharacterState by its entity id. A minimal seam so systems (Abilities, Combat,
    /// Dialogue) don't need to know how/where characters are actually stored (a scene registry, a save-loaded
    /// dictionary, ...).</summary>
    public interface ICharacterRegistry
    {
        bool TryGetCharacter(RuntimeEntityId id, out CharacterState character);
    }
}
