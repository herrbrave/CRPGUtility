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
// One-time content author for a minimal SRD5 "Rogue" and "Barbarian", levels 1-3, no spellcasting: real .asset
// files saved under the package's Rules/SRD5e2014/Content/, authored the same reflection-driven way
// FighterSampleContentAuthor.cs authors the Fighter. This script re-creates the handful of shared stat/skill
// definitions it needs (StatDefinition/SkillDefinition assets are deleted-then-recreated by path via
// CreateAsset<T>, so running this alongside FighterSampleContentAuthor.cs, or sibling scripts authoring other
// classes/races/backgrounds that also touch shared skills, is always safe -- whichever runs last just
// harmlessly re-creates an identical asset) purely so this script has live object references to wire up; it
// does not modify any existing file.
//
// Mechanics deliberately authored as DATA ONLY, pending an engine hook that doesn't exist yet (see this
// class's own report to the user for the full rationale on each):
//   - Rogue Sneak Attack (feature-sneak-attack): its real trigger -- advantage on the attack, OR an ally
//     within 5 ft of the target -- is attacker-side/adjacency combat state nothing in this engine's Effects
//     can see yet (ConditionalEffect only inspects the TARGET's own conditions). Authored as an inert
//     FeatureDefinition with an accurate description; no modifiers.
//   - Rogue Cunning Action (ability-cunning-action / feature-cunning-action): Dash/Disengage/Hide have no
//     engine-level meaning yet -- there's no Speed/movement-as-a-resource concept in this content folder at
//     all. Authored as a real bonus-action-costing AbilityDefinition with an accurate description and zero
//     effects.
//   - Rogue Expertise (feature-expertise): RuleModifierOperation.GrantExpertise exists but nothing consumes
//     it yet. Authored as a FeatureDefinition carrying the real SelectSkill choice (count 2) and an accurate
//     description; the numeric proficiency-doubling isn't wired up.
//   - Barbarian Rage's numeric benefits (condition-raging): bonus melee damage and damage resistance aren't
//     expressible yet (no flat-bonus hook on DamageEffect, no resistance/immunity system at all). The
//     activation loop itself -- spend a rage use, become Raging for 10 rounds -- is fully real: ability-rage
//     spends resource-bonus-action + resource-rage-uses and applies condition-raging, which carries no
//     modifiers yet and is exactly where Rage's numeric benefits should be added once resistance exists.
//
// Mechanic authored as the REAL, WORKING thing (not a stub): Barbarian Reckless Attack. condition-reckless
// grants Advantage under the "AttackBonus" query key and Disadvantage under "AttacksAgainst" -- both are
// gathered and combined by the existing attack-resolution code (DefaultAttackBonusRule/AdvantageCollector),
// so this functions correctly at runtime today.
public static class MartialContentAuthor
{
    private const string ContentFolder = "Packages/com.crpgtoolkit.core/Rules/SRD5e2014/Content";

    [MenuItem("CRPG Toolkit/Content/Author Rogue and Barbarian Content (SRD5)")]
    public static void Author()
    {
        EnsureFolder(ContentFolder);
        var all = new List<DefinitionAsset>();

        var str = CreateStat("stat-strength", "Strength", "STR", all);
        var dex = CreateStat("stat-dexterity", "Dexterity", "DEX", all);
        var con = CreateStat("stat-constitution", "Constitution", "CON", all);
        var intl = CreateStat("stat-intelligence", "Intelligence", "INT", all);
        var wis = CreateStat("stat-wisdom", "Wisdom", "WIS", all);
        var cha = CreateStat("stat-charisma", "Charisma", "CHA", all);

        // Shared skills that already exist elsewhere in the package -- re-created here (identical ids,
        // display names, governing stats) purely to get live references; see the class-level comment.
        var athletics = CreateSkill("skill-athletics", "Athletics", str, all);
        var acrobatics = CreateSkill("skill-acrobatics", "Acrobatics", dex, all);
        var animalHandling = CreateSkill("skill-animal-handling", "Animal Handling", wis, all);
        var insight = CreateSkill("skill-insight", "Insight", wis, all);
        var intimidation = CreateSkill("skill-intimidation", "Intimidation", cha, all);
        var perception = CreateSkill("skill-perception", "Perception", wis, all);
        var survival = CreateSkill("skill-survival", "Survival", wis, all);

        // New skills this content needs that don't exist yet anywhere in the package.
        var deception = CreateSkill("skill-deception", "Deception", cha, all);
        var investigation = CreateSkill("skill-investigation", "Investigation", intl, all);
        var performance = CreateSkill("skill-performance", "Performance", cha, all);
        var persuasion = CreateSkill("skill-persuasion", "Persuasion", cha, all);
        var sleightOfHand = CreateSkill("skill-sleight-of-hand", "Sleight of Hand", dex, all);
        var stealth = CreateSkill("skill-stealth", "Stealth", dex, all);
        var nature = CreateSkill("skill-nature", "Nature", intl, all);

        var bonusAction = CreateResource("resource-bonus-action", "Bonus Action", all);
        var rageUses = CreateResource("resource-rage-uses", "Rage Uses", all, RestTrigger.LongRest);

        // ---------------------------------------------------------------------------------------------
        // Rogue
        // ---------------------------------------------------------------------------------------------

        var rogueSkillOptions = new[]
        {
            acrobatics.Id, athletics.Id, deception.Id, insight.Id, intimidation.Id,
            investigation.Id, perception.Id, performance.Id, persuasion.Id, sleightOfHand.Id, stealth.Id,
        };

        var rogueSkillChoice = CreateFeature("feature-rogue-skill-choice", "Rogue Skill Proficiencies", all);
        SetField(rogueSkillChoice, "choices", new List<ChoiceRequirement>
        {
            Choice("choose-rogue-skills", "Choose 4 skills", ChoiceType.SelectSkill, 4, rogueSkillOptions),
        });

        var thievesCant = CreateFeature("feature-thieves-cant", "Thieves' Cant", all);
        SetField(thievesCant, "description",
            "A secret mix of dialect, jargon, and code that allows you to hide messages in seemingly normal "
            + "conversation. Only another creature that knows thieves' cant understands such messages. It "
            + "takes four times longer to convey such a message than it does to speak the same idea plainly. "
            + "In addition, you understand a set of secret signs and symbols used to convey short, simple "
            + "messages. Flavor/roleplay only -- no mechanical modifiers.");

        var sneakAttack = CreateFeature("feature-sneak-attack", "Sneak Attack", all);
        SetField(sneakAttack, "description",
            "Once per turn, you can deal an extra 1d6 damage to one creature you hit with an attack if you "
            + "have advantage on the attack roll. The attack must use a finesse or a ranged weapon. You don't "
            + "need advantage on the attack roll if another enemy of the target is within 5 feet of it, that "
            + "enemy isn't incapacitated, and you don't have disadvantage on the attack roll. The extra damage "
            + "is 1d6 at levels 1-2, and increases to 2d6 at level 3 and beyond (SRD5 progresses further at "
            + "higher levels, out of scope here). INERT: this feature grants no mechanical modifiers. Its real "
            + "trigger -- advantage on the attack roll, OR an ally within 5 ft of the target -- is "
            + "attacker-side/adjacency combat state that no effect type in this engine can currently see "
            + "(ConditionalEffect only inspects the target's own ConditionDefinitions). Authored ahead of a "
            + "future 'conditional bonus damage on an attack' effect hook.");

        var expertise = CreateFeature("feature-expertise", "Expertise", all);
        SetField(expertise, "description",
            "Choose 2 of your skill proficiencies. Your proficiency bonus is doubled for any ability check you "
            + "make that uses either of the chosen proficiencies. INERT: RuleModifierOperation.GrantExpertise "
            + "exists in the shared enum specifically for this, but nothing in the engine consumes it yet. The "
            + "player-facing choice below is real; the numeric doubling it should eventually grant per chosen "
            + "skill is not wired up.");
        SetField(expertise, "choices", new List<ChoiceRequirement>
        {
            Choice("choose-expertise-skills", "Choose 2 skills for Expertise", ChoiceType.SelectSkill, 2, rogueSkillOptions),
        });

        var cunningActionAbility = CreateAbility("ability-cunning-action", "Cunning Action", AbilityTag.Movement | AbilityTag.Utility, all);
        SetField(cunningActionAbility, "description",
            "Your quick thinking and agility allow you to move and act quickly. You can take a bonus action on "
            + "each of your turns to take the Dash, Disengage, or Hide action. INERT effects: Dash/Disengage/"
            + "Hide have no engine-level meaning yet -- there is no Speed/movement-as-a-resource concept in "
            + "this content folder. This ability is real data (it costs a bonus action and is granted at level "
            + "2) with zero mechanical effects pending that system.");
        SetField(cunningActionAbility, "costs", new List<AbilityCost> { Cost(bonusAction, 1) });
        SetField(cunningActionAbility, "targeting", SelfTargeting());
        SetField(cunningActionAbility, "effects", new List<AbilityEffect>());

        var cunningActionFeature = CreateFeature("feature-cunning-action", "Cunning Action", all);
        SetField(cunningActionFeature, "grantedAbilityIds", new List<DefinitionId> { cunningActionAbility.Id });

        var rogue = CreateAsset<ClassDefinition>("class-rogue", "Rogue", all);
        SetField(rogue, "displayName", "Rogue");
        SetField(rogue, "hitDie", new DiceExpression(1, 8));
        SetField(rogue, "primaryStatIds", new List<DefinitionId> { dex.Id });
        SetField(rogue, "savingThrowProficiencyIds", new List<DefinitionId> { dex.Id, intl.Id });
        SetField(rogue, "levelProgression", new List<ClassLevelGrant>
        {
            LevelGrant(1, rogueSkillChoice.Id, thievesCant.Id, sneakAttack.Id, expertise.Id),
            LevelGrant(2, cunningActionFeature.Id),
        });

        // ---------------------------------------------------------------------------------------------
        // Barbarian
        // ---------------------------------------------------------------------------------------------

        var barbarianSkillChoice = CreateFeature("feature-barbarian-skill-choice", "Barbarian Skill Proficiencies", all);
        SetField(barbarianSkillChoice, "choices", new List<ChoiceRequirement>
        {
            Choice("choose-barbarian-skills", "Choose 2 skills", ChoiceType.SelectSkill, 2,
                animalHandling.Id, athletics.Id, intimidation.Id, nature.Id, perception.Id, survival.Id),
        });

        var recklessCondition = CreateAsset<ConditionDefinition>("condition-reckless", "Reckless", all);
        SetField(recklessCondition, "displayName", "Reckless");
        SetField(recklessCondition, "description",
            "You are attacking recklessly: you have advantage on melee weapon attack rolls using Strength "
            + "during this turn, but attack rolls against you have advantage until your next turn. REAL/"
            + "WORKING: grants Advantage under the shared \"AttackBonus\" query key (your own attack rolls "
            + "while this condition applies) and Disadvantage under \"AttacksAgainst\" (per this engine's "
            + "existing DefaultAttackBonusRule.ComputeAdvantage convention, where an AttacksAgainst-tagged "
            + "modifier expresses the state that gets combined into attacks targeting you). Both are gathered "
            + "and combined automatically by the existing DefaultAttackBonusRule/AdvantageCollector machinery, "
            + "so this functions correctly at runtime today.");
        SetField(recklessCondition, "modifiers", new List<FeatureModifier>
        {
            Modifier("AttackBonus", RuleModifierOperation.Advantage, 0f),
            Modifier("AttacksAgainst", RuleModifierOperation.Disadvantage, 0f),
        });

        var recklessAttackAbility = CreateAbility("ability-reckless-attack", "Reckless Attack", AbilityTag.Utility, all);
        SetField(recklessAttackAbility, "description",
            "When you make your first attack on your turn, you can decide to attack recklessly. Doing so gives "
            + "you advantage on melee weapon attack rolls using Strength during this turn, but attack rolls "
            + "against you have advantage until your next turn. Modeled here as a self-targeted, no-resource-"
            + "cost ability (the real rule is a decision made alongside your first attack, not a separate "
            + "action or bonus action) that applies condition-reckless to yourself for the rest of the round.");
        SetField(recklessAttackAbility, "targeting", SelfTargeting());
        var applyReckless = new ApplyConditionEffect();
        SetField(applyReckless, "condition", recklessCondition);
        SetField(applyReckless, "duration", 1);
        SetField(recklessAttackAbility, "effects", new List<AbilityEffect> { applyReckless });

        var recklessAttackFeature = CreateFeature("feature-reckless-attack", "Reckless Attack", all);
        SetField(recklessAttackFeature, "grantedAbilityIds", new List<DefinitionId> { recklessAttackAbility.Id });

        var ragingCondition = CreateAsset<ConditionDefinition>("condition-raging", "Raging", all);
        SetField(ragingCondition, "displayName", "Raging");
        SetField(ragingCondition, "description",
            "While raging, you gain the following benefits if you aren't wearing heavy armor: you have "
            + "advantage on Strength checks and Strength saving throws; when you make a melee weapon attack "
            + "using Strength, you gain a bonus to the damage roll (+2 at levels 1-8); you have resistance to "
            + "bludgeoning, piercing, and slashing damage. Your rage lasts for 1 minute (~10 rounds), and ends "
            + "early if you are knocked unconscious or if your turn ends and you haven't attacked a hostile "
            + "creature or taken damage since your last turn. INERT modifiers: this condition carries no "
            + "FeatureModifiers yet. The flat damage bonus has no hook on DamageEffect yet, and damage "
            + "resistance/immunity isn't a system in this engine yet (both are being built as separate engine "
            + "features). Once they exist, this is exactly where Rage's numeric benefits belong -- the "
            + "activation loop around it (spend a rage use, become Raging for 10 rounds, expire) is fully real "
            + "and testable today.");

        var rageAbility = CreateAbility("ability-rage", "Rage", AbilityTag.Utility, all);
        SetField(rageAbility, "description",
            "In battle, you fight with primal ferocity. On your turn, you can enter a rage as a bonus action. "
            + "REAL/WORKING activation loop: costs a bonus action plus one use of resource-rage-uses "
            + "(long-rest recovery) and applies condition-raging to yourself for 10 rounds (~1 minute). See "
            + "condition-raging for which of Rage's numeric benefits are still pending an engine hook.");
        SetField(rageAbility, "costs", new List<AbilityCost> { Cost(bonusAction, 1), Cost(rageUses, 1) });
        SetField(rageAbility, "targeting", SelfTargeting());
        var applyRaging = new ApplyConditionEffect();
        SetField(applyRaging, "condition", ragingCondition);
        SetField(applyRaging, "duration", 10);
        SetField(rageAbility, "effects", new List<AbilityEffect> { applyRaging });

        var rageFeature = CreateFeature("feature-rage", "Rage", all);
        SetField(rageFeature, "grantedResources", new List<ResourceGrant> { ResourceGrantOf(rageUses, 2) });
        SetField(rageFeature, "grantedAbilityIds", new List<DefinitionId> { rageAbility.Id });

        var barbarian = CreateAsset<ClassDefinition>("class-barbarian", "Barbarian", all);
        SetField(barbarian, "displayName", "Barbarian");
        SetField(barbarian, "hitDie", new DiceExpression(1, 12));
        SetField(barbarian, "primaryStatIds", new List<DefinitionId> { str.Id });
        SetField(barbarian, "savingThrowProficiencyIds", new List<DefinitionId> { str.Id, con.Id });
        SetField(barbarian, "levelProgression", new List<ClassLevelGrant>
        {
            LevelGrant(1, barbarianSkillChoice.Id, rageFeature.Id, recklessAttackFeature.Id),
        });

        // No AssetDatabase.Refresh()/SaveAssets() here: either can trigger a reimport that invalidates the
        // in-memory references held above, silently leaving later field writes on an already-saved asset
        // untracked. Everything is saved once, at the very end (see FighterSampleContentAuthor.cs for the
        // same pattern and its rationale).

        foreach (var definition in all) EditorUtility.SetDirty(definition);
        AssetDatabase.SaveAssets();

        Debug.Log($"[MartialContentAuthor] Authored {all.Count} SRD5 definitions under {ContentFolder} "
            + "(Rogue and Barbarian, levels 1-3).");
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

    private static TargetingDefinition SelfTargeting()
    {
        var targeting = new TargetingDefinition();
        SetField(targeting, "mode", TargetMode.Self);
        SetField(targeting, "relation", TargetRelation.Self);
        SetField(targeting, "minTargets", 1);
        SetField(targeting, "maxTargets", 1);
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
