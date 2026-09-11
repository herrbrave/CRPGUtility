using System;
using System.Reflection;
using CRPG.Core;
using CRPG.Inventory;
using UnityEditor;
using UnityEngine;

// Dogfood-only tooling (see third_party_assets / TestProject conventions) -- not part of the shipped package.
//
// Turns the Modular Character Outfits pack's Peasant pieces into real equippable "Clothes" gear instead of a
// pre-dressed visual mesh: one EquipmentSlotDefinition per body region (matches the pack's own modular
// design -- see BaseHumanMaleCharacterAuthor, which now uses the bare base body instead of this outfit),
// authored as reusable engine-agnostic content next to the existing MainHand/OffHand/Armor slots since a
// region slot isn't 5e-specific either. One ItemDefinition per piece, authored here in TestProject rather
// than inside crpg-toolkit/ because its VisualPrefab points at a third-party asset that must never ship with
// the toolkit package (project_crpg_third_party_assets convention) -- unlike the slot definitions, which
// carry no such reference and are safe as real package content.
public static class PeasantOutfitEquipmentAuthor
{
    private const string SlotContentFolder = "Packages/com.crpgtoolkit.core/Rules/SRD5e2014/Content";
    private const string ItemFolder = "Assets/Resources/Items";

    private const string ModularPartsFolder =
        "Assets/ThirdPartyAssets/Modular Character Outfits - Fantasy[Standard]/Modular Character Outfits - Fantasy[Standard]/Exports/FBX (Unity)/Modular Parts";

    private static readonly (string SlotId, string SlotName, string PartFile, string ItemId, string ItemName)[] Pieces =
    {
        ("slot-clothing-arms", "Clothing - Arms", "Male_Peasant_Arms.fbx", "item-peasant-arms", "Peasant Sleeves"),
        ("slot-clothing-body", "Clothing - Body", "Male_Peasant_Body.fbx", "item-peasant-body", "Peasant Tunic"),
        ("slot-clothing-legs", "Clothing - Legs", "Male_Peasant_Legs.fbx", "item-peasant-legs", "Peasant Trousers"),
        ("slot-clothing-feet", "Clothing - Feet", "Male_Peasant_Feet.fbx", "item-peasant-feet", "Peasant Boots"),
    };

    [MenuItem("CRPG Toolkit/Characters/Author Peasant Outfit Equipment")]
    public static void Author()
    {
        EnsureFolder(SlotContentFolder);
        EnsureFolder(ItemFolder);

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

            var item = CreateOrReplaceAsset<ItemDefinition>(ItemFolder, piece.ItemId, piece.ItemName);
            SetField(item, "displayName", piece.ItemName);
            SetField(item, "weight", 0.5f);
            SetField(item, "maxStackSize", 1);
            SetField(item, "equipmentSlotId", slot.Id);
            SetField(item, "visualPrefab", visualPrefab);
            EditorUtility.SetDirty(item);
        }

        AssetDatabase.SaveAssets();
        Debug.Log("[PeasantOutfitEquipmentAuthor] Authored 4 clothing slots + 4 Peasant outfit items.");
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
