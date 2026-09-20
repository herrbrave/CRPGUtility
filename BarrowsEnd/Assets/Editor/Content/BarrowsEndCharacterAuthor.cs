using System.IO;
using CRPG.Editor.Character;
using UnityEditor;
using UnityEngine;

namespace BarrowsEnd.Editor
{
    /// <summary>
    /// Builds the campaign's ONE base character prefab through the toolkit's CharacterWrapperBuilder: Grid movement + layered Sprite
    /// presentation. Party members, NPCs and monsters all use it -- what a character looks like is data (CharacterState.Appearance /
    /// NpcDefinition.appearance resolved through the sprite catalogs), not a prefab difference. Idempotent: rebuilding updates the
    /// wrapper in place and keeps its GUID.
    /// </summary>
    public static class BarrowsEndCharacterAuthor
    {
        public const string OutputDir = "Assets/Content/Characters";
        public const string SpriteCharacterPath = OutputDir + "/SpriteCharacter.prefab";

        public static CharacterSetupSpec Spec() => new()
        {
            Movement = MovementBackend.Grid,
            Presentation = PresentationKind.Sprite,
            // The campaign plays SRD5, whose hit-point resource is "resource-hp": the sprite combat cues use it to know when a downed character is
            // genuinely back on its feet. (The toolkit leaves this empty so its engine code stays ruleset-agnostic.)
            SpriteHitPointResourceId = new CRPG.Core.DefinitionId("resource-hp"),
        };

        [MenuItem("Barrows End/Content/Author Base Sprite Character")]
        public static void Author()
        {
            Directory.CreateDirectory(OutputDir);
            AssetDatabase.Refresh();
            CharacterWrapperBuilder.BuildOrUpdate(Spec(), SpriteCharacterPath);
            AssetDatabase.SaveAssets();
            Debug.Log($"[BarrowsEnd] Built {SpriteCharacterPath}");
        }
    }
}
