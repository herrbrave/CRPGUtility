using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace VikingRaid.EditorTools
{
    /// <summary>
    /// Brings the Blender-authored archer clips (<c>Blender/Animations/Bow/export/BowAnimations.fbx</c>: <c>Bow_Draw</c>, 46 frames with the release on
    /// frame 29, and <c>Bow_Idle</c>, a 91-frame loop) into the project as Humanoid clips that retarget onto every Synty character. Idempotent;
    /// <see cref="CharacterRosterAuthor"/> calls <see cref="EnsureImported"/>.
    /// <para>Import settings: root height and XZ baked into the pose (the NavMeshAgent owns movement); root ROTATION is "Original" with an
    /// offset, NOT "Body Orientation": a full draw deliberately turns the chest ~45 degrees, and Body Orientation would average that into
    /// the root and rotate the whole character off its facing. <see cref="RotationOffset"/> is the measured correction (see
    /// BowAnimationTests: legs must face the character's forward).</para>
    /// </summary>
    public static class BowAnimationImport
    {
        public const string Folder = "Assets/Characters/Animation/Source";
        public const string FbxPath = Folder + "/BowAnimations.fbx";
        public const float RotationOffset = 180f;

        private static string SourceFbx => Path.GetFullPath(Path.Combine(Application.dataPath, "..", "..", "Blender", "Animations", "Bow", "export", "BowAnimations.fbx"));

        [MenuItem("Viking Raid/Task 1/Import Bow Animations")]
        public static void Run() => EnsureImported(force: true);

        public static void EnsureImported(bool force = false)
        {
            var target = Path.GetFullPath(Path.Combine(Application.dataPath, "..", FbxPath));
            if (!File.Exists(SourceFbx)) { Debug.LogError($"[BowAnimationImport] missing {SourceFbx} -- run Blender/Animations/Bow/01_build_bow_animations.py"); return; }

            var stale = !File.Exists(target) || File.GetLastWriteTimeUtc(SourceFbx) > File.GetLastWriteTimeUtc(target);
            if (!stale && !force && AssetImporter.GetAtPath(FbxPath) is ModelImporter existing && existing.clipAnimations.Length == 2) return;

            Directory.CreateDirectory(Path.GetDirectoryName(target));
            File.Copy(SourceFbx, target, true);
            AssetDatabase.Refresh();

            var importer = (ModelImporter)AssetImporter.GetAtPath(FbxPath);
            importer.animationType = ModelImporterAnimationType.Human;
            importer.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
            importer.importAnimation = true;

            var clips = importer.defaultClipAnimations;
            foreach (var clip in clips)
            {
                clip.lockRootHeightY = true;
                clip.keepOriginalPositionY = true;
                clip.lockRootPositionXZ = true;
                clip.keepOriginalPositionXZ = true;
                clip.lockRootRotation = true;
                clip.keepOriginalOrientation = true;
                clip.rotationOffset = RotationOffset;
                clip.loopTime = clip.name.EndsWith("Idle");
            }

            importer.clipAnimations = clips;
            importer.SaveAndReimport();

            var avatar = AssetDatabase.LoadAllAssetsAtPath(FbxPath).OfType<Avatar>().FirstOrDefault();
            var names = AssetDatabase.LoadAllAssetsAtPath(FbxPath).OfType<AnimationClip>().Where(c => !c.name.StartsWith("__preview__"))
                .Select(c => $"{c.name} ({c.length:0.00}s, human={c.isHumanMotion})");
            Debug.Log($"[BowAnimationImport] avatar valid={avatar != null && avatar.isValid} human={avatar != null && avatar.isHuman}; clips: {string.Join(", ", names)}");
        }

        public static AnimationClip Clip(string name) =>
            AssetDatabase.LoadAllAssetsAtPath(FbxPath).OfType<AnimationClip>().FirstOrDefault(c => c.name == name);
    }
}
