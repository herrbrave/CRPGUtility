using System.IO;
using System.Reflection;
using CRPG.Actors;
using CRPG.Gameplay;
using CRPG.Inventory;
using CRPG.Navigation;
using CRPG.Presentation;
using UnityEditor;
using UnityEngine;
using UnityEngine.AI;

// Dogfood-only tooling (see third_party_assets / TestProject conventions) -- not part of the shipped package.
//
// Builds/updates the CharacterRoot/VisualRoot/Model wrapper (Docs/Design/Production-Foundation.md sec.4.1)
// around the Universal Base Characters pack's Superhero_Male_FullBody.fbx -- not a pre-dressed outfit, since
// clothing is equipment (see PeasantOutfitEquipmentAuthor): CharacterVisualEquipment instantiates and
// bone-rebinds each equipped piece onto this body's own skeleton at runtime, verified to share the exact same
// 65-bone rig across both packs via a live Editor query rather than assumed from file names. Reconfigures the
// FBX from its default Generic rig to a Humanoid Avatar (so it can later share animation clips with e.g.
// Universal Animation Library[Standard]).
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
        if (!ConfigureHumanoidRig()) return;

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

        root.AddComponent<CharacterAgent>();
        root.AddComponent<CharacterVisualEquipment>();

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

    // Superhero_Male_FullBody.fbx ships with animationType: Generic (no bone-to-Humanoid mapping at all) --
    // switching to Human + CreateFromThisModel asks Unity to auto-map bones by name. Verify it actually
    // succeeded rather than trusting it silently: an unmapped skeleton still produces an Avatar asset, just
    // one with isHuman == false, which would make every future animation-retargeting plan silently
    // impossible without ever throwing an error.
    private static bool ConfigureHumanoidRig()
    {
        var importer = AssetImporter.GetAtPath(ModelPath) as ModelImporter;
        if (importer == null)
        {
            Debug.LogError($"[BaseHumanMaleCharacterAuthor] Could not load ModelImporter at {ModelPath}.");
            return false;
        }

        importer.animationType = ModelImporterAnimationType.Human;
        importer.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
        importer.SaveAndReimport();

        var avatar = AssetDatabase.LoadAssetAtPath<Avatar>(ModelPath);
        if (avatar == null || !avatar.isValid || !avatar.isHuman)
        {
            Debug.LogError(
                $"[BaseHumanMaleCharacterAuthor] Humanoid avatar mapping failed for {ModelPath} " +
                $"(avatar={(avatar == null ? "null" : "non-null")}, isValid={avatar?.isValid}, isHuman={avatar?.isHuman}). " +
                "This FBX's skeleton likely doesn't auto-map to Unity's Humanoid bone set -- open the " +
                "model's Rig tab in the Inspector and configure the Avatar Mapper manually.");
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
