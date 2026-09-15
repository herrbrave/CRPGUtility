using System.Collections.Generic;
using System.Reflection;
using CRPG.Abilities;
using CRPG.Actors;
using CRPG.Core;
using UnityEditor;
using UnityEngine;

// Dogfood-only demo authoring -- not part of the shipped package. Task 4.5: builds the tactical-slice-owned
// content (Movement resource, a melee weapon attack, a reaction opportunity attack + the feature granting it)
// and wires a TacticalSliceContentBundle referencing them alongside four real, unmodified SRD5e2014 spells
// (FireBolt/HealingWord/Bless/Command) for ranged/support/concentration/temporary-effect coverage. Re-runnable:
// re-authoring an already-existing asset edits it in place (SetField + SetDirty), never deletes/recreates,
// matching this project's established "never delete/recreate as an update" discipline.
public static class TacticalSliceContentAuthor
{
    private const string ContentFolder = "Assets/TacticalSlice/Content";
    private const string BundlePath = "Assets/TacticalSlice/TacticalSliceContentBundle.asset";
    private const string Srd5ContentFolder = "Packages/com.crpgtoolkit.core/Rules/SRD5e2014/Content";

    [MenuItem("CRPG Toolkit/Tactical Slice/Author Content")]
    public static void Author()
    {
        var fighterContent = AssetDatabase.LoadAssetAtPath<FighterSampleContentBundle>("Assets/FighterDemo/FighterSampleContentBundle.asset");
        if (fighterContent == null)
        {
            Debug.LogError("[TacticalSliceContentAuthor] Could not load FighterSampleContentBundle.asset.");
            return;
        }

        var movement = CreateOrLoad<ResourceDefinition>($"{ContentFolder}/ResourceDefinition_Movement.asset");
        SetField(movement, "id", "resource-tacticalslice-movement");
        SetField(movement, "displayName", "Movement");

        var meleeStrike = CreateOrLoad<AbilityDefinition>($"{ContentFolder}/AbilityDefinition_MeleeStrike.asset");
        AuthorMeleeAbility(
            meleeStrike, "ability-tacticalslice-meleestrike", "Melee Strike",
            "A basic weapon attack against a creature within melee reach.",
            AbilityTag.Attack | AbilityTag.Melee | AbilityTag.Weapon | AbilityTag.Hostile,
            fighterContent.Action, 1, fighterContent, prepareSeconds: 0.4f, recoverSeconds: 0.6f);

        var opportunityAttack = CreateOrLoad<AbilityDefinition>($"{ContentFolder}/AbilityDefinition_OpportunityAttack.asset");
        AuthorMeleeAbility(
            opportunityAttack, "ability-tacticalslice-opportunityattack", "Opportunity Attack",
            "A reflexive weapon attack against a creature that moves out of your reach -- SRD5's opportunity "
            + "attack, simplified to melee-range-only (no other real trigger/reaction content exists yet to "
            + "compete for the same reaction resource).",
            AbilityTag.Attack | AbilityTag.Melee | AbilityTag.Weapon | AbilityTag.Reaction | AbilityTag.Hostile,
            fighterContent.ReactionResource, 1, fighterContent, prepareSeconds: 0f, recoverSeconds: 0f);

        var reactiveStrikes = CreateOrLoad<FeatureDefinition>($"{ContentFolder}/FeatureDefinition_ReactiveStrikes.asset");
        SetField(reactiveStrikes, "id", "feature-tacticalslice-reactivestrikes");
        SetField(reactiveStrikes, "displayName", "Reactive Strikes");
        SetField(reactiveStrikes, "description", "Grants Opportunity Attack -- can make a reflexive weapon attack when a creature leaves your reach.");
        SetField(reactiveStrikes, "grantedAbilityIds", new List<DefinitionId> { opportunityAttack.Id });

        var fireBolt = LoadSrd5<AbilityDefinition>("AbilityDefinition_FireBolt");
        var healingWord = LoadSrd5<AbilityDefinition>("AbilityDefinition_HealingWord");
        var bless = LoadSrd5<AbilityDefinition>("AbilityDefinition_Bless");
        var command = LoadSrd5<AbilityDefinition>("AbilityDefinition_Command");
        if (fireBolt == null || healingWord == null || bless == null || command == null)
        {
            Debug.LogError("[TacticalSliceContentAuthor] One or more reused SRD5 spell assets failed to load -- aborting before saving the bundle.");
            return;
        }

        var bundle = CreateOrLoad<TacticalSliceContentBundle>(BundlePath);
        SetField(bundle, "fighterContent", fighterContent);
        SetField(bundle, "movement", movement);
        SetField(bundle, "meleeStrike", meleeStrike);
        SetField(bundle, "opportunityAttack", opportunityAttack);
        SetField(bundle, "reactiveStrikes", reactiveStrikes);
        SetField(bundle, "fireBolt", fireBolt);
        SetField(bundle, "healingWord", healingWord);
        SetField(bundle, "bless", bless);
        SetField(bundle, "command", command);
        SetField(bundle, "definitions", new List<DefinitionAsset> { movement, meleeStrike, opportunityAttack, reactiveStrikes });

        EditorUtility.SetDirty(movement);
        EditorUtility.SetDirty(meleeStrike);
        EditorUtility.SetDirty(opportunityAttack);
        EditorUtility.SetDirty(reactiveStrikes);
        EditorUtility.SetDirty(bundle);
        AssetDatabase.SaveAssets();

        Debug.Log("[TacticalSliceContentAuthor] Authored TacticalSliceContentBundle and its tactical-slice-owned content.");
    }

    private static void AuthorMeleeAbility(
        AbilityDefinition asset, string id, string displayName, string description, AbilityTag tags,
        ResourceDefinition costResource, int costAmount, FighterSampleContentBundle fighterContent,
        float prepareSeconds, float recoverSeconds)
    {
        SetField(asset, "id", id);
        SetField(asset, "displayName", displayName);
        SetField(asset, "description", description);
        SetField(asset, "tags", tags);

        // AbilityCost is a struct -- SetField must mutate one boxed instance and unbox it back at the end,
        // never a bare local (each SetField call on a struct local would box-mutate-and-discard a fresh copy,
        // silently leaving the local itself untouched). Mirrors TacticalTestFactory.CreateAbility's own
        // established fix for the identical pitfall.
        object boxedCost = new AbilityCost();
        SetField(boxedCost, "resource", costResource);
        SetField(boxedCost, "amount", costAmount);
        SetField(asset, "costs", new List<AbilityCost> { (AbilityCost)boxedCost });

        var targeting = new TargetingDefinition();
        SetField(targeting, "mode", TargetMode.SingleUnit);
        SetField(targeting, "relation", TargetRelation.Enemy);
        SetField(targeting, "minTargets", 1);
        SetField(targeting, "maxTargets", 1);
        SetField(targeting, "range", 2f); // melee reach
        SetField(targeting, "requireLineOfSight", true);
        SetField(targeting, "requireLineOfEffect", true);
        SetField(targeting, "requireVisibleTarget", true);
        SetField(asset, "targeting", targeting);

        var damage = new DamageEffect();
        SetField(damage, "hitPointResource", fighterContent.HitPoints);
        SetField(damage, "amount", new DiceExpression(1, 8, 3));

        var attack = new AttackRollEffect();
        SetField(attack, "governingStatId", fighterContent.Strength.Id);
        SetField(attack, "onHit", damage);
        SetField(asset, "effects", new List<AbilityEffect> { attack });

        SetField(asset, "prepareSeconds", prepareSeconds);
        SetField(asset, "recoverSeconds", recoverSeconds);
        SetField(asset, "animationCueId", new DefinitionId("attack-melee"));
    }

    private static T CreateOrLoad<T>(string path) where T : ScriptableObject
    {
        var existing = AssetDatabase.LoadAssetAtPath<T>(path);
        if (existing != null) return existing;

        var created = ScriptableObject.CreateInstance<T>();
        AssetDatabase.CreateAsset(created, path);
        return created;
    }

    private static T LoadSrd5<T>(string assetName) where T : Object
    {
        var asset = AssetDatabase.LoadAssetAtPath<T>($"{Srd5ContentFolder}/{assetName}.asset");
        if (asset == null) Debug.LogError($"[TacticalSliceContentAuthor] Could not load {assetName} from {Srd5ContentFolder}.");
        return asset;
    }

    private static void SetField(object target, string fieldName, object value)
    {
        var type = target.GetType();
        while (type != null)
        {
            var field = type.GetField(fieldName, BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.DeclaredOnly);
            if (field != null) { field.SetValue(target, value); return; }
            type = type.BaseType;
        }

        Debug.LogError($"[TacticalSliceContentAuthor] Field '{fieldName}' not found on {target.GetType().Name}.");
    }
}
