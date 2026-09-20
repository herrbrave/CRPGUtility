using System.Collections.Generic;
using CRPG.Navigation.Grid.Tilemaps;
using CRPG.Presentation.Sprites;
using UnityEngine;

namespace CRPG.Editor.Validation
{
    /// <summary>
    /// Validates a <see cref="SemanticTile"/> -- pure logic, no scene. An unclassified tile is an error (no default meaning is allowed); a tile with no
    /// sprite draws nothing; a sorted-object semantic (wall, prop, decor, door) needs an object prefab with a SpriteRenderer and a
    /// <see cref="StaticSpriteSorter"/> or it will not depth-sort; a flat semantic (ground, blocked) should not carry a prefab. Never mutates the asset.
    /// </summary>
    public static class SemanticTileValidator
    {
        public static List<ValidationResult> Validate(SemanticTile tile, string assetPath)
        {
            var results = new List<ValidationResult>();
            if (tile == null) return results;

            if (!tile.IsClassified)
            {
                results.Add(new ValidationResult(ValidationSeverity.Error, assetPath, "semantic",
                    "This tile has no semantic (Unclassified): nothing can tell whether it is walkable, a wall or a prop.",
                    "Pick a semantic, or remove the tile from every palette."));
            }

            if (tile.sprite == null)
                results.Add(new ValidationResult(ValidationSeverity.Error, assetPath, "sprite", "The tile has no sprite, so it draws nothing (and gives instances no sprite).", "Assign a sprite."));

            var prefab = tile.gameObject;
            if (tile.IsSortedObject)
            {
                if (prefab == null)
                {
                    results.Add(new ValidationResult(ValidationSeverity.Error, assetPath, "gameObject",
                        $"A {tile.Semantic} tile is drawn as a depth-sorted object but has no object prefab.", "Assign the shared sorted-object prefab."));
                }
                else
                {
                    if (prefab.GetComponentInChildren<SpriteRenderer>(true) == null)
                        results.Add(new ValidationResult(ValidationSeverity.Error, assetPath, "gameObject",
                            $"The prefab '{prefab.name}' has no SpriteRenderer, so the tile's sprite has nowhere to go.", "Add a SpriteRenderer to the prefab."));
                    if (prefab.GetComponentInChildren<StaticSpriteSorter>(true) == null)
                        results.Add(new ValidationResult(ValidationSeverity.Warning, assetPath, "gameObject",
                            $"The prefab '{prefab.name}' has no StaticSpriteSorter, so its instances will not depth-sort against characters.", "Add a StaticSpriteSorter to the prefab."));
                    if (tile.Semantic == TileSemantic.Door && prefab.GetComponentInChildren<CRPG.Gameplay.Interactions.Adapters.TileDoor>(true) == null)
                        results.Add(new ValidationResult(ValidationSeverity.Warning, assetPath, "gameObject",
                            $"The prefab '{prefab.name}' has no TileDoor, so this door will not block its cell or be usable.", "Use a door prefab with the TileDoor component."));
                    if ((tile.flags & UnityEngine.Tilemaps.TileFlags.InstantiateGameObjectRuntimeOnly) == 0)
                        results.Add(new ValidationResult(ValidationSeverity.Warning, assetPath, "flags",
                            "The prefab is also instantiated while painting in the editor (InstantiateGameObjectRuntimeOnly is off), which draws each painted sprite twice.", "Turn InstantiateGameObjectRuntimeOnly on (SemanticTile.Configure does)."));
                }
            }
            else if (prefab != null)
            {
                results.Add(new ValidationResult(ValidationSeverity.Warning, assetPath, "gameObject",
                    $"A {tile.Semantic} tile is drawn by the tilemap itself; its object prefab '{prefab.name}' will be instantiated for nothing.", "Clear the prefab."));
            }

            return results;
        }
    }
}
