using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using CRPG.Abilities;
using CRPG.Actors;
using CRPG.Core;
using CRPG.Inventory;
using CRPG.Rules;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

// Dogfood-only tooling (see third_party_assets / TestProject conventions) -- not part of the shipped package.
//
// One-time content author for a minimal SRD5 "Human Fighter, levels 1-3" slice: real .asset files saved under
// the package's Rules/SRD5e2014/Content/, authored the same way any ruleset content would be (through
// DefinitionAsset fields) except there's no interactive Inspector session doing the clicking, so this script
// does it once via reflection instead. Exists to dogfood the character-creation pipeline against real content
// and to have something to visualize in the sample scene.
//
// Deliberately NOT built as part of this content: the SRD5 derived-stat math layer (ability modifiers, the AC
// formula, attack bonus, save DCs -- spec's "Derived-stat rule engine"). That's its own substantial piece of
// ruleset code; this content only uses what the generic engine can already express (flat FeatureModifier
// bonuses, resource grants, choice requirements). Two of SRD5's six Fighting Styles are authored (Defense,
// Archery) -- the ones cleanly expressible with a flat modifier; the other four need reaction abilities or
// dice-reroll effects not yet built. Champion's level 3 Improved Critical is authored as data (a CriticalRange
// modifier) even though nothing consumes it yet -- the "author ahead of the consuming system" pattern the
// architecture is meant to support.
public static class FighterSampleContentAuthor
{
    private const string ContentFolder = "Packages/com.crpgtoolkit.core/Rules/SRD5e2014/Content";

    [MenuItem("CRPG Toolkit/Content/Author Fighter Sample Content (SRD5)")]
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

        var athletics = CreateSkill("skill-athletics", "Athletics", str, all);
        var acrobatics = CreateSkill("skill-acrobatics", "Acrobatics", dex, all);
        var animalHandling = CreateSkill("skill-animal-handling", "Animal Handling", wis, all);
        var history = CreateSkill("skill-history", "History", intl, all);
        var insight = CreateSkill("skill-insight", "Insight", wis, all);
        var intimidation = CreateSkill("skill-intimidation", "Intimidation", cha, all);
        var perception = CreateSkill("skill-perception", "Perception", wis, all);
        var survival = CreateSkill("skill-survival", "Survival", wis, all);

        var hp = CreateResource("resource-hp", "Hit Points", all, RestTrigger.LongRest);
        var action = CreateResource("resource-action", "Action", all);
        var bonusAction = CreateResource("resource-bonus-action", "Bonus Action", all);
        var reaction = CreateResource("resource-reaction", "Reaction", all);
        var secondWindUses = CreateResource("resource-second-wind", "Second Wind Uses", all, RestTrigger.Both);
        var actionSurgeUses = CreateResource("resource-action-surge", "Action Surge Uses", all, RestTrigger.Both);

        var mainHandSlot = CreateEquipmentSlot("slot-mainhand", "Main Hand", all);
        var armorSlot = CreateEquipmentSlot("slot-armor", "Armor", all);
        var longsword = CreateItem("item-longsword", "Longsword", mainHandSlot.Id, all, baseValue: 15f);
        var chainShirt = CreateItem("item-chain-shirt", "Chain Shirt", armorSlot.Id, all, baseValue: 50f);
        var goldPiece = CreateItem("item-gold-piece", "Gold Piece", DefinitionId.None, all, maxStackSize: 999999);
        var healingPotion = CreateItem("item-healing-potion", "Potion of Healing", DefinitionId.None, all, baseValue: 50f, maxStackSize: 20);

        var humanTraits = CreateFeature("feature-human-traits", "Human Traits", all);
        SetField(humanTraits, "modifiers", new List<FeatureModifier>
        {
            Modifier(str.Id.Value, RuleModifierOperation.Add, 1f),
            Modifier(dex.Id.Value, RuleModifierOperation.Add, 1f),
            Modifier(con.Id.Value, RuleModifierOperation.Add, 1f),
            Modifier(intl.Id.Value, RuleModifierOperation.Add, 1f),
            Modifier(wis.Id.Value, RuleModifierOperation.Add, 1f),
            Modifier(cha.Id.Value, RuleModifierOperation.Add, 1f),
            Modifier("Speed", RuleModifierOperation.Add, 30f),
        });

        var fightingStyleDefense = CreateFeature("feature-fighting-style-defense", "Fighting Style: Defense", all);
        SetField(fightingStyleDefense, "modifiers", new List<FeatureModifier> { Modifier("ArmorClass", RuleModifierOperation.Add, 1f) });

        var fightingStyleArchery = CreateFeature("feature-fighting-style-archery", "Fighting Style: Archery", all);
        SetField(fightingStyleArchery, "modifiers", new List<FeatureModifier> { Modifier("RangedAttackBonus", RuleModifierOperation.Add, 2f) });

        var fightingStyleChoice = CreateFeature("feature-fighting-style-choice", "Fighting Style", all);
        SetField(fightingStyleChoice, "choices", new List<ChoiceRequirement>
        {
            Choice("choose-fighting-style", "Choose a Fighting Style", ChoiceType.SelectFeature, 1, fightingStyleDefense.Id, fightingStyleArchery.Id),
        });

        var secondWindAbility = CreateAbility("ability-second-wind", "Second Wind", AbilityTag.Healing, all);
        SetField(secondWindAbility, "costs", new List<AbilityCost> { Cost(bonusAction, 1), Cost(secondWindUses, 1) });
        SetField(secondWindAbility, "targeting", SelfTargeting());
        var healEffect = new HealEffect();
        SetField(healEffect, "hitPointResource", hp);
        SetField(healEffect, "amount", new DiceExpression(1, 10, 3)); // 1d10 + fighter level, fixed at 3 for this sample
        SetField(secondWindAbility, "effects", new List<AbilityEffect> { healEffect });

        var secondWindFeature = CreateFeature("feature-second-wind", "Second Wind", all);
        SetField(secondWindFeature, "grantedResources", new List<ResourceGrant> { ResourceGrantOf(secondWindUses, 1) });
        SetField(secondWindFeature, "grantedAbilityIds", new List<DefinitionId> { secondWindAbility.Id });

        var actionSurgeAbility = CreateAbility("ability-action-surge", "Action Surge", AbilityTag.Utility, all);
        SetField(actionSurgeAbility, "costs", new List<AbilityCost> { Cost(actionSurgeUses, 1) });
        SetField(actionSurgeAbility, "targeting", SelfTargeting());
        var restoreEffect = new RestoreResourceEffect();
        SetField(restoreEffect, "resource", action);
        SetField(restoreEffect, "amount", 1);
        SetField(restoreEffect, "applyToSelf", true);
        SetField(actionSurgeAbility, "effects", new List<AbilityEffect> { restoreEffect });

        var actionSurgeFeature = CreateFeature("feature-action-surge", "Action Surge", all);
        SetField(actionSurgeFeature, "grantedResources", new List<ResourceGrant> { ResourceGrantOf(actionSurgeUses, 1) });
        SetField(actionSurgeFeature, "grantedAbilityIds", new List<DefinitionId> { actionSurgeAbility.Id });

        var skillChoiceFeature = CreateFeature("feature-fighter-skill-choice", "Skill Proficiencies", all);
        SetField(skillChoiceFeature, "choices", new List<ChoiceRequirement>
        {
            Choice("choose-fighter-skills", "Choose 2 skills", ChoiceType.SelectSkill, 2,
                acrobatics.Id, animalHandling.Id, athletics.Id, history.Id, insight.Id, intimidation.Id, perception.Id, survival.Id),
        });

        var improvedCritical = CreateFeature("feature-improved-critical", "Improved Critical", all);
        SetField(improvedCritical, "modifiers", new List<FeatureModifier> { Modifier("CriticalRange", RuleModifierOperation.CriticalRange, 19f) });

        var soldierFeature = CreateFeature("feature-soldier-background", "Military Rank", all);
        SetField(soldierFeature, "grantedProficiencies", new List<DefinitionId> { athletics.Id, intimidation.Id });

        var human = CreateAsset<RaceDefinition>("race-human", "Human", all);
        SetField(human, "displayName", "Human");
        SetField(human, "grantedFeatureIds", new List<DefinitionId> { humanTraits.Id });

        var champion = CreateAsset<SubclassDefinition>("subclass-champion", "Champion", all);
        SetField(champion, "displayName", "Champion");
        SetField(champion, "levelProgression", new List<ClassLevelGrant> { LevelGrant(3, improvedCritical.Id) });

        var fighter = CreateAsset<ClassDefinition>("class-fighter", "Fighter", all);
        SetField(fighter, "displayName", "Fighter");
        SetField(fighter, "hitDie", new DiceExpression(1, 10));
        SetField(fighter, "primaryStatIds", new List<DefinitionId> { str.Id });
        SetField(fighter, "savingThrowProficiencyIds", new List<DefinitionId> { str.Id, con.Id });
        SetField(fighter, "levelProgression", new List<ClassLevelGrant>
        {
            LevelGrant(1, fightingStyleChoice.Id, secondWindFeature.Id, skillChoiceFeature.Id),
            LevelGrant(2, actionSurgeFeature.Id),
        });

        var soldier = CreateAsset<BackgroundDefinition>("background-soldier", "Soldier", all);
        SetField(soldier, "displayName", "Soldier");
        SetField(soldier, "grantedFeatureIds", new List<DefinitionId> { soldierFeature.Id });

        // No AssetDatabase.Refresh()/SaveAssets() here: either can trigger a reimport that invalidates the
        // in-memory references (human, fighter, ...) held above, silently leaving the bundle's fields null.
        // Everything is saved once, at the very end, after the bundle itself is populated.

        var bundle = ScriptableObject.CreateInstance<FighterSampleContentBundle>();
        SetField(bundle, "definitions", all);
        SetField(bundle, "race", human);
        SetField(bundle, "characterClass", fighter);
        SetField(bundle, "subclass", champion);
        SetField(bundle, "background", soldier);
        SetField(bundle, "strength", str);
        SetField(bundle, "dexterity", dex);
        SetField(bundle, "constitution", con);
        SetField(bundle, "intelligence", intl);
        SetField(bundle, "wisdom", wis);
        SetField(bundle, "charisma", cha);
        SetField(bundle, "hitPoints", hp);
        SetField(bundle, "action", action);
        SetField(bundle, "bonusAction", bonusAction);
        SetField(bundle, "reactionResource", reaction);
        SetField(bundle, "mainHandSlot", mainHandSlot);
        SetField(bundle, "armorSlot", armorSlot);
        SetField(bundle, "longsword", longsword);
        SetField(bundle, "chainShirt", chainShirt);
        SetField(bundle, "goldPiece", goldPiece);
        SetField(bundle, "healingPotion", healingPotion);

        const string bundleFolder = "Assets/FighterDemo";
        EnsureFolder(bundleFolder);
        var bundlePath = $"{bundleFolder}/FighterSampleContentBundle.asset";
        if (AssetDatabase.LoadAssetAtPath<FighterSampleContentBundle>(bundlePath) != null)
            AssetDatabase.DeleteAsset(bundlePath);
        AssetDatabase.CreateAsset(bundle, bundlePath);

        // Every definition had fields set via reflection *after* its own AssetDatabase.CreateAsset call, which
        // only snapshots the object at that moment -- later field writes never marked it dirty, so they'd be
        // silently dropped on save without this.
        foreach (var definition in all) EditorUtility.SetDirty(definition);
        EditorUtility.SetDirty(bundle);
        AssetDatabase.SaveAssets();

        Debug.Log($"[FighterSampleContentAuthor] Authored {all.Count} SRD5 definitions under {ContentFolder} and saved the demo bundle at {bundlePath}.");
    }

    [MenuItem("CRPG Toolkit/Demos/Fighter/Build Demo Scene")]
    public static void BuildScene()
    {
        const string bundlePath = "Assets/FighterDemo/FighterSampleContentBundle.asset";
        var bundle = AssetDatabase.LoadAssetAtPath<FighterSampleContentBundle>(bundlePath);
        if (bundle == null)
        {
            Debug.LogError($"[FighterSampleContentAuthor] No bundle at {bundlePath} -- run 'Author Fighter Sample Content (SRD5)' first.");
            return;
        }

        const string fbxPath =
            "Assets/ThirdPartyAssets/Universal Base Characters[Standard]/Universal Base Characters[Standard]/Base Characters/Unity/Superhero_Male_FullBody.fbx";
        var characterPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(fbxPath);
        if (characterPrefab == null)
            Debug.LogWarning($"[FighterSampleContentAuthor] Could not find character model at {fbxPath} -- scene will build without a visual.");

        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

        var ground = GameObject.CreatePrimitive(PrimitiveType.Plane);
        ground.name = "Ground";
        ground.transform.localScale = Vector3.one * 5f;

        var lightGo = new GameObject("Directional Light");
        var light = lightGo.AddComponent<Light>();
        light.type = LightType.Directional;
        light.intensity = 1.2f;
        lightGo.transform.rotation = Quaternion.Euler(50f, -30f, 0f);

        var cameraGo = new GameObject("Main Camera");
        cameraGo.tag = "MainCamera";
        cameraGo.AddComponent<Camera>();
        cameraGo.transform.position = new Vector3(0f, 1.6f, -3.5f);
        cameraGo.transform.rotation = Quaternion.Euler(10f, 0f, 0f);

        var bootstrapGo = new GameObject("FighterDemoBootstrap");
        var bootstrap = bootstrapGo.AddComponent<FighterDemoBootstrap>();
        SetField(bootstrap, "content", bundle);
        SetField(bootstrap, "level", 3);

        // Placed directly in the saved scene (not just instantiated by the bootstrap at runtime) so the
        // character is visible immediately on opening the scene, in the Scene view, without pressing Play.
        if (characterPrefab != null)
        {
            var visual = (GameObject)PrefabUtility.InstantiatePrefab(characterPrefab);
            visual.name = "Fighter (Visual)";
            visual.transform.SetParent(bootstrapGo.transform);
            visual.transform.localPosition = Vector3.zero;
        }

        const string sceneFolder = "Assets/Scenes";
        EnsureFolder(sceneFolder);
        var scenePath = $"{sceneFolder}/FighterDemo.unity";
        EditorSceneManager.SaveScene(scene, scenePath);

        Debug.Log($"[FighterSampleContentAuthor] Built demo scene at {scenePath}.");
    }

    [MenuItem("CRPG Toolkit/Demos/Fighter/Build Camp Scene")]
    public static void BuildCampDemoScene()
    {
        const string bundlePath = "Assets/FighterDemo/FighterSampleContentBundle.asset";
        var bundle = AssetDatabase.LoadAssetAtPath<FighterSampleContentBundle>(bundlePath);
        if (bundle == null)
        {
            Debug.LogError($"[FighterSampleContentAuthor] No bundle at {bundlePath} -- run 'Author Fighter Sample Content (SRD5)' first.");
            return;
        }

        // GetOrCreateDemoPanelSettings runs AFTER NewScene, not before: NewScene unloads objects not
        // referenced by any active scene/root, which silently turns an already-loaded asset reference into a
        // fake-null "missing object" even though no CreateAsset/SaveAssets ran in between. This is what broke
        // CampDemo the first time (the user had to fix panelSettings/theme live in the Editor because this
        // script produced a scene with a null UIDocument.panelSettings).
        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        var panelSettings = GetOrCreateDemoPanelSettings("Assets/FighterDemo/CampPanelSettings.asset", "Camp");

        var uiGo = new GameObject("CampDemoBootstrap", typeof(UnityEngine.UIElements.UIDocument));
        var document = uiGo.GetComponent<UnityEngine.UIElements.UIDocument>();
        document.panelSettings = panelSettings;
        var bootstrap = uiGo.AddComponent<CampDemoBootstrap>();
        SetField(bootstrap, "content", bundle);

        const string sceneFolder = "Assets/Scenes";
        EnsureFolder(sceneFolder);
        var scenePath = $"{sceneFolder}/CampDemo.unity";
        EditorSceneManager.SaveScene(scene, scenePath);

        Debug.Log($"[FighterSampleContentAuthor] Built Camp demo scene at {scenePath}.");
    }

    [MenuItem("CRPG Toolkit/Demos/Fighter/Build Store Scene")]
    public static void BuildStoreDemoScene()
    {
        const string bundlePath = "Assets/FighterDemo/FighterSampleContentBundle.asset";
        var bundle = AssetDatabase.LoadAssetAtPath<FighterSampleContentBundle>(bundlePath);
        if (bundle == null)
        {
            Debug.LogError($"[FighterSampleContentAuthor] No bundle at {bundlePath} -- run 'Author Fighter Sample Content (SRD5)' first.");
            return;
        }

        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        var panelSettings = GetOrCreateDemoPanelSettings("Assets/FighterDemo/StorePanelSettings.asset", "Store");

        var uiGo = new GameObject("StoreDemoBootstrap", typeof(UnityEngine.UIElements.UIDocument));
        var document = uiGo.GetComponent<UnityEngine.UIElements.UIDocument>();
        document.panelSettings = panelSettings;
        var bootstrap = uiGo.AddComponent<StoreDemoBootstrap>();
        SetField(bootstrap, "content", bundle);

        const string sceneFolder = "Assets/Scenes";
        EnsureFolder(sceneFolder);
        var scenePath = $"{sceneFolder}/StoreDemo.unity";
        EditorSceneManager.SaveScene(scene, scenePath);

        Debug.Log($"[FighterSampleContentAuthor] Built Store demo scene at {scenePath}.");
    }

    /// <summary>Shared by every UIDocument-based demo scene builder. A PanelSettings with no theme renders
    /// nothing at all, and CreateAsset/SaveAssets can invalidate an in-memory reference to the object just
    /// created (same class of bug as the content authoring above -- see [[project-crpg-toolkit-spec]] memory),
    /// so this always reloads from disk before handing the reference back.</summary>
    private static UnityEngine.UIElements.PanelSettings GetOrCreateDemoPanelSettings(string assetPath, string screenName)
    {
        var panelSettings = AssetDatabase.LoadAssetAtPath<UnityEngine.UIElements.PanelSettings>(assetPath);
        if (panelSettings == null)
        {
            var newPanelSettings = ScriptableObject.CreateInstance<UnityEngine.UIElements.PanelSettings>();
            EnsureFolder("Assets/FighterDemo");
            AssetDatabase.CreateAsset(newPanelSettings, assetPath);
            AssetDatabase.SaveAssets();
            panelSettings = AssetDatabase.LoadAssetAtPath<UnityEngine.UIElements.PanelSettings>(assetPath);
        }

        // ScriptableObject.CreateInstance's default DynamicAtlasSettings starts the runtime text/image atlas
        // at a tiny 64x64 (the Editor's own "Create > UI Toolkit > Panel Settings Asset" menu doesn't hit this
        // path, so it's easy to miss). Any screen whose text changes at runtime -- Store's buy/sell rows,
        // Camp's HP labels after a rest -- keeps destroying and recreating Label elements, forcing the atlas
        // to keep regrowing/repacking. Unity's atlas repack doesn't always preserve already-baked glyphs
        // correctly across a regrow, which is what produced the corrupted-looking digits in the Store screen
        // (a "4" rendering as "&") once enough Buy/Sell clicks had happened. Starting the atlas large enough
        // that a small demo UI's text never forces a regrow avoids the bug entirely, regardless of the exact
        // internal repacking issue.
        if (panelSettings.dynamicAtlasSettings.minAtlasSize < 1024)
        {
            var atlasSettings = panelSettings.dynamicAtlasSettings;
            atlasSettings.minAtlasSize = 1024;
            atlasSettings.maxSubTextureSize = 1024;
            panelSettings.dynamicAtlasSettings = atlasSettings;
            EditorUtility.SetDirty(panelSettings);
            AssetDatabase.SaveAssets();
            panelSettings = AssetDatabase.LoadAssetAtPath<UnityEngine.UIElements.PanelSettings>(assetPath);
        }

        if (panelSettings.themeStyleSheet == null)
        {
            var themePath = AssetDatabase.GUIDToAssetPath("ad4896076fe2984429be1ed03b033546");
            if (!string.IsNullOrEmpty(themePath))
            {
                panelSettings.themeStyleSheet = AssetDatabase.LoadAssetAtPath<UnityEngine.UIElements.ThemeStyleSheet>(themePath);
                EditorUtility.SetDirty(panelSettings);
                AssetDatabase.SaveAssets();
                // SaveAssets can invalidate the in-memory reference just like CreateAsset does (this exact bug
                // shipped once already -- the CampDemo scene needed a live manual fix because this reload was
                // missing). Reload again so the reference handed back is guaranteed valid regardless of which
                // branch above ran.
                panelSettings = AssetDatabase.LoadAssetAtPath<UnityEngine.UIElements.PanelSettings>(assetPath);
            }
            else
            {
                Debug.LogWarning($"[FighterSampleContentAuthor] Could not find Unity's default runtime theme -- {screenName} screen will render unstyled/invisible until a theme is assigned to {assetPath}.");
            }
        }

        return panelSettings;
    }

    [MenuItem("CRPG Toolkit/Demos/Fighter/Screenshot Demo Scene")]
    public static void CaptureScreenshot()
    {
        const string scenePath = "Assets/Scenes/FighterDemo.unity";
        EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Single);

        var camera = Camera.main;
        if (camera == null)
        {
            Debug.LogError("[FighterSampleContentAuthor] No main camera in the scene.");
            return;
        }

        const int width = 1280;
        const int height = 720;
        var renderTexture = new RenderTexture(width, height, 24);
        camera.targetTexture = renderTexture;
        var screenshot = new Texture2D(width, height, TextureFormat.RGB24, false);
        camera.Render();
        RenderTexture.active = renderTexture;
        screenshot.ReadPixels(new Rect(0, 0, width, height), 0, 0);
        screenshot.Apply();
        camera.targetTexture = null;
        RenderTexture.active = null;
        UnityEngine.Object.DestroyImmediate(renderTexture);

        var outputPath = Path.Combine(Application.dataPath, "..", "..", "TestProject_Logs", "fighter-demo-screenshot.png");
        File.WriteAllBytes(outputPath, screenshot.EncodeToPNG());
        Debug.Log($"[FighterSampleContentAuthor] Screenshot saved to {Path.GetFullPath(outputPath)}");
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

    private static EquipmentSlotDefinition CreateEquipmentSlot(string id, string displayName, List<DefinitionAsset> all)
    {
        var slot = CreateAsset<EquipmentSlotDefinition>(id, displayName, all);
        SetField(slot, "displayName", displayName);
        return slot;
    }

    private static ItemDefinition CreateItem(
        string id, string displayName, DefinitionId equipmentSlotId, List<DefinitionAsset> all, float baseValue = 0f, int maxStackSize = 1)
    {
        var item = CreateAsset<ItemDefinition>(id, displayName, all);
        SetField(item, "displayName", displayName);
        SetField(item, "equipmentSlotId", equipmentSlotId);
        SetField(item, "baseValue", baseValue);
        SetField(item, "maxStackSize", maxStackSize);
        return item;
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
