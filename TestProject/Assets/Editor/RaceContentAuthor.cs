using System;
using System.Collections.Generic;
using System.Reflection;
using CRPG.Actors;
using CRPG.Core;
using CRPG.Rules;
using UnityEditor;
using UnityEngine;

// Dogfood-only tooling (see third_party_assets / TestProject conventions) -- not part of the shipped package.
//
// One-time content author for the three OGL-safe SRD5.1 races the original project spec scoped: Hill Dwarf,
// High Elf, and Lightfoot Halfling. Built the same way FighterSampleContentAuthor.cs authors Human/Fighter --
// real .asset files under the package's Rules/SRD5e2014/Content/, driven through DefinitionAsset fields via
// reflection since there's no interactive Inspector session doing the clicking.
//
// Each race is authored as exactly the base race + its one named subrace with no SelectFeature "choose a
// subrace" ChoiceRequirement: that machinery exists for when a race offers multiple subraces to pick between,
// and with only one subrace authored per race this pass (no Mountain Dwarf, no Wood Elf, ...) a single-option
// choice would just be a meaningless click. Instead each subrace's FeatureDefinition id is granted directly
// alongside the base race's traits in RaceDefinition.grantedFeatureIds.
//
// Deliberately NOT mechanically modeled here (same "author ahead of the consuming system" spirit as Fighter's
// Improved Critical, described as real SRD rules text in each feature's description instead):
//   - Any "advantage on saves against a specific condition" (Fey Ancestry vs. charmed, Brave vs. frightened):
//     the existing SavingThrow:<abilityStatId> advantage hook is keyed by which ability score is being rolled,
//     not by what caused the save, so there's no query key for "advantage specifically against being charmed".
//   - Anything needing a Speed/movement stat (Dwarf's 25ft speed -- there's no Speed stat on CharacterState
//     yet) or dice-reroll mechanics (Halfling Lucky's "reroll 1s on the die").
//   - Darkvision, Dwarven Resilience (poison resistance), Stonecunning, Tool Proficiency (a choice of artisan's
//     tools, but no tool item/proficiency definitions exist yet to choose among), Trance, Dwarven Toughness
//     (+1 HP/level -- no per-level HP modifier hook exists), Halfling Nimbleness and Naturally Stealthy (no
//     grid/space model, no stealth-vs-passive-perception check) -- all authored as real description text only.
//   - High Elf's Extra Language and base Elf's implied language grants -- no language content/definition type
//     exists in this codebase at all yet, so this is flavor text only (unlike the cantrip below, there's no
//     existing choice-of-language precedent to hook into).
// Weapon-training proficiencies (Dwarven Combat Training: battleaxe/handaxe/light hammer/warhammer; Elf Weapon
// Training: longsword/shortsword/shortbow/longbow) are authored via grantedProficiencies using each weapon
// item's own DefinitionId, exactly like the generic proficiency convention Human/Fighter content already
// uses -- but only for items that already exist in this content folder. Today that's just item-longsword (from
// FighterSampleContentAuthor), so High Elf's Elf Weapon Training grants that one proficiency for real and notes
// the rest in its description text pending shortsword/shortbow/longbow items; Dwarven Combat Training's four
// weapons don't exist yet at all, so it stays flavor-only. Both proficiency grants are inert today regardless
// (nothing consumes weapon-proficiency for attack-bonus gating yet, same confirmed-dead-data situation Fighter's
// Improved Critical was in).
//
// High Elf's bonus Wizard cantrip is authored as a SelectSpell ChoiceRequirement with an empty Options list and
// FilterTag "wizard-cantrip" -- the documented "resolve candidates dynamically once tagging exists" pattern
// (see ChoiceRequirement's own doc comment), rather than hardcoding a dependency on whichever cantrip
// DefinitionIds a sibling agent authoring the Wizard class happens to pick. Nothing currently reads FilterTag
// at runtime, so this is authored intent only, not yet automatically populated.
public static class RaceContentAuthor
{
    private const string ContentFolder = "Packages/com.crpgtoolkit.core/Rules/SRD5e2014/Content";

    [MenuItem("CRPG Toolkit/Content/Author Race Content (SRD5)")]
    public static void Author()
    {
        EnsureFolder(ContentFolder);
        var all = new List<DefinitionAsset>();

        // Existing shared content reused by id -- not recreated. Stat/skill query keys and proficiency ids are
        // just the same stable strings Human/Fighter content already established (e.g. "stat-constitution",
        // "skill-perception"), and item-longsword already exists on disk from FighterSampleContentAuthor.
        const string statConstitution = "stat-constitution";
        const string statDexterity = "stat-dexterity";
        const string statIntelligence = "stat-intelligence";
        const string statWisdom = "stat-wisdom";
        const string statCharisma = "stat-charisma";
        var skillPerception = new DefinitionId("skill-perception");
        var itemLongsword = new DefinitionId("item-longsword");

        // --- Dwarf / Hill Dwarf -------------------------------------------------------------------------

        var dwarfTraits = CreateFeature("feature-dwarf-traits", "Dwarf Traits", all);
        SetField(dwarfTraits, "description",
            "Ability Score Increase: Your Constitution score increases by 2.\n\n" +
            "Darkvision: Accustomed to life underground, you have superior vision in dark and dim conditions. " +
            "You can see in dim light within 60 feet of you as if it were bright light, and in darkness as if " +
            "it were dim light. You can't discern color in darkness, only shades of gray.\n\n" +
            "Dwarven Resilience: You have advantage on saving throws against poison, and you have resistance " +
            "against poison damage.\n\n" +
            "Dwarven Combat Training: You have proficiency with the battleaxe, handaxe, light hammer, and " +
            "warhammer.\n\n" +
            "Tool Proficiency: You gain proficiency with the artisan's tools of your choice: smith's tools, " +
            "brewer's supplies, or mason's tools.\n\n" +
            "Stonecunning: Whenever you make an Intelligence (History) check related to the origin of " +
            "stonework, you are considered proficient in the History skill and add double your proficiency " +
            "bonus to the check, instead of your normal proficiency bonus.");
        SetField(dwarfTraits, "modifiers", new List<FeatureModifier>
        {
            Modifier(statConstitution, RuleModifierOperation.Add, 2f),
            Modifier("Speed", RuleModifierOperation.Add, 25f),
        });

        var hillDwarfTraits = CreateFeature("feature-hill-dwarf-traits", "Hill Dwarf Traits", all);
        SetField(hillDwarfTraits, "description",
            "Ability Score Increase: Your Wisdom score increases by 1.\n\n" +
            "Dwarven Toughness: Your hit point maximum increases by 1, and it increases by 1 every time you " +
            "gain a level.");
        SetField(hillDwarfTraits, "modifiers", new List<FeatureModifier>
        {
            Modifier(statWisdom, RuleModifierOperation.Add, 1f),
        });

        var dwarf = CreateAsset<RaceDefinition>("race-dwarf", "Dwarf", all);
        SetField(dwarf, "displayName", "Dwarf");
        SetField(dwarf, "description", "Hill Dwarf: bold and hardy, dwarves are known as skilled warriors, " +
            "miners, and workers of stone and metal.");
        SetField(dwarf, "grantedFeatureIds", new List<DefinitionId> { dwarfTraits.Id, hillDwarfTraits.Id });

        // --- Elf / High Elf -------------------------------------------------------------------------------

        var elfTraits = CreateFeature("feature-elf-traits", "Elf Traits", all);
        SetField(elfTraits, "description",
            "Ability Score Increase: Your Dexterity score increases by 2.\n\n" +
            "Darkvision: Accustomed to twilit forests and the night sky, you have superior vision in dark and " +
            "dim conditions. You can see in dim light within 60 feet of you as if it were bright light, and in " +
            "darkness as if it were dim light. You can't discern color in darkness, only shades of gray.\n\n" +
            "Keen Senses: You have proficiency in the Perception skill.\n\n" +
            "Fey Ancestry: You have advantage on saving throws against being charmed, and magic can't put you " +
            "to sleep.\n\n" +
            "Trance: Elves don't need to sleep. Instead, they meditate deeply, remaining semiconscious, for 4 " +
            "hours a day. After resting in this way, you gain the same benefit that a human does from 8 hours " +
            "of sleep.");
        SetField(elfTraits, "modifiers", new List<FeatureModifier>
        {
            Modifier(statDexterity, RuleModifierOperation.Add, 2f),
            Modifier("Speed", RuleModifierOperation.Add, 30f),
        });
        SetField(elfTraits, "grantedProficiencies", new List<DefinitionId> { skillPerception });

        var highElfCantripChoice = Choice("choose-high-elf-cantrip", "Wizard Cantrip", ChoiceType.SelectSpell, 1);
        SetField(highElfCantripChoice, "filterTag", "wizard-cantrip");

        var highElfTraits = CreateFeature("feature-high-elf-traits", "High Elf Traits", all);
        SetField(highElfTraits, "description",
            "Ability Score Increase: Your Intelligence score increases by 1.\n\n" +
            "Elf Weapon Training: You have proficiency with the longsword, shortsword, shortbow, and " +
            "longbow.\n\n" +
            "Cantrip: You know one cantrip of your choice from the wizard spell list. Intelligence is your " +
            "spellcasting ability for it.\n\n" +
            "Extra Language: You can speak, read, and write one extra language of your choice.");
        SetField(highElfTraits, "modifiers", new List<FeatureModifier>
        {
            Modifier(statIntelligence, RuleModifierOperation.Add, 1f),
        });
        // Only item-longsword exists in this content folder today; shortsword/shortbow/longbow aren't
        // authored yet, so Elf Weapon Training is granted partially-mechanical (see header comment).
        SetField(highElfTraits, "grantedProficiencies", new List<DefinitionId> { itemLongsword });
        SetField(highElfTraits, "choices", new List<ChoiceRequirement> { highElfCantripChoice });

        var elf = CreateAsset<RaceDefinition>("race-elf", "Elf", all);
        SetField(elf, "displayName", "Elf");
        SetField(elf, "description", "High Elf: keen-eyed and quick-minded, elves value education and the " +
            "arcane arts.");
        SetField(elf, "grantedFeatureIds", new List<DefinitionId> { elfTraits.Id, highElfTraits.Id });

        // --- Halfling / Lightfoot Halfling ----------------------------------------------------------------

        var halflingTraits = CreateFeature("feature-halfling-traits", "Halfling Traits", all);
        SetField(halflingTraits, "description",
            "Ability Score Increase: Your Dexterity score increases by 2.\n\n" +
            "Lucky: When you roll a 1 on the d20 for an attack roll, ability check, or saving throw, you can " +
            "reroll the die and must use the new roll.\n\n" +
            "Brave: You have advantage on saving throws against being frightened.\n\n" +
            "Halfling Nimbleness: You can move through the space of any creature that is of a size larger " +
            "than yours.");
        SetField(halflingTraits, "modifiers", new List<FeatureModifier>
        {
            Modifier(statDexterity, RuleModifierOperation.Add, 2f),
            Modifier("Speed", RuleModifierOperation.Add, 25f),
        });

        var lightfootHalflingTraits = CreateFeature("feature-lightfoot-halfling-traits", "Lightfoot Halfling Traits", all);
        SetField(lightfootHalflingTraits, "description",
            "Ability Score Increase: Your Charisma score increases by 1.\n\n" +
            "Naturally Stealthy: You can attempt to hide even when you are obscured only by a creature that " +
            "is at least one size larger than you.");
        SetField(lightfootHalflingTraits, "modifiers", new List<FeatureModifier>
        {
            Modifier(statCharisma, RuleModifierOperation.Add, 1f),
        });

        var halfling = CreateAsset<RaceDefinition>("race-halfling", "Halfling", all);
        SetField(halfling, "displayName", "Halfling");
        SetField(halfling, "description", "Lightfoot Halfling: affable and unassuming, halflings possess a " +
            "natural gift for stealth and good fortune.");
        SetField(halfling, "grantedFeatureIds", new List<DefinitionId> { halflingTraits.Id, lightfootHalflingTraits.Id });

        // No AssetDatabase.Refresh()/SaveAssets() here: either can trigger a reimport that invalidates the
        // in-memory references held above, silently leaving later field writes dropped. Everything is saved
        // once, at the very end (see FighterSampleContentAuthor for the same pattern and the reasoning).
        foreach (var definition in all) EditorUtility.SetDirty(definition);
        AssetDatabase.SaveAssets();

        Debug.Log($"[RaceContentAuthor] Authored {all.Count} SRD5 race definitions under {ContentFolder}: " +
            "race-dwarf (Hill Dwarf), race-elf (High Elf), race-halfling (Lightfoot Halfling).");
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
        var safeName = assetName.Replace(" ", "").Replace(":", "");
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

    private static ChoiceRequirement Choice(string id, string displayName, ChoiceType type, int count, params DefinitionId[] options)
    {
        var requirement = new ChoiceRequirement();
        SetField(requirement, "id", id);
        SetField(requirement, "displayName", displayName);
        SetField(requirement, "type", type);
        SetField(requirement, "count", count);
        SetField(requirement, "options", new List<DefinitionId>(options));
        return requirement;
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
