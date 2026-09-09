namespace CRPG.Core
{
    /// <summary>
    /// Marker for an immutable authored data asset (a RaceDefinition, ItemDefinition, AbilityDefinition, ...).
    /// Definitions are shared and read-only at runtime; see <see cref="DefinitionAsset"/> and the CharacterState-style
    /// runtime state types that reference definitions by <see cref="DefinitionId"/> instead of copying/mutating them.
    /// </summary>
    public interface IDefinition
    {
        DefinitionId Id { get; }
    }
}
