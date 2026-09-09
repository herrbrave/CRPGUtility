using System.Collections.Generic;
using System.Reflection;
using CRPG.Actors;
using CRPG.Core;
using CRPG.Rules;
using UnityEditor;
using UnityEngine;

// Dogfood-only tooling (see third_party_assets / TestProject conventions) -- not part of the shipped package.
//
// One-time content author for the remaining SRD5.1 conditions beyond Unconscious/Prone/Restrained/Invisible/
// Poisoned (authored in earlier sessions), plus the two SRD5 Variant: Encumbrance conditions. Built the same
// reflection-driven way as every other *ContentAuthor script here.
//
// Which conditions are real, working mechanics vs. flavor-only description text (same "author ahead of the
// consumer, note the gap" spirit as everywhere else in this codebase):
//   - Blinded: fully real (AttackBonus Disadvantage on self, AttacksAgainst Advantage).
//   - Charmed, Deafened: flavor-only -- no "can't target a specific creature" or "auto-fail hearing checks"
//     mechanism exists.
//   - Exhaustion: levels tracked via ConditionInstance.StackCount (see ExhaustionService). Speed reduction
//     (level 2+ half, level 5+ zero) is real, read directly by DefaultMovementSpeedRule off this condition's
//     id; instant death at level 6 is real, reported by ExhaustionService.IncreaseExhaustion. Disadvantage on
//     ability checks (level 1+) and on attacks/saves (level 3+), and halved HP maximum (level 4+), are NOT
//     modeled -- no generic non-skill "ability check" rule hook exists yet, and safely mutating/restoring a
//     derived max HP needs more care than this pass covers. No FeatureModifiers authored here at all --
//     everything real about this condition is special-cased code, not data.
//   - Frightened: approximated as AttackBonus Disadvantage (self) only -- the real rule is conditional on the
//     source of fear being visible, which isn't modeled, and the ability-check disadvantage has no hook either.
//   - Grappled: fully real (Speed Override 0, via DefaultMovementSpeedRule).
//   - Incapacitated, Paralyzed, Petrified, Stunned, Unconscious: PreventsActions is real (generalizes
//     AbilityResolutionService.CanUse, see its own remarks) for all five. Paralyzed/Petrified/Stunned/
//     Unconscious additionally get real AttacksAgainst Advantage and AutoFailSave on Strength and Dexterity
//     saves. Petrified's damage resistance to everything, and Unconscious/Paralyzed's melee-crit-within-5ft,
//     are NOT modeled (no enumerable "every damage type" concept, no range math in AttackRollEffect).
//   - Encumbered / Heavily Encumbered (SRD5 Variant: Encumbrance, applied by EncumbranceService.Refresh): both
//     get a real flat Speed penalty; Heavily Encumbered additionally gets real Disadvantage on Strength/
//     Dexterity/Constitution saving throws. The variant's attack-roll disadvantage for the same three abilities
//     is NOT modeled -- AttackBonus isn't keyed per governing-ability the way SavingThrow is, so "disadvantage
//     only on Str/Dex-governed attacks" has no query key to attach to.
public static class Tier3ConditionsContentAuthor
{
    private const string ContentFolder = "Packages/com.crpgtoolkit.core/Rules/SRD5e2014/Content";

    [MenuItem("CRPG Toolkit/Content/Author Tier 3 Conditions (SRD5)")]
    public static void Author()
    {
        EnsureFolder(ContentFolder);
        var all = new List<DefinitionAsset>();

        const string statStrength = "stat-strength";
        const string statDexterity = "stat-dexterity";
        const string statConstitution = "stat-constitution";
        const string attackBonusKey = "AttackBonus";
        const string attacksAgainstKey = "AttacksAgainst";
        const string speedKey = "Speed";

        var blinded = CreateCondition(all, "condition-blinded", "Blinded",
            "A blinded creature can't see and automatically fails any ability check that requires sight. " +
            "Attack rolls against the creature have advantage, and the creature's attack rolls have disadvantage.");
        SetField(blinded, "modifiers", new List<FeatureModifier>
        {
            Modifier(attackBonusKey, RuleModifierOperation.Disadvantage, 0f),
            Modifier(attacksAgainstKey, RuleModifierOperation.Advantage, 0f),
        });

        CreateCondition(all, "condition-charmed", "Charmed",
            "A charmed creature can't attack the charmer or target the charmer with harmful abilities or " +
            "magical effects. The charmer has advantage on any ability check to interact socially with the " +
            "creature. (Not mechanically enforced -- no \"can't target a specific creature\" mechanism exists.)");

        CreateCondition(all, "condition-deafened", "Deafened",
            "A deafened creature can't hear and automatically fails any ability check that requires hearing. " +
            "(Not mechanically enforced -- no generic hearing-based check hook exists.)");

        CreateCondition(all, "condition-exhaustion", "Exhaustion",
            "Effects are cumulative and tracked as a stacking level (see ExhaustionService), from 1 (disadvantage " +
            "on ability checks) to 6 (death). Level 2+ halves speed and level 5+ reduces it to 0 -- both enforced " +
            "automatically by DefaultMovementSpeedRule. Level 6 is instant death, reported by " +
            "ExhaustionService.IncreaseExhaustion. Disadvantage on ability checks/attacks/saves and halved hit " +
            "point maximum are not currently enforced.");

        var frightened = CreateCondition(all, "condition-frightened", "Frightened",
            "A frightened creature has disadvantage on ability checks and attack rolls while the source of its " +
            "fear is within line of sight. The creature can't willingly move closer to the source of its fear. " +
            "(Approximated: only the attack-roll disadvantage is enforced, unconditionally rather than only " +
            "while the source is visible; the ability-check part and the movement restriction are not modeled.)");
        SetField(frightened, "modifiers", new List<FeatureModifier> { Modifier(attackBonusKey, RuleModifierOperation.Disadvantage, 0f) });

        var grappled = CreateCondition(all, "condition-grappled", "Grappled",
            "A grappled creature's speed becomes 0, and it can't benefit from any bonus to its speed.");
        SetField(grappled, "modifiers", new List<FeatureModifier> { Modifier(speedKey, RuleModifierOperation.Override, 0f) });

        var incapacitated = CreateCondition(all, "condition-incapacitated", "Incapacitated",
            "An incapacitated creature can't take actions or reactions.");
        SetField(incapacitated, "preventsActions", true);

        CreateIncapacitatingCondition(all, "condition-paralyzed", "Paralyzed",
            "A paralyzed creature is incapacitated and can't move or speak. The creature automatically fails " +
            "Strength and Dexterity saving throws. Attack rolls against the creature have advantage. Any attack " +
            "that hits the creature is a critical hit if the attacker is within 5 feet. " +
            "(Melee-crit-within-5ft is not modeled -- no range math in AttackRollEffect.)",
            statStrength, statDexterity, attacksAgainstKey);

        CreateIncapacitatingCondition(all, "condition-petrified", "Petrified",
            "A petrified creature is transformed, along with any nonmagical object it is wearing or carrying, " +
            "into a solid inanimate substance. It is incapacitated, can't move or speak, and is unaware of its " +
            "surroundings. Attack rolls against the creature have advantage. The creature automatically fails " +
            "Strength and Dexterity saving throws. The creature has resistance to all damage. " +
            "(Damage resistance is not modeled -- no enumerable \"every damage type\" concept exists.)",
            statStrength, statDexterity, attacksAgainstKey);

        CreateIncapacitatingCondition(all, "condition-stunned", "Stunned",
            "A stunned creature is incapacitated, can't move, and can speak only falteringly. The creature " +
            "automatically fails Strength and Dexterity saving throws. Attack rolls against the creature have advantage.",
            statStrength, statDexterity, attacksAgainstKey);

        CreateIncapacitatingCondition(all, "condition-unconscious", "Unconscious",
            "An unconscious creature is incapacitated, can't move or speak, and is unaware of its surroundings. " +
            "The creature drops whatever it's holding and falls prone. The creature automatically fails Strength " +
            "and Dexterity saving throws. Attack rolls against the creature have advantage. Any attack that hits " +
            "the creature is a critical hit if the attacker is within 5 feet. (Falling prone and melee-crit-" +
            "within-5ft are not modeled; see DeathSaveService for how this condition is applied automatically.)",
            statStrength, statDexterity, attacksAgainstKey);

        // --- SRD5 Variant: Encumbrance (applied automatically by EncumbranceService.Refresh) ------------------

        var encumbered = CreateCondition(all, "condition-encumbered", "Encumbered",
            "Carrying more than 5 times your Strength score in weight: your speed drops by 10 feet.");
        SetField(encumbered, "modifiers", new List<FeatureModifier> { Modifier(speedKey, RuleModifierOperation.Add, -10f) });

        var heavilyEncumbered = CreateCondition(all, "condition-heavily-encumbered", "Heavily Encumbered",
            "Carrying more than 10 times your Strength score in weight: your speed drops by 20 feet, and you " +
            "have disadvantage on ability checks, attack rolls, and saving throws that use Strength, Dexterity, " +
            "or Constitution. (Only the speed penalty and the saving-throw disadvantage are enforced -- " +
            "AttackBonus has no per-governing-ability query key to attach the attack-roll part to, and no " +
            "generic non-skill ability-check hook exists for that part either.)");
        SetField(heavilyEncumbered, "modifiers", new List<FeatureModifier>
        {
            Modifier(speedKey, RuleModifierOperation.Add, -20f),
            Modifier("SavingThrow:" + statStrength, RuleModifierOperation.Disadvantage, 0f),
            Modifier("SavingThrow:" + statDexterity, RuleModifierOperation.Disadvantage, 0f),
            Modifier("SavingThrow:" + statConstitution, RuleModifierOperation.Disadvantage, 0f),
        });

        foreach (var definition in all) EditorUtility.SetDirty(definition);
        AssetDatabase.SaveAssets();

        Debug.Log($"[Tier3ConditionsContentAuthor] Authored {all.Count} SRD5 conditions under {ContentFolder}.");
    }

    /// <summary>Shared shape for Paralyzed/Petrified/Stunned/Unconscious: PreventsActions + AttacksAgainst
    /// Advantage + AutoFailSave on both Strength and Dexterity saves.</summary>
    private static ConditionDefinition CreateIncapacitatingCondition(
        List<DefinitionAsset> all, string id, string displayName, string description, string strengthStatId, string dexterityStatId, string attacksAgainstKey)
    {
        var condition = CreateCondition(all, id, displayName, description);
        SetField(condition, "preventsActions", true);
        SetField(condition, "modifiers", new List<FeatureModifier>
        {
            Modifier(attacksAgainstKey, RuleModifierOperation.Advantage, 0f),
            Modifier("SavingThrow:" + strengthStatId, RuleModifierOperation.AutoFailSave, 0f),
            Modifier("SavingThrow:" + dexterityStatId, RuleModifierOperation.AutoFailSave, 0f),
        });
        return condition;
    }

    private static ConditionDefinition CreateCondition(List<DefinitionAsset> all, string id, string displayName, string description)
    {
        var condition = CreateAsset<ConditionDefinition>(id, displayName, all);
        SetField(condition, "displayName", displayName);
        SetField(condition, "description", description);
        return condition;
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
