using System.Collections.Generic;
using System.Reflection;
using CRPG.Actors;
using CRPG.Core;
using CRPG.Presentation.Animation;
using UnityEditor;
using UnityEngine;

// Dogfood-only tooling (see third_party_assets / TestProject conventions) -- not part of the shipped package.
//
// Authors the first CharacterAnimationProfile (Idle/Moving/Attack Idle/Attack Melee, sourced from Universal
// Animation Library[Standard]'s "UAL1_Standard.fbx" -- the non-root-motion variant, since the character's own
// NavMeshAgent already owns movement; combining that with root motion clips would double-apply displacement)
// and wires a CharacterPlayableAnimator onto the existing BaseHumanMaleCharacter prefab. Kept as its own
// authoring step, separate from BaseHumanMaleCharacterAuthor, matching how outfit equipment content was
// authored separately from the prefab itself -- run "Author Base Human Male Character" first if the prefab
// doesn't exist yet. The profile asset lives here in TestProject, not crpg-toolkit/, since it references
// third-party AnimationClips that must never ship with the package.
public static class CharacterAnimationAuthor
{
    private const string ProfileFolder = "Assets/Resources/Animations";
    private const string ProfilePath = ProfileFolder + "/CharacterAnimationProfile_HumanMale.asset";
    private const string PrefabPath = "Assets/Resources/NPCs/BaseHumanMaleCharacter.prefab";

    private const string StandardClipsPath =
        "Assets/ThirdPartyAssets/Universal Animation Library[Standard]/Universal Animation Library[Standard]/Unity/UAL1_Standard.fbx";

    // Task 2.3: reference speed (m/s) for continuous locomotion speed blending -- see
    // AnimationCueClip.ReferenceSpeed and PlayableCharacterAnimationDriver's speed-blending remarks. 0 means
    // "not a locomotion clip, don't blend." "Armature|Walk_Loop" is a non-root-motion clip (see the class
    // comment) with no principled authored speed to measure, so it's calibrated against the prefab's own
    // configured NavMeshAgent.speed (3.5, BaseHumanMaleCharacter.prefab) -- 1x playback at today's default
    // configured walk speed, scaling proportionally if that ever changes. Re-author if the agent's configured
    // speed changes materially.
    private const float WalkReferenceSpeed = 3.5f;

    private static readonly (string CueId, string ClipName, float Crossfade, bool Loop, float ReferenceSpeed)[] Cues =
    {
        ("anim-idle", "Armature|Idle_Loop", 0.2f, true, 0f),
        ("anim-moving", "Armature|Walk_Loop", 0.2f, true, WalkReferenceSpeed),
        ("anim-attack-idle", "Armature|Sword_Idle", 0.25f, true, 0f),
        ("anim-attack-melee", "Armature|Sword_Attack", 0.1f, false, 0f),
    };

    [MenuItem("CRPG Toolkit/Characters/Author Base Human Male Animations")]
    public static void Author()
    {
        var profile = BuildProfile();
        if (profile != null) WireIntoPrefab(profile);
    }

    private static CharacterAnimationProfile BuildProfile()
    {
        EnsureFolder(ProfileFolder);

        var clipsByName = new Dictionary<string, AnimationClip>();
        foreach (var asset in AssetDatabase.LoadAllAssetsAtPath(StandardClipsPath))
        {
            // The FBX importer also generates hidden "__preview__"-prefixed duplicates for its own Inspector
            // preview player -- not meant for runtime playback.
            if (asset is AnimationClip clip && !clip.name.StartsWith("__preview__")) clipsByName[clip.name] = clip;
        }

        var cueList = new List<AnimationCueClip>();
        foreach (var cue in Cues)
        {
            if (!clipsByName.TryGetValue(cue.ClipName, out var clip))
            {
                Debug.LogError($"[CharacterAnimationAuthor] Could not find clip '{cue.ClipName}' in {StandardClipsPath} -- aborting.");
                return null;
            }

            object boxed = new AnimationCueClip();
            SetField(boxed, "cueId", new DefinitionId(cue.CueId));
            SetField(boxed, "clip", clip);
            SetField(boxed, "crossfadeDuration", cue.Crossfade);
            SetField(boxed, "loop", cue.Loop);
            SetField(boxed, "referenceSpeed", cue.ReferenceSpeed);
            cueList.Add((AnimationCueClip)boxed);
        }

        if (AssetDatabase.LoadAssetAtPath<CharacterAnimationProfile>(ProfilePath) != null) AssetDatabase.DeleteAsset(ProfilePath);

        var profile = ScriptableObject.CreateInstance<CharacterAnimationProfile>();
        SetField(profile, "cues", cueList);
        AssetDatabase.CreateAsset(profile, ProfilePath);
        EditorUtility.SetDirty(profile);
        AssetDatabase.SaveAssets();

        Debug.Log($"[CharacterAnimationAuthor] Authored {ProfilePath} with {cueList.Count} cues.");
        return AssetDatabase.LoadAssetAtPath<CharacterAnimationProfile>(ProfilePath);
    }

    private static void WireIntoPrefab(CharacterAnimationProfile profile)
    {
        var prefabRoot = PrefabUtility.LoadPrefabContents(PrefabPath);
        if (prefabRoot == null)
        {
            Debug.LogError($"[CharacterAnimationAuthor] Could not load prefab at {PrefabPath} -- run 'Author Base Human Male Character' first.");
            return;
        }

        var animator = prefabRoot.GetComponent<CharacterPlayableAnimator>();
        if (animator == null) animator = prefabRoot.AddComponent<CharacterPlayableAnimator>();
        SetField(animator, "profile", profile);

        PrefabUtility.SaveAsPrefabAsset(prefabRoot, PrefabPath);
        PrefabUtility.UnloadPrefabContents(prefabRoot);

        Debug.Log($"[CharacterAnimationAuthor] Wired CharacterPlayableAnimator + profile onto {PrefabPath}.");
    }

    private static void SetField(object target, string fieldName, object value)
    {
        var type = target.GetType();
        while (type != null)
        {
            var field = type.GetField(fieldName, BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.DeclaredOnly);
            if (field != null)
            {
                field.SetValue(target, value);
                return;
            }

            type = type.BaseType;
        }

        throw new System.Exception($"Field '{fieldName}' not found on {target.GetType().Name} or its base types.");
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
