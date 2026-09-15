using System.Collections.Generic;
using CRPG.Core;
using UnityEditor;
using UnityEngine;

// Dogfood-only tooling -- not part of the shipped package. Task 4.5: "known unsupported rules are disabled in
// content/validator" (Docs/Design/Production-Foundation.md sec.8), demonstrated rather than only documented --
// walks the tactical-slice content bundle's own roster and fails loudly if it ever references an ability/
// condition/feature not listed in Docs/Rules/SRD5-Supported-Subset.md's allowlist table.
//
// Lives here, not as an NUnit test under crpg-toolkit/Tests/Editor/, because TacticalSliceContentBundle is a
// TestProject/Assembly-CSharp type -- the package's own test assemblies must never reference the consuming
// project's dogfood code (wrong dependency direction, same reasoning RulesetAssemblyDependencyTests already
// enforces the other way). Matches this project's established convention for pure-authoring validation
// ("validate the saved artifact... rather than inventing a failing unit test") -- a real runnable validator,
// not an invented test in the wrong assembly.
public static class TacticalSliceContentAudit
{
    private const string BundlePath = "Assets/TacticalSlice/TacticalSliceContentBundle.asset";

    // Mirrors Docs/Rules/SRD5-Supported-Subset.md's "What the Task 4.5 tactical slice's own roster actually
    // contains" table exactly -- update both together.
    private static readonly HashSet<string> AllowedAbilityIds = new()
    {
        "ability-tacticalslice-meleestrike",
        "ability-tacticalslice-opportunityattack",
        "spell-fire-bolt",
        "spell-healing-word",
        "spell-bless",
        "spell-command",
    };

    private static readonly HashSet<string> AllowedFeatureIds = new()
    {
        "feature-tacticalslice-reactivestrikes",
    };

    [MenuItem("CRPG Toolkit/Tactical Slice/Audit Content Against Supported-Subset Allowlist")]
    public static void Audit()
    {
        var bundle = AssetDatabase.LoadAssetAtPath<TacticalSliceContentBundle>(BundlePath);
        if (bundle == null)
        {
            Debug.LogError($"[TacticalSliceContentAudit] Could not load {BundlePath}.");
            return;
        }

        var offenders = new List<string>();

        CheckAbility(bundle.MeleeStrike, offenders);
        CheckAbility(bundle.OpportunityAttack, offenders);
        CheckAbility(bundle.FireBolt, offenders);
        CheckAbility(bundle.HealingWord, offenders);
        CheckAbility(bundle.Bless, offenders);
        CheckAbility(bundle.Command, offenders);
        CheckFeature(bundle.ReactiveStrikes, offenders);

        foreach (var definition in bundle.Definitions)
        {
            switch (definition)
            {
                case CRPG.Abilities.AbilityDefinition ability: CheckAbility(ability, offenders); break;
                case CRPG.Actors.FeatureDefinition feature: CheckFeature(feature, offenders); break;
            }
        }

        if (offenders.Count > 0)
        {
            Debug.LogError($"[TacticalSliceContentAudit] FAILED -- content not in Docs/Rules/SRD5-Supported-Subset.md's allowlist: {string.Join(", ", offenders)}");
        }
        else
        {
            Debug.Log("[TacticalSliceContentAudit] PASSED -- every ability/feature the bundle references is listed in the supported-subset allowlist.");
        }
    }

    private static void CheckAbility(CRPG.Abilities.AbilityDefinition ability, List<string> offenders)
    {
        if (ability == null) return;
        if (!AllowedAbilityIds.Contains(ability.Id.Value)) offenders.Add($"ability:{ability.Id.Value}");
    }

    private static void CheckFeature(CRPG.Actors.FeatureDefinition feature, List<string> offenders)
    {
        if (feature == null) return;
        if (!AllowedFeatureIds.Contains(feature.Id.Value)) offenders.Add($"feature:{feature.Id.Value}");

        foreach (var grantedId in feature.GrantedAbilityIds)
        {
            if (!AllowedAbilityIds.Contains(grantedId.Value)) offenders.Add($"granted-ability:{grantedId.Value}");
        }
    }
}
