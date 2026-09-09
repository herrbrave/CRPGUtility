using System;
using System.Collections.Generic;
using System.Reflection;
using CRPG.Abilities;
using CRPG.Actors;
using CRPG.Core;
using CRPG.Rules;
using UnityEditor;
using UnityEngine;

// Dogfood-only tooling (see third_party_assets / TestProject conventions) -- not part of the shipped package.
//
// One-time content author for a minimal SRD5 "Cleric, levels 1-3" slice: real .asset files saved under the
// package's Rules/SRD5e2014/Content/, authored the same reflection-driven way FighterSampleContentAuthor.cs
// authors the Fighter -- there's no interactive Inspector session doing the clicking, so this script does it
// once instead. Mirrors that script's helper methods verbatim (CreateAsset/SetField/EnsureFolder/Modifier/
// Choice/Cost/ResourceGrantOf/LevelGrant) rather than sharing code with it -- duplication across one-time
// authoring scripts is the established pattern here, not an oversight.
//
// Spell mechanics were pulled from Open5e's SRD5.1 data (api.open5e.com), not recalled from training data, per
// this session's instruction that a past session confirmed Open5e serves accurate 2014/5.1 wording.
//
// Deliberately simplified or skipped, same "author what's expressible, note the rest" spirit as Fighter's own
// header comment:
//  - No damage-type modeling exists yet (fire/radiant/etc.) -- Sacred Flame and Guiding Bolt deal plain,
//    untyped DamageEffect damage.
//  - Cure Wounds/Healing Word/Prayer of Healing's "+ your spellcasting ability modifier" is baked in as a
//    flat +3 (a 16 Wisdom caster), the same fixed-value simplification Fighter's Second Wind used ("1d10 +
//    fighter level, fixed at 3 for this sample") -- HealEffect/DamageEffect only take a flat DiceExpression,
//    there's no live ability-modifier lookup wired into an authored dice amount.
//  - Bless and Shield of Faith are both temporary buffs (1 minute / 10 minutes, both concentration) with no
//    auto-expiring-buff mechanism in the engine yet, so both are modeled as GrantFeatureEffect granting a
//    permanent FeatureDefinition -- same gap the architecture notes call out explicitly ("grant it permanently
//    for simplicity and note the gap"). Bless's "+1d4 to attack rolls and saving throws" is approximated as a
//    flat +2 (the average of 1d4) on the AttackBonus query key and on every SavingThrow:<stat> key, since
//    FeatureModifier only carries a flat numeric value, not a rollable die.
//  - Command collapses the real spell's five word-options (Approach/Drop/Flee/Grovel/Halt) into one
//    representative effect: a new "condition-prone" ConditionDefinition (Disadvantage on AttackBonus,
//    Advantage on AttacksAgainst -- real Prone mechanics, expressible as flag modifiers) applied for 1 round,
//    standing in for "Grovel". There's no branching command-word choice system to pick from the other four.
//  - Lesser Restoration ends "one disease or one condition (blinded, deafened, paralyzed, or poisoned)"; a new
//    "condition-poisoned" ConditionDefinition (Disadvantage on AttackBonus, the one poisoned effect a flat
//    modifier can express) stands in as the single representative condition it removes.
//  - Prayer of Healing's 10-minute casting time has no matching action-economy resource (only Action/Bonus
//    Action/Reaction exist) -- its ability only costs the spell slot, not an action.
//  - Silence's "no sound / anti-verbal-component sphere" has no real mechanical hook in the engine (no sound
//    system, no verbal-component gating) -- authored with Sphere targeting/range/area declared and zero
//    Effects, matching the brief's own suggested substitution.
//  - Spiritual Weapon (a persistent summoned attacker) and Divine Domain/Channel Divinity (Cleric's actual
//    level-1/2 subclass features) are out of scope entirely -- the brief only asks for the four listed level-1
//    grants, and a summoned independently-acting weapon has no supporting system yet.
//  - Tags are limited to exactly what the brief calls for (Spell always; Healing for heals; Concentration for
//    real concentration spells) rather than also guessing at Attack/Ranged/Hostile/Melee, so tagging stays
//    predictable across this and the four sibling scripts authoring other classes/races/backgrounds in parallel.
public static class ClericContentAuthor
{
    private const string ContentFolder = "Packages/com.crpgtoolkit.core/Rules/SRD5e2014/Content";

    [MenuItem("CRPG Toolkit/Content/Author Cleric Content (SRD5)")]
    public static void Author()
    {
        EnsureFolder(ContentFolder);
        var all = new List<DefinitionAsset>();

        // --- Existing shared content: loaded, never recreated/modified. ---
        var str = LoadExisting<StatDefinition>("Strength");
        var dex = LoadExisting<StatDefinition>("Dexterity");
        var con = LoadExisting<StatDefinition>("Constitution");
        var intl = LoadExisting<StatDefinition>("Intelligence");
        var wis = LoadExisting<StatDefinition>("Wisdom");
        var cha = LoadExisting<StatDefinition>("Charisma");

        var history = LoadExisting<SkillDefinition>("History");
        var insight = LoadExisting<SkillDefinition>("Insight");

        var hp = LoadExisting<ResourceDefinition>("HitPoints");
        var action = LoadExisting<ResourceDefinition>("Action");
        var bonusAction = LoadExisting<ResourceDefinition>("BonusAction");

        // --- New shared content (idempotent -- CreateAsset deletes-then-recreates by path, so a sibling
        // agent authoring another class independently creating the same ids is harmless). ---
        var medicine = CreateSkill("skill-medicine", "Medicine", wis, all);
        var persuasion = CreateSkill("skill-persuasion", "Persuasion", cha, all);
        var religion = CreateSkill("skill-religion", "Religion", intl, all);

        var spellSlot1 = CreateResource("resource-spell-slot-1", "Spell Slot Level 1", all, RestTrigger.LongRest);
        var spellSlot2 = CreateResource("resource-spell-slot-2", "Spell Slot Level 2", all, RestTrigger.LongRest);

        // --- Conditions ---
        var prone = CreateAsset<ConditionDefinition>("condition-prone", "Prone", all);
        SetField(prone, "displayName", "Prone");
        SetField(prone, "description",
            "A prone creature's only movement option is to crawl, unless it stands up. The creature has " +
            "disadvantage on attack rolls, and an attack roll against the creature has advantage if the " +
            "attacker is within 5 feet, disadvantage otherwise.");
        SetField(prone, "modifiers", new List<FeatureModifier>
        {
            Modifier("AttackBonus", RuleModifierOperation.Disadvantage, 0f),
            Modifier("AttacksAgainst", RuleModifierOperation.Advantage, 0f),
        });

        var poisoned = CreateAsset<ConditionDefinition>("condition-poisoned", "Poisoned", all);
        SetField(poisoned, "displayName", "Poisoned");
        SetField(poisoned, "description", "A poisoned creature has disadvantage on attack rolls and ability checks.");
        SetField(poisoned, "modifiers", new List<FeatureModifier>
        {
            Modifier("AttackBonus", RuleModifierOperation.Disadvantage, 0f),
        });

        // --- Cantrips (SRD5.1 text via Open5e) ---
        var sacredFlame = CreateAbility("spell-sacred-flame", "Sacred Flame", AbilityTag.Spell, all);
        SetField(sacredFlame, "description",
            "Flame-like radiance descends on a creature that you can see within range. The target must " +
            "succeed on a Dexterity saving throw or take 1d8 radiant damage (treated as generic damage -- no " +
            "damage-type modeling yet). The spell's damage increases by 1d8 when you reach 5th level (2d8), " +
            "11th level (3d8), and 17th level (4d8).");
        SetField(sacredFlame, "costs", new List<AbilityCost> { Cost(action, 1) });
        SetField(sacredFlame, "targeting", Targeting(TargetMode.SingleUnit, TargetRelation.Enemy, 1, 1, 18f));
        {
            var damage = new DamageEffect();
            SetField(damage, "hitPointResource", hp);
            SetField(damage, "amount", new DiceExpression(1, 8));
            var save = new SavingThrowEffect();
            SetField(save, "savingStatId", dex.Id);
            SetField(save, "useCasterSpellSaveDC", true);
            SetField(save, "onFailure", (AbilityEffect)damage);
            SetField(sacredFlame, "effects", new List<AbilityEffect> { save });
        }

        var guidance = CreateAbility("spell-guidance", "Guidance", AbilityTag.Spell | AbilityTag.Concentration, all);
        SetField(guidance, "description",
            "You touch one willing creature. Once before the spell ends, the target can roll a d4 and add the " +
            "number rolled to one ability check of its choice. It can roll the die before or after making the " +
            "ability check. The spell then ends. (No ability-check system exists yet to hook this into -- " +
            "authored as a zero-effect utility spell, same as Light/Thaumaturgy.)");
        SetField(guidance, "costs", new List<AbilityCost> { Cost(action, 1) });
        SetField(guidance, "targeting", Targeting(TargetMode.SingleUnit, TargetRelation.Ally, 1, 1, 1.5f));
        SetField(guidance, "effects", new List<AbilityEffect>());

        var light = CreateAbility("spell-light", "Light", AbilityTag.Spell, all);
        SetField(light, "description",
            "You touch one object that is no larger than 10 feet in any dimension. Until the spell ends, the " +
            "object sheds bright light in a 20-foot radius and dim light for an additional 20 feet. Purely " +
            "cosmetic -- no lighting/visibility system to hook into, authored with zero Effects.");
        SetField(light, "costs", new List<AbilityCost> { Cost(action, 1) });
        SetField(light, "targeting", Targeting(TargetMode.SingleUnit, TargetRelation.Any, 1, 1, 1.5f));
        SetField(light, "effects", new List<AbilityEffect>());

        var thaumaturgy = CreateAbility("spell-thaumaturgy", "Thaumaturgy", AbilityTag.Spell, all);
        SetField(thaumaturgy, "description",
            "You manifest a minor wonder, a sign of supernatural power, within range: booming voice, flickering " +
            "flames, harmless tremors, an instantaneous sound, a door flying open or slamming shut, or altered " +
            "eyes, for 1 minute. Flavor-only, authored with zero Effects.");
        SetField(thaumaturgy, "costs", new List<AbilityCost> { Cost(action, 1) });
        SetField(thaumaturgy, "targeting", Targeting(TargetMode.Point, TargetRelation.Any, 1, 1, 9f));
        SetField(thaumaturgy, "effects", new List<AbilityEffect>());

        // --- 1st-level spells ---
        var cureWounds = CreateAbility("spell-cure-wounds", "Cure Wounds", AbilityTag.Spell | AbilityTag.Healing, all);
        SetField(cureWounds, "description",
            "A creature you touch regains a number of hit points equal to 1d8 + your spellcasting ability " +
            "modifier (fixed at +3 for this sample -- see header comment). This spell has no effect on undead " +
            "or constructs. When cast using a higher-level slot, healing increases by 1d8 per slot level above 1st.");
        SetField(cureWounds, "costs", new List<AbilityCost> { Cost(action, 1), Cost(spellSlot1, 1) });
        SetField(cureWounds, "targeting", Targeting(TargetMode.SingleUnit, TargetRelation.Ally, 1, 1, 1.5f));
        {
            var heal = new HealEffect();
            SetField(heal, "hitPointResource", hp);
            SetField(heal, "amount", new DiceExpression(1, 8, 3));
            SetField(cureWounds, "effects", new List<AbilityEffect> { heal });
        }

        var guidingBolt = CreateAbility("spell-guiding-bolt", "Guiding Bolt", AbilityTag.Spell, all);
        SetField(guidingBolt, "description",
            "A flash of light streaks toward a creature of your choice within range. Make a ranged spell " +
            "attack against the target. On a hit, the target takes 4d6 radiant damage (treated as generic " +
            "damage), and the next attack roll made against this target before the end of your next turn has " +
            "advantage (not modeled -- no 'advantage until next turn' timer exists yet). When cast using a " +
            "higher-level slot, damage increases by 1d6 per slot level above 1st.");
        SetField(guidingBolt, "costs", new List<AbilityCost> { Cost(action, 1), Cost(spellSlot1, 1) });
        SetField(guidingBolt, "targeting", Targeting(TargetMode.SingleUnit, TargetRelation.Enemy, 1, 1, 36f));
        {
            var damage = new DamageEffect();
            SetField(damage, "hitPointResource", hp);
            SetField(damage, "amount", new DiceExpression(4, 6));
            var attackRoll = new AttackRollEffect();
            // governingStatId left at DefinitionId.None (default) -- resolved from the caster's own
            // CharacterState.SpellcastingStatId at cast time, per the architecture notes.
            SetField(attackRoll, "onHit", (AbilityEffect)damage);
            SetField(guidingBolt, "effects", new List<AbilityEffect> { attackRoll });
        }

        var bless = CreateAbility("spell-bless", "Bless", AbilityTag.Spell | AbilityTag.Concentration, all);
        SetField(bless, "description",
            "You bless up to three creatures of your choice within range. Whenever a target makes an attack " +
            "roll or a saving throw before the spell ends, the target can roll a d4 and add the number rolled " +
            "to the attack roll or saving throw. Concentration, up to 1 minute. Modeled as a permanently " +
            "granted FeatureDefinition (feature-bless) with a flat +2 approximation of the 1d4 bonus, applied " +
            "to AttackBonus and every SavingThrow:<stat> query key, since there's no auto-expiring buff " +
            "mechanism or rollable-modifier support yet -- see header comment.");
        SetField(bless, "costs", new List<AbilityCost> { Cost(action, 1), Cost(spellSlot1, 1) });
        SetField(bless, "targeting", Targeting(TargetMode.MultiUnit, TargetRelation.Ally, 1, 3, 9f));
        {
            var blessFeature = CreateFeature("feature-bless", "Blessed", all);
            var blessModifiers = new List<FeatureModifier> { Modifier("AttackBonus", RuleModifierOperation.Add, 2f) };
            foreach (var stat in new[] { str, dex, con, intl, wis, cha })
                blessModifiers.Add(Modifier("SavingThrow:" + stat.Id.Value, RuleModifierOperation.Add, 2f));
            SetField(blessFeature, "modifiers", blessModifiers);

            var grant = new GrantFeatureEffect();
            SetField(grant, "feature", blessFeature);
            SetField(grant, "applyToSelf", false);
            SetField(bless, "effects", new List<AbilityEffect> { grant });
        }

        var command = CreateAbility("spell-command", "Command", AbilityTag.Spell, all);
        SetField(command, "description",
            "You speak a one-word command to a creature you can see within range. The target must succeed on " +
            "a Wisdom saving throw or follow the command on its next turn (Approach/Drop/Flee/Grovel/Halt; the " +
            "DM decides other commands' effects). No effect on undead, creatures that don't understand your " +
            "language, or creatures immune to being charmed by a command that would harm it. Simplified here " +
            "to a single representative outcome -- 'Grovel', modeled as the Prone condition for 1 round -- " +
            "since there's no branching command-word choice system. When cast using a higher-level slot, " +
            "affects one additional creature per slot level above 1st.");
        SetField(command, "costs", new List<AbilityCost> { Cost(action, 1), Cost(spellSlot1, 1) });
        SetField(command, "targeting", Targeting(TargetMode.SingleUnit, TargetRelation.Enemy, 1, 1, 18f));
        {
            var applyProne = new ApplyConditionEffect();
            SetField(applyProne, "condition", prone);
            SetField(applyProne, "duration", 1);
            var save = new SavingThrowEffect();
            SetField(save, "savingStatId", wis.Id);
            SetField(save, "useCasterSpellSaveDC", true);
            SetField(save, "onFailure", (AbilityEffect)applyProne);
            SetField(command, "effects", new List<AbilityEffect> { save });
        }

        var healingWord = CreateAbility("spell-healing-word", "Healing Word", AbilityTag.Spell | AbilityTag.Healing, all);
        SetField(healingWord, "description",
            "A creature of your choice that you can see within range regains hit points equal to 1d4 + your " +
            "spellcasting ability modifier (fixed at +3 for this sample). No effect on undead or constructs. " +
            "When cast using a higher-level slot, healing increases by 1d4 per slot level above 1st.");
        SetField(healingWord, "costs", new List<AbilityCost> { Cost(bonusAction, 1), Cost(spellSlot1, 1) });
        SetField(healingWord, "targeting", Targeting(TargetMode.SingleUnit, TargetRelation.Ally, 1, 1, 18f));
        {
            var heal = new HealEffect();
            SetField(heal, "hitPointResource", hp);
            SetField(heal, "amount", new DiceExpression(1, 4, 3));
            SetField(healingWord, "effects", new List<AbilityEffect> { heal });
        }

        var shieldOfFaith = CreateAbility("spell-shield-of-faith", "Shield of Faith", AbilityTag.Spell | AbilityTag.Concentration, all);
        SetField(shieldOfFaith, "description",
            "A shimmering field appears and surrounds a creature of your choice within range, granting it a " +
            "+2 bonus to AC for the duration (concentration, up to 10 minutes). Modeled as a permanently " +
            "granted FeatureDefinition (feature-shield-of-faith), same auto-expiring-buff gap as Bless.");
        SetField(shieldOfFaith, "costs", new List<AbilityCost> { Cost(bonusAction, 1), Cost(spellSlot1, 1) });
        SetField(shieldOfFaith, "targeting", Targeting(TargetMode.SingleUnit, TargetRelation.Ally, 1, 1, 18f));
        {
            var shieldFeature = CreateFeature("feature-shield-of-faith", "Shield of Faith", all);
            SetField(shieldFeature, "modifiers", new List<FeatureModifier> { Modifier("ArmorClass", RuleModifierOperation.Add, 2f) });

            var grant = new GrantFeatureEffect();
            SetField(grant, "feature", shieldFeature);
            SetField(grant, "applyToSelf", false);
            SetField(shieldOfFaith, "effects", new List<AbilityEffect> { grant });
        }

        // --- 2nd-level spells ---
        var lesserRestoration = CreateAbility("spell-lesser-restoration", "Lesser Restoration", AbilityTag.Spell, all);
        SetField(lesserRestoration, "description",
            "You touch a creature and can end either one disease or one condition afflicting it: blinded, " +
            "deafened, paralyzed, or poisoned. Poisoned is authored here as the one representative condition " +
            "(condition-poisoned) this removes.");
        SetField(lesserRestoration, "costs", new List<AbilityCost> { Cost(action, 1), Cost(spellSlot2, 1) });
        SetField(lesserRestoration, "targeting", Targeting(TargetMode.SingleUnit, TargetRelation.Ally, 1, 1, 1.5f));
        {
            var removeCondition = new RemoveConditionEffect();
            SetField(removeCondition, "condition", poisoned);
            SetField(lesserRestoration, "effects", new List<AbilityEffect> { removeCondition });
        }

        var prayerOfHealing = CreateAbility("spell-prayer-of-healing", "Prayer of Healing", AbilityTag.Spell | AbilityTag.Healing, all);
        SetField(prayerOfHealing, "description",
            "Up to six creatures of your choice that you can see within range each regain hit points equal to " +
            "2d8 + your spellcasting ability modifier (fixed at +3 for this sample). No effect on undead or " +
            "constructs. Casting time is 10 minutes, which has no matching action-economy resource here (only " +
            "Action/Bonus Action/Reaction exist), so this ability only costs the spell slot. When cast using a " +
            "higher-level slot, healing increases by 1d8 per slot level above 2nd.");
        SetField(prayerOfHealing, "costs", new List<AbilityCost> { Cost(spellSlot2, 1) });
        SetField(prayerOfHealing, "targeting", Targeting(TargetMode.MultiUnit, TargetRelation.Ally, 1, 6, 9f));
        {
            var heal = new HealEffect();
            SetField(heal, "hitPointResource", hp);
            SetField(heal, "amount", new DiceExpression(2, 8, 3));
            SetField(prayerOfHealing, "effects", new List<AbilityEffect> { heal });
        }

        var silence = CreateAbility("spell-silence", "Silence", AbilityTag.Spell | AbilityTag.Concentration, all);
        SetField(silence, "description",
            "For the duration (concentration, up to 10 minutes), no sound can be created within or pass " +
            "through a 20-foot-radius sphere centered on a point you choose within range. Creatures entirely " +
            "inside are immune to thunder damage and deafened, and casting a spell with a verbal component is " +
            "impossible there. No sound system or verbal-component gating exists yet, so this is authored with " +
            "Sphere targeting/range/area declared and zero mechanical Effects.");
        SetField(silence, "costs", new List<AbilityCost> { Cost(action, 1), Cost(spellSlot2, 1) });
        SetField(silence, "targeting", Targeting(TargetMode.Sphere, TargetRelation.Any, 1, 1, 36f, areaSize: 6f));
        SetField(silence, "effects", new List<AbilityEffect>());

        // --- Level 1 features: skill choice, cantrip choice, spell slots ---
        var skillChoiceFeature = CreateFeature("feature-cleric-skill-choice", "Cleric Skill Proficiencies", all);
        SetField(skillChoiceFeature, "choices", new List<ChoiceRequirement>
        {
            Choice("choose-cleric-skills", "Choose 2 skills", ChoiceType.SelectSkill, 2,
                history.Id, insight.Id, medicine.Id, persuasion.Id, religion.Id),
        });

        var cantripChoiceFeature = CreateFeature("feature-cleric-cantrips", "Cantrips", all);
        SetField(cantripChoiceFeature, "choices", new List<ChoiceRequirement>
        {
            Choice("choose-cleric-cantrips", "Choose 3 cantrips", ChoiceType.SelectSpell, 3,
                sacredFlame.Id, guidance.Id, light.Id, thaumaturgy.Id),
        });

        var slotsLevel1 = CreateFeature("feature-cleric-slots-1", "Spellcasting (1st Level)", all);
        SetField(slotsLevel1, "grantedResources", new List<ResourceGrant> { ResourceGrantOf(spellSlot1, 2) });

        var slotsLevel2 = CreateFeature("feature-cleric-slots-2", "Spellcasting (2nd Level)", all);
        SetField(slotsLevel2, "grantedResources", new List<ResourceGrant> { ResourceGrantOf(spellSlot1, 3) });

        var slotsLevel3 = CreateFeature("feature-cleric-slots-3", "Spellcasting (3rd Level)", all);
        SetField(slotsLevel3, "grantedResources", new List<ResourceGrant>
        {
            ResourceGrantOf(spellSlot1, 4),
            ResourceGrantOf(spellSlot2, 2),
        });

        // --- Class ---
        var cleric = CreateAsset<ClassDefinition>("class-cleric", "Cleric", all);
        SetField(cleric, "displayName", "Cleric");
        SetField(cleric, "description",
            "A priestly champion who wields divine magic in service of a higher power. Levels 1-3 only, no " +
            "Divine Domain subclass authored -- see header comment.");
        SetField(cleric, "hitDie", new DiceExpression(1, 8));
        SetField(cleric, "primaryStatIds", new List<DefinitionId> { wis.Id });
        SetField(cleric, "savingThrowProficiencyIds", new List<DefinitionId> { wis.Id, cha.Id });
        SetField(cleric, "spellcastingStatId", wis.Id);
        SetField(cleric, "spellcastingType", SpellcastingType.Prepared);
        SetField(cleric, "preparedSpellsLevelDivisor", 1);
        SetField(cleric, "spellListAbilityIds", new List<DefinitionId>
        {
            cureWounds.Id, guidingBolt.Id, bless.Id, command.Id, healingWord.Id, shieldOfFaith.Id,
            lesserRestoration.Id, prayerOfHealing.Id, silence.Id,
        });
        SetField(cleric, "levelProgression", new List<ClassLevelGrant>
        {
            LevelGrant(1, skillChoiceFeature.Id, cantripChoiceFeature.Id, slotsLevel1.Id),
            LevelGrant(2, slotsLevel2.Id),
            LevelGrant(3, slotsLevel3.Id),
        });

        // No AssetDatabase.Refresh()/SaveAssets() until the very end -- either can trigger a reimport that
        // invalidates in-memory references still held above (same gotcha FighterSampleContentAuthor documents).
        foreach (var definition in all) EditorUtility.SetDirty(definition);
        AssetDatabase.SaveAssets();

        Debug.Log($"[ClericContentAuthor] Authored {all.Count} SRD5 definitions under {ContentFolder}.");
    }

    private static SkillDefinition CreateSkill(string id, string displayName, StatDefinition governingStat, List<DefinitionAsset> all)
    {
        var skill = CreateAsset<SkillDefinition>(id, displayName, all);
        SetField(skill, "displayName", displayName);
        SetField(skill, "governingStat", governingStat);
        return skill;
    }

    private static ResourceDefinition CreateResource(string id, string displayName, List<DefinitionAsset> all, RestTrigger restTrigger = RestTrigger.None)
    {
        var resource = CreateAsset<ResourceDefinition>(id, displayName, all);
        SetField(resource, "displayName", displayName);
        SetField(resource, "restTrigger", restTrigger);
        return resource;
    }

    private static FeatureDefinition CreateFeature(string id, string displayName, List<DefinitionAsset> all)
    {
        var feature = CreateAsset<FeatureDefinition>(id, displayName, all);
        SetField(feature, "displayName", displayName);
        return feature;
    }

    private static AbilityDefinition CreateAbility(string id, string displayName, AbilityTag tags, List<DefinitionAsset> all)
    {
        var ability = CreateAsset<AbilityDefinition>(id, displayName, all);
        SetField(ability, "displayName", displayName);
        SetField(ability, "tags", tags);
        return ability;
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

    /// <summary>Looks up already-authored shared SRD5 content (stats, core resources, skills authored by
    /// FighterSampleContentAuthor) by the same {TypeName}_{SafeName}.asset path convention CreateAsset uses,
    /// without ever creating, deleting, or modifying it -- this script must not touch existing files.</summary>
    private static T LoadExisting<T>(string assetName) where T : DefinitionAsset
    {
        var path = $"{ContentFolder}/{typeof(T).Name}_{assetName}.asset";
        var asset = AssetDatabase.LoadAssetAtPath<T>(path);
        if (asset == null) throw new Exception($"[ClericContentAuthor] Expected existing shared asset at '{path}' but found none.");
        return asset;
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

    private static ResourceGrant ResourceGrantOf(ResourceDefinition resource, int maxAmount)
    {
        object boxed = new ResourceGrant();
        SetField(boxed, "resource", resource);
        SetField(boxed, "maxAmount", maxAmount);
        return (ResourceGrant)boxed;
    }

    private static AbilityCost Cost(ResourceDefinition resource, int amount)
    {
        object boxed = new AbilityCost();
        SetField(boxed, "resource", resource);
        SetField(boxed, "amount", amount);
        return (AbilityCost)boxed;
    }

    private static ClassLevelGrant LevelGrant(int level, params DefinitionId[] featureIds)
    {
        var grant = new ClassLevelGrant();
        SetField(grant, "level", level);
        SetField(grant, "featureIds", new List<DefinitionId>(featureIds));
        return grant;
    }

    private static TargetingDefinition Targeting(TargetMode mode, TargetRelation relation, int minTargets, int maxTargets, float range, float areaSize = 0f)
    {
        var targeting = new TargetingDefinition();
        SetField(targeting, "mode", mode);
        SetField(targeting, "relation", relation);
        SetField(targeting, "minTargets", minTargets);
        SetField(targeting, "maxTargets", maxTargets);
        SetField(targeting, "range", range);
        SetField(targeting, "areaSize", areaSize);
        return targeting;
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
