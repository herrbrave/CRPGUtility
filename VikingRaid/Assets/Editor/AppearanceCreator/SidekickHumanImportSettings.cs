using UnityEditor;

namespace VikingRaid.Appearance.Editor
{
    /// <summary>Import settings for the Sidekick human parts WE author (Blender-exported outfit pieces under <c>Assets/Appearance/SidekickHumans/</c>): they are only skinned meshes bound to the master skeleton by bone
    /// name, so no avatar (Generic rig, NoAvatar: a rig of None would import them as static meshes), no animation, no materials (the colour map is applied by the applier), readable meshes (BakeMesh, tests), and the exported normals kept.</summary>
    public sealed class SidekickHumanImportSettings : AssetPostprocessor
    {
        public const string Folder = "Assets/Appearance/SidekickHumans/";

        /// <summary>Bump when the settings below change: Unity reimports every model the postprocessor applies to.</summary>
        public override uint GetVersion() => 2;

        private void OnPreprocessModel()
        {
            if (!assetPath.StartsWith(Folder)) return;
            var importer = (ModelImporter)assetImporter;
            // NOT None: with no rig at all Unity imports the meshes as STATIC renderers and the skinning is lost. Generic without an avatar keeps the SkinnedMeshRenderer and its bones.
            importer.animationType = ModelImporterAnimationType.Generic;
            importer.avatarSetup = ModelImporterAvatarSetup.NoAvatar;
            importer.importAnimation = false;
            importer.materialImportMode = ModelImporterMaterialImportMode.None;
            importer.isReadable = true;
            importer.importBlendShapes = true;
            importer.importCameras = false;
            importer.importLights = false;
            importer.importNormals = ModelImporterNormals.Import;
            importer.meshCompression = ModelImporterMeshCompression.Off;
        }
    }
}
