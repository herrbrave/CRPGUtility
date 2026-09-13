using System;
using System.Collections.Generic;
using System.Reflection;
using CRPG.Core;
using CRPG.Inventory;
using CRPG.Presentation.Equipment;
using UnityEditor;
using UnityEngine;

// Dogfood-only tooling (see third_party_assets / TestProject conventions) -- not part of the shipped package.
//
// Turns the Modular Character Outfits pack's Peasant pieces into real equippable "Clothes" gear instead of a
// pre-dressed visual mesh: one EquipmentSlotDefinition per body region (matches the pack's own modular
// design), one ItemDefinition per piece, and (Task 2.4) one EquipmentVisualProfile + RigBindingMap per piece
// declaring EXACTLY which source bone maps to which target bone by relative PATH -- replacing the old
// "instantiate and rebind by leaf Transform.name, silently leave an unmatched bone pointing at the piece's
// own unused copy" behavior. The bone paths themselves are still COMPUTED here by matching leaf names between
// a real instantiated copy of the target skeleton (Superhero_Male_FullBody.fbx, the same body
// BaseHumanMaleCharacterAuthor wires up) and each piece's own real instantiated hierarchy -- name-matching is
// a fine ONE-TIME AUTHORING AID for generating an explicit, reviewable, validated asset; it is no longer how
// the runtime resolves bones every time a piece is equipped (EquipmentVisualBinder does that by path alone).
public static class PeasantOutfitEquipmentAuthor
{
    private const string SlotContentFolder = "Packages/com.crpgtoolkit.core/Rules/SRD5e2014/Content";
    private const string ItemFolder = "Assets/Resources/Items";
    private const string VisualFolder = "Assets/Resources/Equipment";

    private const string ModularPartsFolder =
        "Assets/ThirdPartyAssets/Modular Character Outfits - Fantasy[Standard]/Modular Character Outfits - Fantasy[Standard]/Exports/FBX (Unity)/Modular Parts";

    // Same target skeleton BaseHumanMaleCharacterAuthor wires onto BaseHumanMaleCharacter.prefab's Model --
    // duplicated as a constant here rather than referencing that class's private one, matching how each
    // authoring script in this project owns its own path constants.
    private const string TargetSkeletonPath =
        "Assets/ThirdPartyAssets/Universal Base Characters[Standard]/Universal Base Characters[Standard]/Base Characters/Unity/Superhero_Male_FullBody.fbx";

    // A computed RigBindingMap's target paths must match exactly how EquipmentVisualBinder resolves them at
    // runtime: relative to CharacterRoot, which nests the skeleton at VisualRoot/Model (Docs/Design/
    // Production-Foundation.md sec.4.1's wrapper convention) -- the FBX's own internal node structure survives
    // PrefabUtility.InstantiatePrefab unchanged, so walking a bare instantiated copy of the FBX and prefixing
    // every resulting path with this string produces exactly the same paths as the real, wrapped prefab.
    private const string TargetPathPrefix = "VisualRoot/Model/";

    private static readonly (string SlotId, string SlotName, string PartFile, string ItemId, string ItemName, string RegionId)[] Pieces =
    {
        ("slot-clothing-arms", "Clothing - Arms", "Male_Peasant_Arms.fbx", "item-peasant-arms", "Peasant Sleeves", "region-arms"),
        ("slot-clothing-body", "Clothing - Body", "Male_Peasant_Body.fbx", "item-peasant-body", "Peasant Tunic", "region-body"),
        ("slot-clothing-legs", "Clothing - Legs", "Male_Peasant_Legs.fbx", "item-peasant-legs", "Peasant Trousers", "region-legs"),
        ("slot-clothing-feet", "Clothing - Feet", "Male_Peasant_Feet.fbx", "item-peasant-feet", "Peasant Boots", "region-feet"),
    };

    [MenuItem("CRPG Toolkit/Characters/Author Peasant Outfit Equipment")]
    public static void Author()
    {
        EnsureFolder(SlotContentFolder);
        EnsureFolder(ItemFolder);
        EnsureFolder(VisualFolder);

        var targetSkeletonPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(TargetSkeletonPath);
        if (targetSkeletonPrefab == null)
        {
            Debug.LogError($"[PeasantOutfitEquipmentAuthor] Could not load target skeleton at {TargetSkeletonPath} -- aborting.");
            return;
        }

        var authored = 0;
        foreach (var piece in Pieces)
        {
            var slot = CreateOrReplaceAsset<EquipmentSlotDefinition>(SlotContentFolder, piece.SlotId, piece.SlotName);
            SetField(slot, "displayName", piece.SlotName);
            EditorUtility.SetDirty(slot);

            var visualPath = $"{ModularPartsFolder}/{piece.PartFile}";
            var visualPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(visualPath);
            if (visualPrefab == null)
            {
                Debug.LogError($"[PeasantOutfitEquipmentAuthor] Could not load {visualPath} -- skipping {piece.ItemId}.");
                continue;
            }

            if (!TryComputeBoneMap(targetSkeletonPrefab, visualPrefab, out var bonePaths, out var rootBonePath, out var mapError))
            {
                Debug.LogError($"[PeasantOutfitEquipmentAuthor] {piece.PartFile}: {mapError} -- skipping {piece.ItemId}.");
                continue;
            }

            var map = CreateOrReplaceAsset<RigBindingMap>(VisualFolder, $"rigmap-{piece.SlotId}", $"{piece.ItemName} Bone Map");
            SetField(map, "rootBonePath", rootBonePath);
            SetField(map, "bonePaths", bonePaths);
            EditorUtility.SetDirty(map);

            var profile = CreateOrReplaceAsset<EquipmentVisualProfile>(VisualFolder, $"visual-{piece.SlotId}", $"{piece.ItemName} Visual");
            SetField(profile, "kind", EquipmentVisualKind.Skinned);
            SetField(profile, "visualPrefab", visualPrefab);
            SetField(profile, "bodyRegionId", new DefinitionId(piece.RegionId));
            SetField(profile, "hiddenBodyRegions", new List<DefinitionId>());
            SetField(profile, "boneMap", map);
            EditorUtility.SetDirty(profile);

            var item = CreateOrReplaceAsset<ItemDefinition>(ItemFolder, piece.ItemId, piece.ItemName);
            SetField(item, "displayName", piece.ItemName);
            SetField(item, "weight", 0.5f);
            SetField(item, "maxStackSize", 1);
            SetField(item, "equipmentSlotId", slot.Id);
            SetField(item, "equipmentVisualProfileId", profile.Id);
            EditorUtility.SetDirty(item);

            authored++;
        }

        AssetDatabase.SaveAssets();
        Debug.Log($"[PeasantOutfitEquipmentAuthor] Authored {authored}/{Pieces.Length} clothing slots + items + equipment visual profiles.");
    }

    /// <summary>Computes an explicit source-&gt;target bone PATH list by matching leaf bone names between a
    /// throwaway instantiated copy of the target skeleton and the source piece -- a one-time authoring-time
    /// convenience (see this file's header comment), never used at runtime. Bones present on the source but
    /// absent on the target (or vice versa) are simply not included -- not every accessory bone needs a
    /// mapping, only the ones the piece's own SkinnedMeshRenderers actually reference.</summary>
    private static bool TryComputeBoneMap(GameObject targetSkeletonPrefab, GameObject sourcePiecePrefab,
        out List<RigBindingMap.BonePathEntry> bonePaths, out string rootBonePath, out string error)
    {
        bonePaths = new List<RigBindingMap.BonePathEntry>();
        rootBonePath = null;

        var targetInstance = (GameObject)PrefabUtility.InstantiatePrefab(targetSkeletonPrefab);
        var sourceInstance = (GameObject)PrefabUtility.InstantiatePrefab(sourcePiecePrefab);
        try
        {
            var targetPathsByName = BuildNameToPathMap(targetInstance.transform);
            var sourcePathsByName = BuildNameToPathMap(sourceInstance.transform);

            var renderers = sourceInstance.GetComponentsInChildren<SkinnedMeshRenderer>(true);
            if (renderers.Length == 0)
            {
                error = "No SkinnedMeshRenderer found on the source piece.";
                return false;
            }

            var seenSourceNames = new HashSet<string>();
            string sourceRootBoneName = null;

            foreach (var renderer in renderers)
            {
                if (renderer.rootBone != null) sourceRootBoneName ??= renderer.rootBone.name;

                foreach (var bone in renderer.bones)
                {
                    if (bone == null || !seenSourceNames.Add(bone.name)) continue;
                    if (!sourcePathsByName.TryGetValue(bone.name, out var sourcePath)) continue;
                    if (!targetPathsByName.TryGetValue(bone.name, out var targetPath)) continue;

                    var entry = new RigBindingMap.BonePathEntry();
                    SetField(entry, "sourcePath", sourcePath);
                    SetField(entry, "targetPath", TargetPathPrefix + targetPath);
                    bonePaths.Add(entry);
                }
            }

            if (bonePaths.Count == 0)
            {
                error = "No matching bone names found between the source piece and the target skeleton.";
                return false;
            }

            if (sourceRootBoneName != null && sourcePathsByName.TryGetValue(sourceRootBoneName, out var rootPath))
                rootBonePath = rootPath;

            error = null;
            return true;
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(targetInstance);
            UnityEngine.Object.DestroyImmediate(sourceInstance);
        }
    }

    private static Dictionary<string, string> BuildNameToPathMap(Transform root)
    {
        var map = new Dictionary<string, string>();
        Walk(root, string.Empty, map);
        return map;
    }

    private static void Walk(Transform current, string path, Dictionary<string, string> map)
    {
        if (path.Length > 0 && !map.ContainsKey(current.name)) map[current.name] = path;
        for (var i = 0; i < current.childCount; i++)
        {
            var child = current.GetChild(i);
            Walk(child, path.Length == 0 ? child.name : path + "/" + child.name, map);
        }
    }

    private static T CreateOrReplaceAsset<T>(string folder, string id, string assetName) where T : DefinitionAsset
    {
        var safeName = assetName.Replace(" ", "").Replace(":", "").Replace("'", "").Replace("-", "");
        var path = $"{folder}/{typeof(T).Name}_{safeName}.asset";
        if (AssetDatabase.LoadAssetAtPath<T>(path) != null) AssetDatabase.DeleteAsset(path);

        var instance = ScriptableObject.CreateInstance<T>();
        SetField(instance, "id", id);
        AssetDatabase.CreateAsset(instance, path);
        return AssetDatabase.LoadAssetAtPath<T>(path);
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

        throw new Exception($"Field '{fieldName}' not found on {target.GetType().Name} or its base types.");
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
