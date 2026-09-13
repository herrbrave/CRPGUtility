using System.IO;
using System.Reflection;
using CRPG.Actors;
using CRPG.Gameplay;
using CRPG.Inventory;
using CRPG.Navigation;
using CRPG.Presentation;
using CRPG.Presentation.Animation;
using CRPG.Presentation.Equipment;
using UnityEditor;
using UnityEngine;
using UnityEngine.AI;

// Dogfood-only tooling (see third_party_assets / TestProject conventions) -- not part of the shipped package.
//
// Builds/updates the CharacterRoot/VisualRoot/Model wrapper (Docs/Design/Production-Foundation.md sec.4.1)
// around the Universal Base Characters pack's Superhero_Male_FullBody.fbx -- not a pre-dressed outfit, since
// clothing is equipment (see PeasantOutfitEquipmentAuthor): EquipmentVisualBinder instantiates and rebinds
// each equipped piece's bones onto this body's own skeleton at runtime via an explicit, authored
// RigBindingMap (Task 2.4) -- the two packs share the exact same 65-bone rig, verified via a live Editor
// query rather than assumed from file names. Explicitly (re)configures the FBX as a Generic rig with a
// Generic Avatar -- NOT Humanoid, corrected after a real visually-reproduced bug (see ConfigureGenericRig's
// own remarks): a Humanoid avatar actively corrupts this system's raw Playables-based clip playback.
//
// Task 1.3: CharacterRoot (gameplay: collider/rigidbody/NavMeshAgent/NavMeshCharacterMotor/CharacterAgent/
// CharacterVisualEquipment/CharacterPlayableAnimator) is now a separate GameObject from VisualRoot/Model (the
// FBX instance, art only) -- CharacterView (added last, so its Awake() finds Model already parented) binds
// them together. Updates the EXISTING prefab asset in place via SaveAsPrefabAsset onto the same path, never
// AssetDatabase.DeleteAsset first -- preserves the asset's own GUID (confirmed empirically) and therefore
// GilroyMasters.asset's reference to it; the previous delete-then-recreate pattern silently broke that
// reference on every re-run (Task 1.3 code review).
public static class BaseHumanMaleCharacterAuthor
{
    private const string ModelPath =
        "Assets/ThirdPartyAssets/Universal Base Characters[Standard]/Universal Base Characters[Standard]/Base Characters/Unity/Superhero_Male_FullBody.fbx";

    private const string PrefabFolder = "Assets/Resources/NPCs";
    private const string PrefabPath = PrefabFolder + "/BaseHumanMaleCharacter.prefab";
    private const string BaseNpcPrefabPath = PrefabFolder + "/BaseNPC.prefab";
    private const string GilroyDefinitionPath = "Assets/Resources/NPCs/GilroyMasters.asset";

    [MenuItem("CRPG Toolkit/Characters/Author Base Human Male Character")]
    public static void Author()
    {
        if (!ConfigureGenericRig()) return;

        var model = AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath);
        if (model == null)
        {
            Debug.LogError($"[BaseHumanMaleCharacterAuthor] Could not load model at {ModelPath}.");
            return;
        }

        // Rebuilding the hierarchy below must not silently discard whatever CharacterAnimationAuthor (a
        // separate authoring step) already wired onto an existing prefab's CharacterPlayableAnimator.
        var existingProfile = LoadExistingAnimationProfile();

        var root = new GameObject("BaseHumanMaleCharacter");
        BuildCharacterRoot(root, existingProfile);

        var visualRoot = new GameObject("VisualRoot");
        visualRoot.transform.SetParent(root.transform, false);

        var modelInstance = (GameObject)PrefabUtility.InstantiatePrefab(model);
        modelInstance.name = "Model";
        modelInstance.transform.SetParent(visualRoot.transform, false);
        // This pack's body and Generic clips face -Z. Normalize the art to CharacterRoot's
        // +Z facing contract outside the animated skeleton; never reverse motion or clip playback.
        modelInstance.transform.localRotation = Quaternion.Euler(0f, 180f, 0f);

        // CharacterView.Awake() runs the instant it's added to this already-active GameObject, and
        // auto-binds to whatever child it finds -- so Model must already be parented first.
        visualRoot.AddComponent<CharacterView>();

        if (!Directory.Exists(PrefabFolder)) AssetDatabase.CreateFolder("Assets/Resources", "NPCs");

        var savedPrefab = PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
        Object.DestroyImmediate(root);

        Debug.Log($"[BaseHumanMaleCharacterAuthor] Updated {PrefabPath} in place (CharacterRoot/VisualRoot/Model).");

        WireIntoGilroyMasters(savedPrefab);
    }

    // BaseNPC.prefab was previously an orphaned, empty GameObject with no components and no referrers
    // anywhere in the project -- confirmed by grep before touching it. Task 1.3 turns it into an actual
    // reusable, bare CharacterRoot/VisualRoot template (gameplay components only, no Model yet) matching the
    // new prefab contract, rather than leaving it a pointless stub. Same in-place update discipline as
    // Author(): SaveAsPrefabAsset onto the existing path, never delete first.
    [MenuItem("CRPG Toolkit/Characters/Author Base NPC Template")]
    public static void AuthorBaseNpcTemplate()
    {
        var root = new GameObject("BaseNPC");
        BuildCharacterRoot(root, existingProfile: null);

        var visualRoot = new GameObject("VisualRoot");
        visualRoot.transform.SetParent(root.transform, false);
        visualRoot.AddComponent<CharacterView>(); // no Model child yet -- stays unbound until SetModel is called

        if (!Directory.Exists(PrefabFolder)) AssetDatabase.CreateFolder("Assets/Resources", "NPCs");

        PrefabUtility.SaveAsPrefabAsset(root, BaseNpcPrefabPath);
        Object.DestroyImmediate(root);

        Debug.Log($"[BaseHumanMaleCharacterAuthor] Updated {BaseNpcPrefabPath} in place (bare CharacterRoot/VisualRoot template, no Model).");
    }

    private static void BuildCharacterRoot(GameObject root, CharacterAnimationProfile existingProfile)
    {
        // Same trio OakhavenContentAuthor's player setup used -- a NavMeshAgent moves the transform directly
        // and adds no Rigidbody of its own, so without a kinematic/no-gravity one, this character would never
        // raise OnTriggerEnter/Exit for any dialogue/teleport trigger zone in a scene.
        root.AddComponent<CapsuleCollider>();
        var rigidbody = root.AddComponent<Rigidbody>();
        rigidbody.isKinematic = true;
        rigidbody.useGravity = false;
        root.AddComponent<NavMeshAgent>();
        root.AddComponent<NavMeshCharacterMotor>();
        // Task 2.5 discovery: FacingController (Task 2.1, "sole writer of Transform rotation") was never
        // actually wired onto this real prefab -- only exercised via synthetic PlayMode fixtures. Added here
        // so this fixture matches what CharacterWrapperBuilder now builds by default, and Gate A's
        // interruption/turning inspection has a real, correctly-composed character to look at.
        root.AddComponent<FacingController>();

        root.AddComponent<CharacterAgent>();
        root.AddComponent<EquipmentVisualBinder>();

        var animator = root.AddComponent<CharacterPlayableAnimator>();
        if (existingProfile != null) SetField(animator, "profile", existingProfile);
    }

    private static CharacterAnimationProfile LoadExistingAnimationProfile()
    {
        var existing = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
        if (existing == null) return null;

        var existingAnimator = existing.GetComponent<CharacterPlayableAnimator>();
        return existingAnimator == null ? null : (CharacterAnimationProfile)GetField(existingAnimator, "profile");
    }

    // CORRECTED after a real, visually-reproduced bug (Phase 2 review follow-up, 2026-09-11 -- see
    // Docs/Verification/Gate-A-Results.md's "visual defect" addendum): this used to configure a HUMANOID
    // avatar here, on the theory that it would "let this character later share animation clips with e.g.
    // Universal Animation Library[Standard]" via Humanoid retargeting. It never actually did that --
    // CharacterAnimationAuthor's clips are imported as Generic (raw bone-path curves, not Humanoid muscle
    // data), and PlayableCharacterAnimationDriver plays them via a raw AnimationClipPlayable -> Animator
    // output with NO retargeting step at all; sharing works purely because both FBX packs happen to use an
    // identical bone-path hierarchy (confirmed via a live Editor query), not because of Humanoid retargeting.
    // Worse: a HUMANOID avatar actively corrupts this specific setup. Confirmed empirically with a live
    // repro: a character bound this way collapsed into a wrong, ground-level pose within about a second of
    // Play starting -- reproducible via Unity's own continuous graph evaluation but NOT via a one-shot manual
    // Evaluate() call, and momentarily "fixed" by Animator.Rebind() only to immediately re-corrupt on the
    // next frame. Removing the Humanoid avatar (Generic animation type instead) made the same character
    // stand and idle correctly and stay stable. This is now Generic, matching what this animation system
    // actually does (path-based curve application, no muscle-space retargeting anywhere).
    private static bool ConfigureGenericRig()
    {
        var importer = AssetImporter.GetAtPath(ModelPath) as ModelImporter;
        if (importer == null)
        {
            Debug.LogError($"[BaseHumanMaleCharacterAuthor] Could not load ModelImporter at {ModelPath}.");
            return false;
        }

        importer.animationType = ModelImporterAnimationType.Generic;
        importer.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
        importer.SaveAndReimport();

        var avatar = AssetDatabase.LoadAssetAtPath<Avatar>(ModelPath);
        if (avatar == null || !avatar.isValid)
        {
            Debug.LogError(
                $"[BaseHumanMaleCharacterAuthor] Generic avatar setup failed for {ModelPath} " +
                $"(avatar={(avatar == null ? "null" : "non-null")}, isValid={avatar?.isValid}). " +
                "Open the model's Rig tab in the Inspector and check its Avatar setup manually.");
            return false;
        }

        return true;
    }

    private static void WireIntoGilroyMasters(GameObject prefab)
    {
        var gilroy = AssetDatabase.LoadAssetAtPath<NpcDefinition>(GilroyDefinitionPath);
        if (gilroy == null)
        {
            Debug.LogWarning($"[BaseHumanMaleCharacterAuthor] Could not find {GilroyDefinitionPath} to wire VisualPrefab into.");
            return;
        }

        var serialized = new SerializedObject(gilroy);
        serialized.FindProperty("visualPrefab").objectReferenceValue = prefab;
        serialized.ApplyModifiedProperties();
        EditorUtility.SetDirty(gilroy);
        AssetDatabase.SaveAssets();

        Debug.Log($"[BaseHumanMaleCharacterAuthor] Wired VisualPrefab on {GilroyDefinitionPath}.");
    }

    private static void SetField(object target, string fieldName, object value) => Field(target, fieldName).SetValue(target, value);

    private static object GetField(object target, string fieldName) => Field(target, fieldName).GetValue(target);

    private static FieldInfo Field(object target, string fieldName)
    {
        var type = target.GetType();
        while (type != null)
        {
            var field = type.GetField(fieldName, BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.DeclaredOnly);
            if (field != null) return field;
            type = type.BaseType;
        }

        throw new System.Exception($"Field '{fieldName}' not found on {target.GetType().Name} or its base types.");
    }
}
