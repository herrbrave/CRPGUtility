using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using CRPG.Actors;
using CRPG.Core;
using CRPG.Editor.Character;
using UnityEditor;
using UnityEngine;

namespace VikingRaid.EditorTools.Spikes
{
    /// <summary>Task 0 spike (b)/(c) authoring: re-imports a COPY of the Quaternius UAL (CC0) as HUMANOID so its sword/hit/death
    /// clips retarget onto the Synty Humanoid rigs, builds one shared <see cref="CharacterAnimationProfile"/> mixing Synty goblin
    /// locomotion with UAL combat clips, and builds real wrapper prefabs through the toolkit's own
    /// <see cref="CharacterWrapperBuilder"/> for a Viking, a Sidekick goblin and a War Camp goblin. The PlayMode test
    /// (Assets/Tests/PlayMode/HumanoidPlaybackSpikeTests.cs) then plays them through the REAL
    /// CharacterPlayableAnimator/PlayableCharacterAnimationDriver -- the plan review's go/no-go for Humanoid playback.</summary>
    public static class AnimationSpikeAuthor
    {
        public const string UalFolder = "Assets/ThirdParty/UAL";
        public const string UalFbx = UalFolder + "/UAL1_Standard.fbx";
        public const string SpikeFolder = "Assets/Spike";
        public const string ProfilePath = SpikeFolder + "/SpikeAnimationProfile.asset";

        private const string TestProjectUal =
            "TestProject/Assets/ThirdPartyAssets/Universal Animation Library[Standard]/Universal Animation Library[Standard]";

        private const string GoblinIdle = "Assets/Synty/AnimationGoblinLocomotion/Animations/Polygon/Neutral/Idles/A_POLY_GBL_Idle_Standing_Neut.fbx";
        private const string GoblinWalk = "Assets/Synty/AnimationGoblinLocomotion/Animations/Polygon/Neutral/Locomotion/Walk/A_POLY_GBL_Walk_F_Neut.fbx";

        public static readonly (string Name, string ModelPath)[] Wrappers =
        {
            ("Viking", "Assets/Synty/PolygonVikingRealm/Prefabs/Characters/SM_Chr_Warrior_Male_01.prefab"),
            ("SidekickGoblin", "Assets/Synty/SidekickCharacters/Characters/GoblinFighters/GoblinFighter_01/GoblinFighter_01.prefab"),
            ("WarCampGoblin", "Assets/Synty/PolygonGoblinWarCamp/Prefabs/Characters/SM_Chr_Warrior_Male_01.prefab"),
        };

        [MenuItem("Viking Raid/Spikes/Author Animation Spike")]
        public static void Run()
        {
            CopyUalAsHumanoid();
            EnsureFolder(SpikeFolder);

            var attack = FindClip(UalFbx, "Sword_Attack");
            var hit = FindClip(UalFbx, "Hit_Chest");
            var death = FindClip(UalFbx, "Death01");
            var hero = BuildProfile(HeroProfilePath,
                ("anim-idle", FindClip(UalFbx, "Idle_Loop"), 0.2f, true),
                ("anim-moving", FindClip(UalFbx, "Walk_Loop"), 0.2f, true),
                ("anim-attack-melee", attack, 0.1f, false),
                ("anim-hit", hit, 0.05f, false),
                ("anim-death", death, 0.1f, false));
            var goblin = BuildProfile(GoblinProfilePath,
                ("anim-idle", FindClip(GoblinIdle), 0.2f, true),
                ("anim-moving", FindClip(GoblinWalk), 0.2f, true),
                ("anim-attack-melee", attack, 0.1f, false),
                ("anim-hit", hit, 0.05f, false),
                ("anim-death", death, 0.1f, false));
            if (hero == null || goblin == null) return;

            foreach (var (name, modelPath) in Wrappers) BuildWrapper(name, modelPath, name == "Viking" ? hero : goblin);
            AssetDatabase.SaveAssets();
            Debug.Log("[AnimationSpikeAuthor] done");
        }

        /// <summary>Idempotent: copies the UAL FBX from TestProject (CC0) and imports it as Humanoid with root height/orientation baked, unless
        /// that has already been done. Also called by the Task 1 roster author so a fresh checkout can rebuild everything from the menu.</summary>
        public static void EnsureUalHumanoid()
        {
            var existing = AssetImporter.GetAtPath(UalFbx) as ModelImporter;
            if (existing != null && existing.animationType == ModelImporterAnimationType.Human && existing.clipAnimations.Length > 0) return;
            CopyUalAsHumanoid();
        }

        private static void CopyUalAsHumanoid()
        {
            var repoRoot = Path.GetFullPath(Path.Combine(Application.dataPath, "..", ".."));
            var projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            var sourceDir = Path.Combine(repoRoot, TestProjectUal);
            var targetDir = Path.Combine(projectRoot, UalFolder);
            Directory.CreateDirectory(targetDir);

            // Copy, never move/modify the TestProject original (CC0, but it belongs to that project). The .meta carries the
            // FBX's clip settings; only the rig type is patched.
            File.Copy(Path.Combine(sourceDir, "Unity", "UAL1_Standard.fbx"), Path.Combine(targetDir, "UAL1_Standard.fbx"), true);
            File.Copy(Path.Combine(sourceDir, "Unity", "UAL1_Standard.fbx.meta"), Path.Combine(targetDir, "UAL1_Standard.fbx.meta"), true);
            foreach (var doc in new[] { "License.txt", "README.txt" })
                File.Copy(Path.Combine(sourceDir, doc), Path.Combine(targetDir, doc), true);

            AssetDatabase.Refresh();

            var importer = (ModelImporter)AssetImporter.GetAtPath(UalFbx);
            importer.animationType = ModelImporterAnimationType.Human;
            importer.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;

            // Bake root transform into the pose for every clip. The characters are driven by a NavMeshAgent with
            // applyRootMotion=false, so a clip whose root drops or turns (death, hit reactions) would otherwise lose that
            // component: the first spike run left a dead character floating at standing hip height.
            var clips = importer.defaultClipAnimations;
            foreach (var clip in clips)
            {
                clip.lockRootHeightY = true;
                clip.keepOriginalPositionY = true;
                clip.lockRootRotation = true;
                clip.keepOriginalOrientation = false; // Body Orientation: face the rig forward whatever the source clip faced
                clip.lockRootPositionXZ = true;
                clip.keepOriginalPositionXZ = true;
            }

            importer.clipAnimations = clips;
            importer.SaveAndReimport();

            var avatar = AssetDatabase.LoadAllAssetsAtPath(UalFbx).OfType<Avatar>().FirstOrDefault();
            Debug.Log($"[AnimationSpikeAuthor] UAL avatar: {(avatar == null ? "NONE" : $"valid={avatar.isValid} human={avatar.isHuman}")}");
        }

        private static AnimationClip FindClip(string assetPath, string nameContains = null)
        {
            var clips = AssetDatabase.LoadAllAssetsAtPath(assetPath).OfType<AnimationClip>().Where(c => !c.name.StartsWith("__preview__")).ToList();
            Debug.Log($"[AnimationSpikeAuthor] clips in {Path.GetFileName(assetPath)}: {string.Join(", ", clips.Select(c => $"{c.name} ({c.length:0.00}s, human={c.isHumanMotion})"))}");
            if (nameContains == null) return clips.FirstOrDefault();
            return clips.FirstOrDefault(c => c.name == "Armature|" + nameContains) ?? clips.FirstOrDefault(c => c.name.Contains(nameContains));
        }

        public const string HeroProfilePath = SpikeFolder + "/SpikeHeroProfile.asset";
        public const string GoblinProfilePath = SpikeFolder + "/SpikeGoblinProfile.asset";

        // Finding from the first spike run: every Polygon-family rig shares one skeleton, so the Polygon goblin idle/walk
        // (a hunched goblin stance) plays unchanged on a Viking -- technically fine, visually wrong. Heroes therefore use
        // the UAL's neutral humanoid locomotion; goblins keep the goblin pack. Combat cues come from the UAL for both.
        private static CharacterAnimationProfile BuildProfile(string path, params (string CueId, AnimationClip Clip, float Crossfade, bool Loop)[] sources)
        {
            var cues = new List<AnimationCueClip>();
            foreach (var (cueId, clip, crossfade, loop) in sources)
            {
                if (clip == null) { Debug.LogError($"[AnimationSpikeAuthor] no clip for {cueId}"); return null; }
                object boxed = new AnimationCueClip();
                SetField(boxed, "cueId", new DefinitionId(cueId));
                SetField(boxed, "clip", clip);
                SetField(boxed, "crossfadeDuration", crossfade);
                SetField(boxed, "loop", loop);
                cues.Add((AnimationCueClip)boxed);
            }

            var profile = AssetDatabase.LoadAssetAtPath<CharacterAnimationProfile>(path);
            if (profile == null)
            {
                profile = ScriptableObject.CreateInstance<CharacterAnimationProfile>();
                AssetDatabase.CreateAsset(profile, path);
            }

            SetField(profile, "cues", cues);
            EditorUtility.SetDirty(profile);
            AssetDatabase.SaveAssets();
            return AssetDatabase.LoadAssetAtPath<CharacterAnimationProfile>(path);
        }

        private static void BuildWrapper(string name, string modelPath, CharacterAnimationProfile profile)
        {
            var model = AssetDatabase.LoadAssetAtPath<GameObject>(modelPath);
            if (model == null) { Debug.LogError($"[AnimationSpikeAuthor] missing model {modelPath}"); return; }

            var spec = new CharacterSetupSpec { ModelPrefab = model, AnimationProfile = profile };
            var path = $"{SpikeFolder}/Wrapper_{name}.prefab";
            CharacterWrapperBuilder.BuildOrUpdate(spec, path);

            // The Synty prefabs carry applyRootMotion=1 and CullUpdateTransforms: root motion would drift the Model off
            // CharacterRoot (the NavMeshAgent owns movement), and culling would stop animating off-screen models.
            var contents = PrefabUtility.LoadPrefabContents(path);
            try
            {
                foreach (var animator in contents.GetComponentsInChildren<Animator>(true))
                {
                    animator.applyRootMotion = false;
                    animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
                }

                PrefabUtility.SaveAsPrefabAsset(contents, path);
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(contents);
            }

            Debug.Log($"[AnimationSpikeAuthor] wrapper {path}");
        }

        private static void SetField(object target, string fieldName, object value)
        {
            var type = target.GetType();
            while (type != null)
            {
                var field = type.GetField(fieldName, BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.DeclaredOnly);
                if (field != null) { field.SetValue(target, value); return; }
                type = type.BaseType;
            }

            throw new System.Exception($"Field '{fieldName}' not found on {target.GetType().Name}.");
        }

        private static void EnsureFolder(string assetPath)
        {
            if (AssetDatabase.IsValidFolder(assetPath)) return;
            var parts = assetPath.Split('/');
            var current = parts[0];
            for (var i = 1; i < parts.Length; i++)
            {
                var next = $"{current}/{parts[i]}";
                if (!AssetDatabase.IsValidFolder(next)) AssetDatabase.CreateFolder(current, parts[i]);
                current = next;
            }
        }
    }
}
