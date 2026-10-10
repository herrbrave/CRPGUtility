using UnityEditor;

namespace VikingRaid.Appearance.Editor
{
    /// <summary>Import settings for the Sidekick colour maps (<c>T_*ColorMap.png</c>, 32x32 flat cells): READABLE, so a skin tone or hair colour can be composed on top of them (<see cref="ModelAppearanceColours"/>),
    /// and UNCOMPRESSED, because with one pixel per cell block compression visibly shifts the colours, which would make a composed copy look different from the untouched look. The files live in the ignored
    /// Synty folder, so this is applied locally on import (never committed).</summary>
    public sealed class SidekickColourMapImportSettings : AssetPostprocessor
    {
        public const string Folder = "Assets/Synty/SidekickCharacters/Characters/";

        /// <summary>Bump when the settings below change: Unity reimports every texture the postprocessor applies to.</summary>
        public override uint GetVersion() => 1;

        private void OnPreprocessTexture()
        {
            if (!assetPath.StartsWith(Folder) || !assetPath.EndsWith("ColorMap.png")) return;
            var importer = (TextureImporter)assetImporter;
            importer.isReadable = true;
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            importer.mipmapEnabled = false;
            importer.filterMode = UnityEngine.FilterMode.Point;
        }
    }
}
