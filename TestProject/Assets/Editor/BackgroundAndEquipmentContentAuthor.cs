using System;
using System.Collections.Generic;
using System.Reflection;
using CRPG.Actors;
using CRPG.Core;
using CRPG.Inventory;
using CRPG.Rules;
using UnityEditor;
using UnityEngine;

// Dogfood-only tooling (see third_party_assets / TestProject conventions) -- not part of the shipped package.
//
// One-time content author for three more SRD5 backgrounds (Acolyte, Criminal, Sage) and a basic equipment
// set (weapons/armor/adventuring gear) beyond the Longsword/Chain Shirt slice FighterSampleContentAuthor
// already covers. Authored the same reflection-driven way as that script -- there's no interactive Inspector
// session doing the clicking, so this does it once instead.
//
// Sourcing note: verified against the official SRD5.1 PDF (media.wizards.com/2016/downloads/DND/SRD-OGL_V5.1.pdf)
// and cross-checked against api.open5e.com rather than relying on recalled text. One finding worth recording:
// the SRD5.1 document itself only fully details ONE sample background -- Acolyte (page 60-61 of that PDF).
// Criminal and Sage are PHB-only backgrounds, not present in the OGL SRD text at all. Since the task calls for
// them anyway (and gives their exact skill proficiencies, which do match the published PHB versions), they're
// authored here with real, commonly-published mechanics (skills, equipment, cost/weight) but the two features'
// descriptions ("Criminal Contact", "Researcher") are written in this script's own words rather than quoting
// PHB text verbatim, since that text is not OGL/SRD open content the way Acolyte's "Shelter of the Faithful"
// is. Weapon/armor/gear numbers (cost, weight, damage dice, AC) all come directly off the SRD5.1 Weapons/Armor/
// Adventuring Gear/Equipment Packs tables, which *are* open content.
//
// Existing shared content (stats, skill-insight/skill-history, slot-mainhand/slot-armor, item-longsword, ...)
// is referenced by bare DefinitionId string rather than re-authored -- ItemDefinition.equipmentSlotId and
// FeatureDefinition.grantedProficiencies are just DefinitionId values, never direct object references, so no
// live reference to another script's asset is needed to point at it. Skills newly introduced by sibling
// authoring scripts in parallel (Arcana, Religion, Deception, Stealth) ARE recreated here with their exact
// specified id/displayName/governingStat, since CreateAsset<T> deletes-then-recreates by path and duplicate
// creation across independent one-time authoring scripts is harmless as long as the definition matches.
public static class BackgroundAndEquipmentContentAuthor
{
    private const string ContentFolder = "Packages/com.crpgtoolkit.core/Rules/SRD5e2014/Content";

    [MenuItem("CRPG Toolkit/Content/Author Background and Equipment Content (SRD5)")]
    public static void Author()
    {
        EnsureFolder(ContentFolder);
        var all = new List<DefinitionAsset>();

        // Stats needed to govern the skills below. Existing shared ids (stat-strength/-constitution) aren't
        // touched at all -- nothing authored here needs them.
        var intl = CreateStat("stat-intelligence", "Intelligence", "INT", all);
        var wis = CreateStat("stat-wisdom", "Wisdom", "WIS", all);
        var cha = CreateStat("stat-charisma", "Charisma", "CHA", all);
        var dex = CreateStat("stat-dexterity", "Dexterity", "DEX", all);

        // skill-insight and skill-history already exist from FighterSampleContentAuthor's Soldier slice;
        // skill-arcana/-religion/-deception/-stealth are the exact ids/governing stats sibling agents'
        // Wizard/Cleric/Rogue scripts introduced. All six are (re)created here so this script is
        // self-contained regardless of authoring order.
        var insight = CreateSkill("skill-insight", "Insight", wis, all);
        var religion = CreateSkill("skill-religion", "Religion", intl, all);
        var deception = CreateSkill("skill-deception", "Deception", cha, all);
        var stealth = CreateSkill("skill-stealth", "Stealth", dex, all);
        var arcana = CreateSkill("skill-arcana", "Arcana", intl, all);
        var history = CreateSkill("skill-history", "History", intl, all);

        // Existing equipment slots (slot-mainhand, slot-armor) are referenced by raw id below -- an
        // ItemDefinition's equipmentSlotId is a DefinitionId value, not an object reference, so no live
        // reference to the asset FighterSampleContentAuthor created is required. Only the new Off Hand slot
        // (needed for the Shield, which has no home in the current two-slot setup) is actually authored here.
        var mainHandSlotId = new DefinitionId("slot-mainhand");
        var armorSlotId = new DefinitionId("slot-armor");
        var offHandSlot = CreateEquipmentSlot("slot-offhand", "Off Hand", all);

        // ---- Weapons (SRD5.1 Weapons table; damage dice noted in description for a future weapon-damage
        // system to consume -- nothing derives damage from equipped weapons automatically yet) ----
        CreateItem("item-dagger", "Dagger", mainHandSlotId, all, baseValue: 2f, weight: 1f,
            description: "1d4 piercing damage. Finesse, light, thrown (range 20/60).");
        CreateItem("item-shortsword", "Shortsword", mainHandSlotId, all, baseValue: 10f, weight: 2f,
            description: "1d6 piercing damage. Finesse, light.");
        CreateItem("item-rapier", "Rapier", mainHandSlotId, all, baseValue: 25f, weight: 2f,
            description: "1d8 piercing damage. Finesse.");
        CreateItem("item-mace", "Mace", mainHandSlotId, all, baseValue: 5f, weight: 4f,
            description: "1d6 bludgeoning damage.");
        CreateItem("item-warhammer", "Warhammer", mainHandSlotId, all, baseValue: 15f, weight: 2f,
            description: "1d8 bludgeoning damage (1d10 if wielded two-handed). Versatile.");
        CreateItem("item-handaxe", "Handaxe", mainHandSlotId, all, baseValue: 5f, weight: 2f,
            description: "1d6 slashing damage. Light, thrown (range 20/60).");
        CreateItem("item-shortbow", "Shortbow", mainHandSlotId, all, baseValue: 25f, weight: 2f,
            description: "1d6 piercing damage. Ammunition (range 80/320), two-handed.");
        CreateItem("item-longbow", "Longbow", mainHandSlotId, all, baseValue: 50f, weight: 2f,
            description: "1d8 piercing damage. Ammunition (range 150/600), heavy, two-handed.");

        // ---- Armor ----
        // The derived-stat AC formula (base + Dex modifier, capped for medium armor) isn't built yet -- same
        // known gap noted in FighterSampleContentAuthor. Approximation used here: assume a typical +2 Dexterity
        // modifier (light armor's Dex cap never binds, so this is the same assumption for both) and fold the
        // whole "base + Dex" formula into one flat ArmorClass Add value over the unarmored baseline of 10.
        //   Leather:         11 + Dex(+2 assumed) = 13  -> flat Add 3
        //   Studded Leather:  12 + Dex(+2 assumed) = 14  -> flat Add 4
        // This is a coarser approximation than a real Dex-aware formula (a Dex-focused character undershoots,
        // a Dex-dump character overshoots), but it's directionally correct and easy to replace once the real
        // derived-stat rule engine exists.
        var leatherArmorAc = CreateFeature("feature-leather-armor-ac", "Leather Armor (AC)", all);
        SetField(leatherArmorAc, "modifiers", new List<FeatureModifier> { Modifier("ArmorClass", RuleModifierOperation.Add, 3f) });
        CreateItem("item-leather-armor", "Leather Armor", armorSlotId, all, baseValue: 10f, weight: 10f,
            description: "Light armor. AC 11 + Dex modifier (approximated here as a flat +3, assuming a typical +2 Dex modifier).",
            equippedFeatureId: leatherArmorAc.Id);

        var studdedLeatherAc = CreateFeature("feature-studded-leather-ac", "Studded Leather (AC)", all);
        SetField(studdedLeatherAc, "modifiers", new List<FeatureModifier> { Modifier("ArmorClass", RuleModifierOperation.Add, 4f) });
        CreateItem("item-studded-leather", "Studded Leather", armorSlotId, all, baseValue: 45f, weight: 13f,
            description: "Light armor. AC 12 + Dex modifier (approximated here as a flat +4, assuming a typical +2 Dex modifier).",
            equippedFeatureId: studdedLeatherAc.Id);

        // Shield's +2 AC is genuinely a flat bonus in the real rules (not an approximation like the above).
        var shieldAc = CreateFeature("feature-shield-item-ac", "Shield (AC)", all);
        SetField(shieldAc, "modifiers", new List<FeatureModifier> { Modifier("ArmorClass", RuleModifierOperation.Add, 2f) });
        CreateItem("item-shield", "Shield", new DefinitionId("slot-offhand"), all, baseValue: 10f, weight: 6f,
            description: "Wielded in one hand. Grants +2 AC.", equippedFeatureId: shieldAc.Id);

        // ---- Adventuring gear ----
        var holySymbol = CreateItem("item-holy-symbol", "Holy Symbol", DefinitionId.None, all, baseValue: 5f, weight: 1f,
            description: "An amulet, emblem, or reliquary bearing a deity's or pantheon's symbol. Required to focus divine spellcasting.");
        var thievesTools = CreateItem("item-thieves-tools", "Thieves' Tools", DefinitionId.None, all, baseValue: 25f, weight: 1f,
            description: "A set of lockpicks and small tools used to disarm traps and pick locks.");
        var inkAndQuill = CreateItem("item-ink-and-quill", "Ink and Quill", DefinitionId.None, all, baseValue: 10f, weight: 0f,
            description: "A bottle of ink and a quill pen, bundled as one item for record-keeping and correspondence.");
        var componentPouch = CreateItem("item-component-pouch", "Component Pouch", DefinitionId.None, all, baseValue: 25f, weight: 2f,
            description: "A small watertight leather belt pouch stocked with the material components needed to cast most spells that don't consume their components.");
        // Explorer's Pack is abstracted as a single stackable-1 bundle rather than itemizing every component --
        // the real SRD5.1 pack (10 gp) contains a backpack, a bedroll, a mess kit, a tinderbox, 10 torches,
        // 10 days of rations, and a waterskin, with 50 feet of hempen rope strapped to the outside; weight
        // below (59 lb) is the sum of those real component weights.
        var explorersPack = CreateItem("item-explorers-pack", "Explorer's Pack", DefinitionId.None, all, baseValue: 10f, weight: 59f,
            description: "Bundles a backpack, a bedroll, a mess kit, a tinderbox, 10 torches, 10 days of rations, a waterskin, and 50 feet of hempen rope (abstracted as one item rather than itemizing every component).");

        // ---- Backgrounds ----
        // Acolyte: the one background the SRD5.1 document itself fully details (Insight + Religion,
        // holy symbol, "Shelter of the Faithful" as a flavor-only roleplay/DM-adjudicated benefit -- no
        // mechanical modifier exists for "free healing at a temple").
        var acolyteFeature = CreateFeature("feature-acolyte-background", "Shelter of the Faithful", all);
        SetField(acolyteFeature, "description",
            "As an acolyte, you command the respect of those who share your faith and can perform the religious " +
            "ceremonies of your deity. You and your adventuring companions can expect to receive free healing " +
            "and care at a temple, shrine, or other established presence of your faith, though you must provide " +
            "any material components needed for spells. Those who share your religion will support you (but " +
            "only you) at a modest lifestyle. You might also have ties to a specific temple dedicated to your " +
            "chosen deity or pantheon, and can call upon its priests for aid, provided the request isn't " +
            "hazardous and you remain in good standing with them.");
        SetField(acolyteFeature, "grantedProficiencies", new List<DefinitionId> { insight.Id, religion.Id });
        var acolyte = CreateAsset<BackgroundDefinition>("background-acolyte", "Acolyte", all);
        SetField(acolyte, "displayName", "Acolyte");
        SetField(acolyte, "description", "You have spent your life in the service of a temple to a specific god or pantheon of gods, acting as an intermediary between the mortal world and the divine.");
        SetField(acolyte, "grantedFeatureIds", new List<DefinitionId> { acolyteFeature.Id });

        // Criminal: not actually present in the OGL SRD5.1 text (PHB-only background), authored here anyway
        // per spec with its real published skills/equipment; "Criminal Contact" description below is this
        // script's own paraphrase, not quoted PHB text.
        var criminalFeature = CreateFeature("feature-criminal-background", "Criminal Contact", all);
        SetField(criminalFeature, "description",
            "You have a reliable contact who serves as your liaison to a network of other criminals. You know " +
            "how to get messages to and from your contact, even over great distances, using the local " +
            "messengers, corrupt caravan masters, and seedy sailors who move information for people like you.");
        SetField(criminalFeature, "grantedProficiencies", new List<DefinitionId> { deception.Id, stealth.Id });
        var criminal = CreateAsset<BackgroundDefinition>("background-criminal", "Criminal", all);
        SetField(criminal, "displayName", "Criminal");
        SetField(criminal, "description", "You have a history of breaking the law and survived by leveraging your criminal contacts, hidden away from the law-abiding folk you rub shoulders with.");
        SetField(criminal, "grantedFeatureIds", new List<DefinitionId> { criminalFeature.Id });

        // Sage: also PHB-only, not in the OGL SRD5.1 text; "Researcher" description is this script's own
        // paraphrase.
        var sageFeature = CreateFeature("feature-sage-background", "Researcher", all);
        SetField(sageFeature, "description",
            "When you attempt to learn or recall a piece of lore you don't personally know, you often know " +
            "where and from whom you can obtain it -- typically a library, scriptorium, university, or a sage " +
            "or other learned person or creature. Your GM will let you know if the lore is simply beyond your " +
            "reach.");
        SetField(sageFeature, "grantedProficiencies", new List<DefinitionId> { arcana.Id, history.Id });
        var sage = CreateAsset<BackgroundDefinition>("background-sage", "Sage", all);
        SetField(sage, "displayName", "Sage");
        SetField(sage, "description", "You spent years learning the lore of the multiverse, studying at a university, monastery, or other institution of learning.");
        SetField(sage, "grantedFeatureIds", new List<DefinitionId> { sageFeature.Id });

        // No AssetDatabase.Refresh()/SaveAssets() mid-script: either can trigger a reimport that invalidates
        // the in-memory references held above, silently leaving later field writes dropped. Everything is
        // saved once, at the very end.

        foreach (var definition in all) EditorUtility.SetDirty(definition);
        AssetDatabase.SaveAssets();

        Debug.Log($"[BackgroundAndEquipmentContentAuthor] Authored {all.Count} SRD5 definitions under {ContentFolder} " +
                  $"(backgrounds: Acolyte, Criminal, Sage; new equipment slot: Off Hand; " +
                  $"holy symbol/thieves' tools/ink and quill/component pouch/explorer's pack; " +
                  $"weapons: Dagger, Shortsword, Rapier, Mace, Warhammer, Handaxe, Shortbow, Longbow; " +
                  $"armor: Leather Armor, Studded Leather, Shield).");
    }

    private static StatDefinition CreateStat(string id, string displayName, string abbreviation, List<DefinitionAsset> all)
    {
        var stat = CreateAsset<StatDefinition>(id, displayName, all);
        SetField(stat, "displayName", displayName);
        SetField(stat, "abbreviation", abbreviation);
        return stat;
    }

    private static SkillDefinition CreateSkill(string id, string displayName, StatDefinition governingStat, List<DefinitionAsset> all)
    {
        var skill = CreateAsset<SkillDefinition>(id, displayName, all);
        SetField(skill, "displayName", displayName);
        SetField(skill, "governingStat", governingStat);
        return skill;
    }

    private static EquipmentSlotDefinition CreateEquipmentSlot(string id, string displayName, List<DefinitionAsset> all)
    {
        var slot = CreateAsset<EquipmentSlotDefinition>(id, displayName, all);
        SetField(slot, "displayName", displayName);
        return slot;
    }

    private static ItemDefinition CreateItem(
        string id, string displayName, DefinitionId equipmentSlotId, List<DefinitionAsset> all,
        float baseValue = 0f, int maxStackSize = 1, float weight = 0f, string description = null,
        DefinitionId equippedFeatureId = default)
    {
        var item = CreateAsset<ItemDefinition>(id, displayName, all);
        SetField(item, "displayName", displayName);
        SetField(item, "equipmentSlotId", equipmentSlotId);
        SetField(item, "baseValue", baseValue);
        SetField(item, "maxStackSize", maxStackSize);
        SetField(item, "weight", weight);
        if (!string.IsNullOrEmpty(description)) SetField(item, "description", description);
        if (equippedFeatureId.IsValid) SetField(item, "equippedFeatureId", equippedFeatureId);
        return item;
    }

    private static FeatureDefinition CreateFeature(string id, string displayName, List<DefinitionAsset> all)
    {
        var feature = CreateAsset<FeatureDefinition>(id, displayName, all);
        SetField(feature, "displayName", displayName);
        return feature;
    }

    private static T CreateAsset<T>(string id, string assetName, List<DefinitionAsset> all) where T : DefinitionAsset
    {
        var instance = ScriptableObject.CreateInstance<T>();
        SetField(instance, "id", id);
        var safeName = assetName.Replace(" ", "").Replace(":", "").Replace("'", "");
        var path = $"{ContentFolder}/{typeof(T).Name}_{safeName}.asset";
        if (AssetDatabase.LoadAssetAtPath<T>(path) != null) AssetDatabase.DeleteAsset(path);
        AssetDatabase.CreateAsset(instance, path);
        all.Add(instance);
        return instance;
    }

    private static FeatureModifier Modifier(string queryKey, RuleModifierOperation operation, float value)
    {
        object boxed = new FeatureModifier();
        SetField(boxed, "queryKey", queryKey);
        SetField(boxed, "operation", operation);
        SetField(boxed, "value", value);
        SetField(boxed, "priority", 0);
        return (FeatureModifier)boxed;
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
