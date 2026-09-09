using System;
using System.Collections.Generic;
using System.Reflection;
using CRPG.Abilities;
using CRPG.Actors;
using CRPG.AI;
using CRPG.Combat;
using CRPG.Core;
using CRPG.Rules;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UIElements;

// Dogfood-only tooling (see third_party_assets / TestProject conventions) -- not part of the shipped package.
//
// Authors the small amount of content the AI-combat 2D test scene needs: one generic "Strike" attack ability
// (reusing the Fighter sample content's HitPoints/Action resources rather than duplicating them) plus two
// differently-tuned BehaviorSetDefinitions -- Heroes finish off the weakest enemy, Goblins pile onto whoever's
// nearest -- so the scene demonstrates the IAUS picking different targets for different "personalities", not
// just a single hardcoded rule. This content is test-only tuning, not real sellable SRD5 content, so unlike
// FighterSampleContentAuthor it's saved under TestProject's own Assets/AIDemo/ rather than inside the package's
// Rules/SRD5e2014/Content/.
public static class AIDemoContentAuthor
{
    private const string ContentFolder = "Assets/AIDemo/Content";
    private const string FighterBundlePath = "Assets/FighterDemo/FighterSampleContentBundle.asset";
    private const string BundlePath = "Assets/AIDemo/AIDemoContentBundle.asset";

    [MenuItem("CRPG Toolkit/Demos/AI Combat/Author Content")]
    public static void Author()
    {
        var fighterContent = AssetDatabase.LoadAssetAtPath<FighterSampleContentBundle>(FighterBundlePath);
        if (fighterContent == null)
        {
            Debug.LogError($"[AIDemoContentAuthor] No Fighter sample content at {FighterBundlePath} -- run 'Author Fighter Sample Content (SRD5)' first.");
            return;
        }

        EnsureFolder(ContentFolder);
        var all = new List<DefinitionAsset>();

        // A per-turn distance budget, restored to full every turn exactly like Action -- see TurnResourceSet
        // in AICombatDemoBootstrap. Measured in world units (meters).
        var movement = CreateAsset<ResourceDefinition>("resource-movement", "Movement", all);
        SetField(movement, "displayName", "Movement");

        // Melee range (~1.5) rather than the old field-spanning 6 -- combatants now start out of reach and
        // must actually close the distance via the Move Behavior below before their first swing lands.
        const float strikeRange = 1.5f;
        var strike = CreateAsset<AbilityDefinition>("ability-strike", "Strike", all);
        SetField(strike, "displayName", "Strike");
        SetField(strike, "tags", AbilityTag.Attack | AbilityTag.Melee);
        SetField(strike, "costs", new List<AbilityCost> { Cost(fighterContent.Action, 1) });
        SetField(strike, "targeting", MeleeTargeting(range: strikeRange));
        var damageEffect = new DamageEffect();
        SetField(damageEffect, "hitPointResource", fighterContent.HitPoints);
        SetField(damageEffect, "amount", new DiceExpression(1, 6, 2));

        // Strike is now a real attack roll (1d20 + Strength mod + proficiency vs the target's AC), not a
        // guaranteed hit -- damage only happens through AttackRollEffect's OnHit, and only for targets it
        // actually resolves as hits (see AttackRollEffect/DefaultAttackBonusRule/DefaultArmorClassRule).
        var attackRoll = new AttackRollEffect();
        SetField(attackRoll, "governingStatId", fighterContent.Strength.Id);
        SetField(attackRoll, "onHit", damageEffect);
        SetField(strike, "effects", new List<AbilityEffect> { attackRoll });

        // Shared by both sides: favors a *far* target (ascending curve) so it competes fairly against an
        // attack Behavior's *close*-favoring curve on the exact same utility score -- far away, nothing is in
        // Strike's range so Move wins by default; once within melee range, Move's score drops toward 0 while
        // Strike's rises, so attacking naturally takes over without any special-cased handoff logic. Stops a
        // little short of Strike's range so the next turn's attack is comfortably in range, not right at the
        // boundary.
        //
        // The curve's floor is Strike's own range, not 0 -- deliberately, after simulation exposed a deadlock
        // with a naive 0 floor: Finish the Weak's HP consideration is weight 1 (a full-HP target hard-vetoes to
        // score 0, harmless when Attack was the only Behavior, since 0 still wins as the only option), but once
        // Move competes on the same score, a floor of 0 left Move scoring a hair above that forced 0 even while
        // already adjacent -- heroes never attacked, goblins' HP never dropped, so the veto never broke. Tying
        // the floor to the real engagement range makes Move clamp to exactly 0 once a target is in Strike's
        // range, so a tie against Attack's 0 resolves to Attack by evaluation order (BehaviorSetDefinition lists
        // the attack Behavior first in both sets) instead of Move narrowly winning forever.
        var closeDistance = CreateAsset<BehaviorDefinition>("behavior-close-distance", "Close Distance", all);
        SetField(closeDistance, "displayName", "Close Distance");
        SetField(closeDistance, "kind", BehaviorKind.Move);
        SetField(closeDistance, "movementResourceId", movement.Id);
        SetField(closeDistance, "moveTargetRelation", TargetRelation.Enemy);
        SetField(closeDistance, "stopDistance", strikeRange - 0.3f);
        SetField(closeDistance, "considerations", new List<Consideration> {
            ConsiderationOf("Distance", AscendingCurve(strikeRange, 6f), weight: 1f),
        });

        // Prefers a low-HP% target (weight 1, primary axis) and mildly prefers a closer one as a tie-breaker
        // (weight 0.3) -- with all targets starting at full HP this also gives ties something non-random to
        // resolve on before HP percentages diverge.
        var heroAttack = CreateAsset<BehaviorDefinition>("behavior-finish-the-weak", "Finish the Weak", all);
        SetField(heroAttack, "displayName", "Finish the Weak");
        SetField(heroAttack, "abilityId", strike.Id);
        SetField(heroAttack, "considerations", new List<Consideration> {
            ConsiderationOf($"TargetResourcePercent:{fighterContent.HitPoints.Id.Value}", DescendingCurve(0f, 1f), weight: 1f),
            ConsiderationOf("Distance", DescendingCurve(0f, strikeRange), weight: 0.3f),
        });

        var heroBehaviorSet = CreateAsset<BehaviorSetDefinition>("behaviorset-heroes", "Heroes", all);
        SetField(heroBehaviorSet, "displayName", "Heroes");
        SetField(heroBehaviorSet, "behaviorIds", new List<DefinitionId> { heroAttack.Id, closeDistance.Id });

        // Inverse priority from the heroes: prefers whoever's nearest (weight 1, primary axis) and mildly
        // prefers a weaker target as a tie-breaker (weight 0.3) -- a visibly different "personality" scoring
        // the exact same ability/targets, to show the IAUS driving distinct behavior from tuning alone.
        var goblinAttack = CreateAsset<BehaviorDefinition>("behavior-attack-nearest", "Attack Nearest", all);
        SetField(goblinAttack, "displayName", "Attack Nearest");
        SetField(goblinAttack, "abilityId", strike.Id);
        SetField(goblinAttack, "considerations", new List<Consideration> {
            ConsiderationOf("Distance", DescendingCurve(0f, strikeRange), weight: 1f),
            ConsiderationOf($"TargetResourcePercent:{fighterContent.HitPoints.Id.Value}", DescendingCurve(0f, 1f), weight: 0.3f),
        });

        var goblinBehaviorSet = CreateAsset<BehaviorSetDefinition>("behaviorset-goblins", "Goblins", all);
        SetField(goblinBehaviorSet, "displayName", "Goblins");
        SetField(goblinBehaviorSet, "behaviorIds", new List<DefinitionId> { goblinAttack.Id, closeDistance.Id });

        // Real ConditionDefinition content (not test-only synthetic fixtures) proving Tier 1's "AttacksAgainst"/
        // "SavingThrow:<ability>" advantage-gathering actually works end to end. Unconscious is the one the demo
        // actively applies (via DeathSaveService, triggered automatically on dropping to 0 HP); Prone/Restrained
        // are authored alongside it to demonstrate the broader capability but aren't granted by anything in this
        // demo's single-ability (Strike-only) roster yet.
        var unconscious = CreateAsset<ConditionDefinition>("condition-unconscious", "Unconscious", all);
        SetField(unconscious, "displayName", "Unconscious");
        SetField(unconscious, "modifiers", new List<FeatureModifier> {
            ModifierOf(DefaultAttackBonusRule.AttacksAgainstQueryKey, RuleModifierOperation.Advantage, 0f),
        });

        var prone = CreateAsset<ConditionDefinition>("condition-prone", "Prone", all);
        SetField(prone, "displayName", "Prone");
        SetField(prone, "modifiers", new List<FeatureModifier> {
            ModifierOf(DefaultAttackBonusRule.AttacksAgainstQueryKey, RuleModifierOperation.Advantage, 0f),
        });

        var restrained = CreateAsset<ConditionDefinition>("condition-restrained", "Restrained", all);
        SetField(restrained, "displayName", "Restrained");
        SetField(restrained, "modifiers", new List<FeatureModifier> {
            ModifierOf(DefaultAttackBonusRule.AttacksAgainstQueryKey, RuleModifierOperation.Advantage, 0f),
            ModifierOf(DefaultSavingThrowRule.QueryKeyPrefix + fighterContent.Dexterity.Id.Value, RuleModifierOperation.Disadvantage, 0f),
        });

        // No AssetDatabase.Refresh()/SaveAssets() here: either can trigger a reimport that invalidates the
        // in-memory references held above, silently leaving the bundle's fields null (same gotcha
        // FighterSampleContentAuthor documents). Everything is saved once, at the very end.

        var bundle = ScriptableObject.CreateInstance<AIDemoContentBundle>();
        SetField(bundle, "fighterContent", fighterContent);
        SetField(bundle, "definitions", all);
        SetField(bundle, "movement", movement);
        SetField(bundle, "strikeAbility", strike);
        SetField(bundle, "heroAttackBehavior", heroAttack);
        SetField(bundle, "goblinAttackBehavior", goblinAttack);
        SetField(bundle, "closeDistanceBehavior", closeDistance);
        SetField(bundle, "heroBehaviorSet", heroBehaviorSet);
        SetField(bundle, "goblinBehaviorSet", goblinBehaviorSet);
        SetField(bundle, "unconsciousCondition", unconscious);
        SetField(bundle, "proneCondition", prone);
        SetField(bundle, "restrainedCondition", restrained);

        if (AssetDatabase.LoadAssetAtPath<AIDemoContentBundle>(BundlePath) != null) AssetDatabase.DeleteAsset(BundlePath);
        AssetDatabase.CreateAsset(bundle, BundlePath);

        foreach (var definition in all) EditorUtility.SetDirty(definition);
        EditorUtility.SetDirty(bundle);
        AssetDatabase.SaveAssets();

        Debug.Log($"[AIDemoContentAuthor] Authored {all.Count} definitions under {ContentFolder} and saved the demo bundle at {BundlePath}.");
    }

    [MenuItem("CRPG Toolkit/Demos/AI Combat/Build Scene")]
    public static void BuildScene()
    {
        var bundle = AssetDatabase.LoadAssetAtPath<AIDemoContentBundle>(BundlePath);
        if (bundle == null)
        {
            Debug.LogError($"[AIDemoContentAuthor] No bundle at {BundlePath} -- run 'Author AI Combat Demo Content' first.");
            return;
        }

        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

        var cameraGo = new GameObject("Main Camera");
        cameraGo.tag = "MainCamera";
        var camera = cameraGo.AddComponent<Camera>();
        camera.orthographic = true;
        camera.orthographicSize = 4.5f;
        camera.backgroundColor = new Color(0.08f, 0.08f, 0.1f);
        camera.clearFlags = CameraClearFlags.SolidColor;
        cameraGo.transform.position = new Vector3(0f, 0f, -10f);

        // GetOrCreateDemoPanelSettings runs AFTER NewScene, not before -- NewScene unloads objects not
        // referenced by any active scene/root, which can silently turn an already-loaded asset reference into
        // a fake-null "missing object" (the CampDemo bug FighterSampleContentAuthor's version of this helper
        // documents in full).
        var panelSettings = GetOrCreateDemoPanelSettings("Assets/AIDemo/AICombatPanelSettings.asset", "AI Combat");

        var uiGo = new GameObject("AICombatDemoBootstrap", typeof(UIDocument));
        var document = uiGo.GetComponent<UIDocument>();
        document.panelSettings = panelSettings;
        var bootstrap = uiGo.AddComponent<AICombatDemoBootstrap>();
        SetField(bootstrap, "content", bundle);

        const string sceneFolder = "Assets/Scenes";
        EnsureFolder(sceneFolder);
        var scenePath = $"{sceneFolder}/AICombatDemo.unity";
        EditorSceneManager.SaveScene(scene, scenePath);

        Debug.Log($"[AIDemoContentAuthor] Built AI combat demo scene at {scenePath}.");
    }

    /// <summary>Same shared shape as FighterSampleContentAuthor's version -- a PanelSettings with no theme
    /// renders nothing at all, and CreateAsset/SaveAssets can invalidate an in-memory reference to the object
    /// just created, so this always reloads from disk before handing the reference back.</summary>
    private static PanelSettings GetOrCreateDemoPanelSettings(string assetPath, string screenName)
    {
        var panelSettings = AssetDatabase.LoadAssetAtPath<PanelSettings>(assetPath);
        if (panelSettings == null)
        {
            var newPanelSettings = ScriptableObject.CreateInstance<PanelSettings>();
            EnsureFolder("Assets/AIDemo");
            AssetDatabase.CreateAsset(newPanelSettings, assetPath);
            AssetDatabase.SaveAssets();
            panelSettings = AssetDatabase.LoadAssetAtPath<PanelSettings>(assetPath);
        }

        if (panelSettings.dynamicAtlasSettings.minAtlasSize < 1024)
        {
            var atlasSettings = panelSettings.dynamicAtlasSettings;
            atlasSettings.minAtlasSize = 1024;
            atlasSettings.maxSubTextureSize = 1024;
            panelSettings.dynamicAtlasSettings = atlasSettings;
            EditorUtility.SetDirty(panelSettings);
            AssetDatabase.SaveAssets();
            panelSettings = AssetDatabase.LoadAssetAtPath<PanelSettings>(assetPath);
        }

        if (panelSettings.themeStyleSheet == null)
        {
            var themePath = AssetDatabase.GUIDToAssetPath("ad4896076fe2984429be1ed03b033546");
            if (!string.IsNullOrEmpty(themePath))
            {
                panelSettings.themeStyleSheet = AssetDatabase.LoadAssetAtPath<ThemeStyleSheet>(themePath);
                EditorUtility.SetDirty(panelSettings);
                AssetDatabase.SaveAssets();
                panelSettings = AssetDatabase.LoadAssetAtPath<PanelSettings>(assetPath);
            }
            else
            {
                Debug.LogWarning($"[AIDemoContentAuthor] Could not find Unity's default runtime theme -- {screenName} screen will render unstyled/invisible until a theme is assigned to {assetPath}.");
            }
        }

        return panelSettings;
    }

    private static Consideration ConsiderationOf(string inputKey, AnimationCurve curve, float weight)
    {
        var consideration = new Consideration();
        SetField(consideration, "inputKey", inputKey);
        SetField(consideration, "curve", curve);
        SetField(consideration, "weight", weight);
        return consideration;
    }

    private static AnimationCurve DescendingCurve(float from, float to) =>
        AnimationCurve.Linear(from, 1f, to, 0f);

    private static AnimationCurve AscendingCurve(float from, float to) =>
        AnimationCurve.Linear(from, 0f, to, 1f);

    private static FeatureModifier ModifierOf(string queryKey, RuleModifierOperation operation, float value)
    {
        object boxed = new FeatureModifier();
        SetField(boxed, "queryKey", queryKey);
        SetField(boxed, "operation", operation);
        SetField(boxed, "value", value);
        SetField(boxed, "priority", 0);
        return (FeatureModifier)boxed;
    }

    private static AbilityCost Cost(ResourceDefinition resource, int amount)
    {
        object boxed = new AbilityCost();
        SetField(boxed, "resource", resource);
        SetField(boxed, "amount", amount);
        return (AbilityCost)boxed;
    }

    private static TargetingDefinition MeleeTargeting(float range)
    {
        var targeting = new TargetingDefinition();
        SetField(targeting, "mode", TargetMode.SingleUnit);
        SetField(targeting, "relation", TargetRelation.Enemy);
        SetField(targeting, "minTargets", 1);
        SetField(targeting, "maxTargets", 1);
        SetField(targeting, "range", range);
        return targeting;
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
