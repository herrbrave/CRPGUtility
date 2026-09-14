namespace CRPG.Core
{
    /// <summary>Task 4.2: resolves an entity's faction, generalizing the exact "same faction = ally, different
    /// = enemy" rule CombatEncounter.GetEnemiesOf/GetAlliesOf already use for in-combat relation checks, so
    /// ability-target validation can apply the same rule outside a combat encounter too. TryGet-shaped
    /// (mirrors ICharacterRegistry.TryGetCharacter) rather than a bare getter deliberately: "no faction data
    /// available for this entity" must be a distinguishable case a caller can reject on, never silently
    /// compared as default(DefinitionId) == default(DefinitionId) and treated as a false ally match. No
    /// shipped default implementation -- faction storage is inherently game-specific, same reasoning
    /// IPositionProvider already established.</summary>
    public interface IFactionProvider
    {
        bool TryGetFaction(RuntimeEntityId entity, out DefinitionId faction);
    }
}
