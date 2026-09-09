using System;
using System.Collections.Generic;
using System.Reflection;
using CRPG.Actors;
using CRPG.Camera;
using CRPG.Core;
using CRPG.Dialogue;
using CRPG.Inventory;
using CRPG.Navigation;
using CRPG.Quests;
using CRPG.Rules;
using Unity.AI.Navigation;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;

// Dogfood-only content author for the Oakhaven demo (see third_party_assets / TestProject conventions) -- not
// part of the shipped package. Mirrors FighterSampleContentAuthor.cs's reflection-based one-time-author
// pattern: DialogueNode/QuestObjectiveDefinition/etc. fields are private (meant to be authored through the
// Inspector or generated content), so this sets them via reflection instead of a public constructor.
//
// Per the approved Oakhaven demo plan: cave "combat" is resolved narratively (a CheckNode per encounter, no
// tactical combat -- that engine layer doesn't exist), so no monster stats/abilities are authored here at
// all; the "goblins" are purely visual set dressing placed by the scene builder. Skill-check proficiency
// doesn't yet translate into a numeric bonus anywhere in the engine (the SRD5 derived-stat layer is still
// unbuilt), so a small standalone FeatureDefinition grants flat +2 bonuses to the two skills this demo's
// checks actually use that the Fighter is already proficient in (Athletics, Intimidation) -- Persuasion is
// left unmodified on purpose, a Fighter isn't the smooth talker of the group.
public static class OakhavenContentAuthor
{
    private const string ContentFolder = "Packages/com.crpgtoolkit.core/Rules/SRD5e2014/Content/Oakhaven";
    private const string BundleFolder = "Assets/FighterDemo";
    private const string BundlePath = BundleFolder + "/OakhavenContentBundle.asset";

    [MenuItem("CRPG Toolkit/Demos/Oakhaven/Author Content")]
    public static void Author()
    {
        const string fighterBundlePath = "Assets/FighterDemo/FighterSampleContentBundle.asset";
        var fighterContent = AssetDatabase.LoadAssetAtPath<FighterSampleContentBundle>(fighterBundlePath);
        if (fighterContent == null)
        {
            Debug.LogError($"[OakhavenContentAuthor] No Fighter sample bundle at {fighterBundlePath} -- run 'Author Fighter Sample Content (SRD5)' first.");
            return;
        }

        EnsureFolder(ContentFolder);
        var all = new List<DefinitionAsset>();

        var skillPersuasion = CreateAsset<SkillDefinition>("skill-persuasion", "Persuasion", all);
        SetField(skillPersuasion, "displayName", "Persuasion");
        SetField(skillPersuasion, "governingStat", fighterContent.Charisma);

        var soldierTraining = CreateAsset<FeatureDefinition>("feature-soldier-training", "Soldier's Training", all);
        SetField(soldierTraining, "displayName", "Soldier's Training");
        SetField(soldierTraining, "modifiers", new List<FeatureModifier>
        {
            Modifier("Skill:Athletics", RuleModifierOperation.Add, 2f),
            Modifier("Skill:Intimidation", RuleModifierOperation.Add, 2f),
        });

        var ringOfProtection = CreateAsset<ItemDefinition>("item-ring-of-protection", "Ring of Protection", all);
        SetField(ringOfProtection, "displayName", "Ring of Protection");
        SetField(ringOfProtection, "description", "A family heirloom. Eldor's father wore it home from every campaign.");

        // ----- Eldor -----
        var eldor = CreateDialogue("dlg-eldor", "start", all,
            CreateBranch("start", "elian-fate-known", BranchConditionType.HasOccurred, 0, "fate-known", "greeting-gate"),
            CreateBranch("greeting-gate", "quest-lost-heir-accepted", BranchConditionType.HasOccurred, 0, "already-accepted", "greeting"),
            CreateLine("greeting", "Eldor",
                "Greetings, traveler. You arrive at a dark hour. My son, Elian, is missing. He was investigating the old well. Goblins... I fear they took him. Find him, please, and I will reward you dearly.",
                "greeting-choice"),
            CreateChoice("greeting-choice",
                CreateOption("I will find your son, Eldor.", "accept-action"),
                CreateOption("I have other matters.", "decline-line")),
            CreateAction("accept-action", CreateRecordEvent("quest-lost-heir-accepted"), "accept-line"),
            CreateLine("accept-line", "Eldor", "Thank you. The well behind my manor... that is where he was last seen. May the gods watch over you.", "end"),
            CreateLine("decline-line", "Eldor", "Then pray they do not involve the safety of your own family.", "end"),
            CreateLine("already-accepted", "Eldor", "Please, hurry back with news of my son.", "end"),
            CreateBranch("fate-known", "elian-alive", BranchConditionType.HasOccurred, 0, "joyful-line", "grief-line"),
            CreateLine("joyful-line", "Eldor", "Elian! My boy! Safe! Oh, thank the gods. Words cannot express my gratitude, traveler.", "give-ring"),
            CreateAction("give-ring", CreateGiveItem(ringOfProtection, 1), "ring-line"),
            CreateLine("ring-line", "Eldor", "Take this family heirloom -- a Ring of Protection. It saved my father; may it save you.", "end"),
            CreateLine("grief-line", "Eldor",
                "(Sobbing) The locket... it was his mother's. You promised you would find him! You failed us... Leave me. Leave Oakhaven. Your presence is a curse.",
                "grief-action"),
            CreateAction("grief-action", CreateRecordEvent("eldor-hostile"), "end"),
            CreateEnd("end"));

        // ----- Griff -----
        var griff = CreateDialogue("dlg-griff", "start", all,
            CreateBranch("start", "cave-cleared", BranchConditionType.HasOccurred, 0, "cleared-line", "not-cleared-gate"),
            CreateBranch("not-cleared-gate", "quest-goblin-menace-accepted", BranchConditionType.HasOccurred, 0, "already-accepted-line", "greeting"),
            CreateLine("greeting", "Griff",
                "Well met. If you're here about the goblins, Mayor's got a bounty. If you're here for gear, I'm low on ore. Those pests inside the caves are blocking the main vein. Clear 'em, and I'll make it worth your while.",
                "greeting-choice"),
            CreateChoice("greeting-choice",
                CreateOption("Consider the cave cleared, Griff.", "accept-action"),
                CreateOption("Not interested in mining trouble.", "decline-line"),
                CreateOption("Show me your wares.", "open-store-action")),
            CreateAction("accept-action", CreateRecordEvent("quest-goblin-menace-accepted"), "accept-line"),
            CreateLine("accept-line", "Griff", "Good. Watch yourself down there.", "end"),
            CreateLine("decline-line", "Griff", "Suit yourself. Good steel isn't cheap when supply is choked.", "end"),
            CreateLine("already-accepted-line", "Griff", "Those goblins still holed up in my vein. Clear 'em and my prices will thank you.", "already-accepted-choice"),
            CreateChoice("already-accepted-choice",
                CreateOption("Show me your wares.", "open-store-action"),
                CreateOption("Farewell.", "end")),
            CreateAction("open-store-action", CreateOpenStore(), "end"),
            CreateLine("cleared-line", "Griff", "Ore's flowing again thanks to you. Name's good with me -- take a look, prices are fair now.", "cleared-price-action"),
            CreateAction("cleared-price-action", CreateSetStorePriceMultiplier(1f, 0.5f), "cleared-choice"),
            CreateChoice("cleared-choice",
                CreateOption("Show me your wares.", "open-store-action"),
                CreateOption("Farewell.", "end")),
            CreateEnd("end"));

        // ----- Encounter 1: The Well Cavern -----
        var wellCavern = CreateDialogue("dlg-well-cavern", "start", all,
            CreateBranch("start", "well-cavern-passed", BranchConditionType.HasOccurred, 0, "already-passed", "intro"),
            CreateLine("intro", "You",
                "The well plunges into darkness. A knotted rope leads down into a damp, torch-lit cavern. Two goblin sentries pace an upper ledge above, unaware of you... for now.",
                "check"),
            CreateCheck("check", "Skill:Acrobatics", 12, CheckPerformer.BestPartyMember, "success-line", "failure-line"),
            CreateLine("success-line", "You", "You slip past the ledge unseen, the sentries none the wiser.", "pass-action"),
            CreateLine("failure-line", "You",
                "A loose stone clatters away beneath your foot. The sentries snap alert -- but by the time they scramble down, you're already past, deeper into the dark.",
                "pass-action"),
            CreateAction("pass-action", CreateRecordEvent("well-cavern-passed"), "end"),
            CreateLine("already-passed", "You", "The entrance cavern is quiet behind you now.", "end"),
            CreateEnd("end"));

        // ----- Encounter 2: The Mining Crevasse (Quest 2 target) -----
        var miningCrevasse = CreateDialogue("dlg-mining-crevasse", "start", all,
            CreateBranch("start", "cave-cleared", BranchConditionType.HasOccurred, 0, "already-cleared", "intro"),
            CreateLine("intro", "You",
                "The crevasse walls glow faintly with exposed ore. Goblin miners swarm a crude blockade, picks in hand, with a hulking Hobgoblin Overseer barking orders over the din.",
                "check"),
            CreateCheck("check", "Skill:Athletics", 14, CheckPerformer.BestPartyMember, "success-line", "failure-line"),
            CreateLine("success-line", "You",
                "You barrel through the blockade, scattering goblins and toppling the Overseer into his own tunnel. The vein is clear.", "clear-action"),
            CreateLine("failure-line", "You",
                "The Overseer rallies his miners and drives you back up the passage, bruised but unbowed. You'll need another approach.", "end"),
            CreateAction("clear-action", CreateRecordEvent("cave-cleared"), "end"),
            CreateLine("already-cleared", "You", "The crevasse sits silent, the ore vein finally unguarded.", "end"),
            CreateEnd("end"));

        // ----- Encounter 3: The Brood Chamber (Quest 1 target -- Elian's fate) -----
        var broodChamber = CreateDialogue("dlg-brood-chamber", "start", all,
            CreateBranch("start", "elian-fate-known", BranchConditionType.HasOccurred, 0, "already-known", "intro"),
            CreateLine("intro", "You",
                "Smoke thickens the air of the deepest chamber. Crude cages of bone and wood line the walls. Goblin stalkers circle a robed Witch Doctor muttering over a huddled captive -- Elian.",
                "check"),
            CreateCheck("check", "Skill:Persuasion", 15, CheckPerformer.BestPartyMember, "success-line", "failure-line"),
            CreateLine("success-line", "You",
                "Your bold defiance rattles the stalkers -- they scatter into the dark, and the Witch Doctor flees rather than face you alone. You cut Elian free. He is shaken, but alive.",
                "alive-action"),
            CreateAction("alive-action", CreateRecordEvent("elian-alive"), "fate-action"),
            CreateLine("failure-line", "You",
                "The stalkers close in before you can act. By the time you fight clear to the cage, Elian is already gone. You find only his mother's locket among the ashes.",
                "dead-action"),
            CreateAction("dead-action", CreateRecordEvent("elian-dead"), "fate-action"),
            CreateAction("fate-action", CreateRecordEvent("elian-fate-known"), "end"),
            CreateLine("already-known", "You", "The Brood Chamber lies empty and cold now.", "end"),
            CreateEnd("end"));

        // ----- Quests -----
        var questLostHeir = CreateAsset<QuestDefinition>("quest-lost-heir", "The Lost Heir", all);
        SetField(questLostHeir, "displayName", "The Lost Heir");
        SetField(questLostHeir, "description", "Eldor's son Elian went missing investigating the old well. Find him.");
        SetField(questLostHeir, "objectives", new List<QuestObjectiveDefinition>
        {
            QuestObjective("find-elian", "Learn Elian's fate", QuestObjectiveType.TalkTo, "elian-fate-known", 1),
        });

        var questGoblinMenace = CreateAsset<QuestDefinition>("quest-goblin-menace", "The Goblin Menace", all);
        SetField(questGoblinMenace, "displayName", "The Goblin Menace");
        SetField(questGoblinMenace, "description", "Griff needs the goblins cleared from his ore vein in the Mining Crevasse.");
        SetField(questGoblinMenace, "objectives", new List<QuestObjectiveDefinition>
        {
            QuestObjective("clear-cave", "Clear the Mining Crevasse", QuestObjectiveType.Kill, "cave-cleared", 1),
        });

        // No AssetDatabase.Refresh()/SaveAssets() before this point -- see FighterSampleContentAuthor.cs for
        // why (it can invalidate the in-memory references held above).
        var bundle = ScriptableObject.CreateInstance<OakhavenContentBundle>();
        SetField(bundle, "fighterContent", fighterContent);
        SetField(bundle, "definitions", all);
        SetField(bundle, "soldierTrainingFeature", soldierTraining);
        SetField(bundle, "ringOfProtection", ringOfProtection);
        SetField(bundle, "dialogueEldor", eldor);
        SetField(bundle, "dialogueGriff", griff);
        SetField(bundle, "dialogueWellCavern", wellCavern);
        SetField(bundle, "dialogueMiningCrevasse", miningCrevasse);
        SetField(bundle, "dialogueBroodChamber", broodChamber);
        SetField(bundle, "questLostHeir", questLostHeir);
        SetField(bundle, "questGoblinMenace", questGoblinMenace);

        EnsureFolder(BundleFolder);
        if (AssetDatabase.LoadAssetAtPath<OakhavenContentBundle>(BundlePath) != null)
            AssetDatabase.DeleteAsset(BundlePath);
        AssetDatabase.CreateAsset(bundle, BundlePath);

        foreach (var definition in all) EditorUtility.SetDirty(definition);
        EditorUtility.SetDirty(bundle);
        AssetDatabase.SaveAssets();

        Debug.Log($"[OakhavenContentAuthor] Authored {all.Count} Oakhaven definitions under {ContentFolder} and saved the demo bundle at {BundlePath}.");
    }

    private static DialogueDefinition CreateDialogue(string id, string startNodeId, List<DefinitionAsset> all, params DialogueNode[] nodes)
    {
        var definition = CreateAsset<DialogueDefinition>(id, id, all);
        SetField(definition, "displayName", id);
        SetField(definition, "startNodeId", startNodeId);
        SetField(definition, "nodes", new List<DialogueNode>(nodes));
        return definition;
    }

    private static LineNode CreateLine(string nodeId, string speakerName, string text, string nextNodeId)
    {
        var node = new LineNode();
        SetField(node, "nodeId", nodeId);
        SetField(node, "speakerName", speakerName);
        SetField(node, "text", text);
        SetField(node, "nextNodeId", nextNodeId);
        return node;
    }

    private static ChoiceOption CreateOption(string text, string nextNodeId)
    {
        var option = new ChoiceOption();
        SetField(option, "text", text);
        SetField(option, "nextNodeId", nextNodeId);
        return option;
    }

    private static ChoiceNode CreateChoice(string nodeId, params ChoiceOption[] options)
    {
        var node = new ChoiceNode();
        SetField(node, "nodeId", nodeId);
        SetField(node, "options", new List<ChoiceOption>(options));
        return node;
    }

    private static BranchNode CreateBranch(
        string nodeId, string timelineKey, BranchConditionType conditionType, double threshold, string trueNodeId, string falseNodeId)
    {
        var node = new BranchNode();
        SetField(node, "nodeId", nodeId);
        SetField(node, "timelineKey", timelineKey);
        SetField(node, "conditionType", conditionType);
        SetField(node, "threshold", threshold);
        SetField(node, "trueNodeId", trueNodeId);
        SetField(node, "falseNodeId", falseNodeId);
        return node;
    }

    private static CheckNode CreateCheck(
        string nodeId, string queryKey, int difficultyClass, CheckPerformer performer, string successNodeId, string failureNodeId)
    {
        var node = new CheckNode();
        SetField(node, "nodeId", nodeId);
        SetField(node, "queryKey", queryKey);
        SetField(node, "difficultyClass", difficultyClass);
        SetField(node, "performer", performer);
        SetField(node, "successNodeId", successNodeId);
        SetField(node, "failureNodeId", failureNodeId);
        return node;
    }

    private static ActionNode CreateAction(string nodeId, DialogueAction action, string nextNodeId)
    {
        var node = new ActionNode();
        SetField(node, "nodeId", nodeId);
        SetField(node, "action", action);
        SetField(node, "nextNodeId", nextNodeId);
        return node;
    }

    private static EndNode CreateEnd(string nodeId)
    {
        var node = new EndNode();
        SetField(node, "nodeId", nodeId);
        return node;
    }

    private static RecordEventAction CreateRecordEvent(string key)
    {
        var action = new RecordEventAction();
        SetField(action, "key", key);
        SetField(action, "valueKind", TimelineValueKind.Flag);
        SetField(action, "amount", 1d);
        return action;
    }

    private static GiveItemAction CreateGiveItem(ItemDefinition item, int quantity)
    {
        var action = new GiveItemAction();
        SetField(action, "item", item);
        SetField(action, "quantity", quantity);
        return action;
    }

    private static OpenStoreAction CreateOpenStore() => new();

    private static SetStorePriceMultiplierAction CreateSetStorePriceMultiplier(float buyMultiplier, float sellMultiplier)
    {
        var action = new SetStorePriceMultiplierAction();
        SetField(action, "buyPriceMultiplier", buyMultiplier);
        SetField(action, "sellPriceMultiplier", sellMultiplier);
        return action;
    }

    private static QuestObjectiveDefinition QuestObjective(string id, string displayName, QuestObjectiveType type, string trackingKey, int targetCount)
    {
        var objective = new QuestObjectiveDefinition();
        SetField(objective, "id", id);
        SetField(objective, "displayName", displayName);
        SetField(objective, "type", type);
        SetField(objective, "trackingKey", trackingKey);
        SetField(objective, "targetCount", targetCount);
        return objective;
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

    // =====================================================================================
    // Scene assembly (approved plan Phase 4): a single rough scripted blockout, per the user's
    // explicit choice over hand-placed level design. Buildings are plain primitives, not modular-kit
    // pieces -- assembling those correctly needs visual iteration this script can't do blind, and a
    // misaligned attempt would look worse than an honestly-rough colored box. Real third-party assets
    // are used only where placement is forgiving: freestanding rocks, trees, and props, and the
    // Puglin creature as static goblin set dressing (no animation clips exist for it -- see the plan).
    // =====================================================================================

    private const string ThirdPartyRoot = "Assets/ThirdPartyAssets";
    private const float CaveOffsetZ = 200f;

    [MenuItem("CRPG Toolkit/Demos/Oakhaven/Build Scene")]
    public static void BuildOakhavenDemoScene()
    {
        var bundle = AssetDatabase.LoadAssetAtPath<OakhavenContentBundle>(BundlePath);
        if (bundle == null)
        {
            Debug.LogError($"[OakhavenContentAuthor] No bundle at {BundlePath} -- run 'Author Oakhaven Demo Content' first.");
            return;
        }

        var panelSettings = AssetDatabase.LoadAssetAtPath<PanelSettings>("Assets/FighterDemo/StorePanelSettings.asset");
        if (panelSettings == null)
            Debug.LogWarning("[OakhavenContentAuthor] No StorePanelSettings.asset found -- run 'Build Store Demo Scene' at least once first, or dialogue will render unstyled/invisible.");

        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

        BuildLighting();
        var cameraRig = BuildCameraRig();
        var townGround = BuildGround("TownGround", new Vector3(0f, 0f, 0f), 6f, new Color(0.35f, 0.55f, 0.25f));
        var caveGround = BuildGround("CaveGround", new Vector3(0f, 0f, CaveOffsetZ), 6f, new Color(0.25f, 0.22f, 0.2f));

        BuildTownStructures();
        var wellPosition = new Vector3(-8f, 0f, -2f);
        BuildWell(wellPosition);

        var chamber1 = new Vector3(0f, 0f, CaveOffsetZ - 20f);
        var chamber2 = new Vector3(0f, 0f, CaveOffsetZ);
        var chamber3 = new Vector3(0f, 0f, CaveOffsetZ + 20f);
        BuildCaveChamber("Chamber1_WellCavern", chamber1, puglinCount: 2);
        BuildCaveChamber("Chamber2_MiningCrevasse", chamber2, puglinCount: 3, withPickaxes: true);
        BuildCaveChamber("Chamber3_BroodChamber", chamber3, puglinCount: 3, withCage: true, withTorches: true);

        var eldorPosition = new Vector3(-8f, 0f, 8f);
        var griffPosition = new Vector3(8f, 0f, 8f);
        var maraPosition = new Vector3(2f, 0f, 2f);
        var mayorPosition = new Vector3(8f, 0f, -8f);

        CreateNpc("Eldor", eldorPosition, isFemale: false, new Color(0.6f, 0.5f, 0.4f));
        CreateNpc("Griff", griffPosition, isFemale: false, new Color(0.4f, 0.3f, 0.25f));
        CreateNpc("Mara", maraPosition, isFemale: true, new Color(0.5f, 0.45f, 0.55f));
        CreateNpc("Mayor", mayorPosition, isFemale: false, new Color(0.3f, 0.3f, 0.5f));

        var player = CreatePlayer(Vector3.zero);
        cameraRig.transform.position = player.transform.position;

        CreateDialogueTrigger("EldorTrigger", eldorPosition + new Vector3(0f, 0f, -1.5f), bundle.DialogueEldor, panelSettings, oneShot: false);
        CreateDialogueTrigger("GriffTrigger", griffPosition + new Vector3(0f, 0f, -1.5f), bundle.DialogueGriff, panelSettings, oneShot: false);
        CreateDialogueTrigger("Chamber1Trigger", chamber1, bundle.DialogueWellCavern, panelSettings, oneShot: false);
        CreateDialogueTrigger("Chamber2Trigger", chamber2, bundle.DialogueMiningCrevasse, panelSettings, oneShot: false);
        CreateDialogueTrigger("Chamber3Trigger", chamber3, bundle.DialogueBroodChamber, panelSettings, oneShot: false);

        var caveEntranceMarker = new GameObject("CaveEntranceMarker").transform;
        caveEntranceMarker.position = chamber1;
        CreateTeleportTrigger("WellTeleport", wellPosition, caveEntranceMarker, oneShot: false);

        BuildTreeRing();

        GameObjectUtility.SetStaticEditorFlags(townGround, StaticEditorFlags.NavigationStatic);
        GameObjectUtility.SetStaticEditorFlags(caveGround, StaticEditorFlags.NavigationStatic);

        // Modern com.unity.ai.navigation baking: a NavMeshSurface component owns and persists its own baked
        // NavMeshData (the classic scene-global UnityEditor.AI.NavMeshBuilder.BuildNavMesh() left
        // NavMeshSettings.m_NavMeshData at {fileID: 0} in the saved scene -- that pipeline doesn't populate
        // anything once this package is installed). CollectObjects.All (the default) bakes from every static
        // object in the scene, not just this GameObject's children.
        var navMeshSurfaceGo = new GameObject("NavMeshSurface");
        var navMeshSurface = navMeshSurfaceGo.AddComponent<NavMeshSurface>();
        navMeshSurface.BuildNavMesh();

        var bootstrapGo = new GameObject("OakhavenDemoBootstrap");
        var bootstrap = bootstrapGo.AddComponent<OakhavenDemoBootstrap>();
        SetField(bootstrap, "content", bundle);

        const string sceneFolder = "Assets/Scenes";
        EnsureFolder(sceneFolder);
        var scenePath = $"{sceneFolder}/OakhavenDemo.unity";
        EditorSceneManager.SaveScene(scene, scenePath);

        Debug.Log($"[OakhavenContentAuthor] Built Oakhaven demo scene at {scenePath}.");
    }

    private static void BuildLighting()
    {
        var lightGo = new GameObject("Directional Light");
        var light = lightGo.AddComponent<Light>();
        light.type = LightType.Directional;
        light.intensity = 1.1f;
        lightGo.transform.rotation = Quaternion.Euler(55f, -30f, 0f);
    }

    private static IsometricCameraRig BuildCameraRig()
    {
        var root = new GameObject("CameraRoot");
        var yaw = new GameObject("YawPivot");
        yaw.transform.SetParent(root.transform, false);
        var pitch = new GameObject("PitchPivot");
        pitch.transform.SetParent(yaw.transform, false);
        var cameraGo = new GameObject("Main Camera", typeof(Camera), typeof(AudioListener)) { tag = "MainCamera" };
        cameraGo.transform.SetParent(pitch.transform, false);

        var settings = ScriptableObject.CreateInstance<CRPGCameraSettings>();
        EnsureFolder("Assets/FighterDemo");
        const string settingsPath = "Assets/FighterDemo/OakhavenCameraSettings.asset";
        if (AssetDatabase.LoadAssetAtPath<CRPGCameraSettings>(settingsPath) != null) AssetDatabase.DeleteAsset(settingsPath);
        AssetDatabase.CreateAsset(settings, settingsPath);
        AssetDatabase.SaveAssets();
        settings = AssetDatabase.LoadAssetAtPath<CRPGCameraSettings>(settingsPath);

        var rig = root.AddComponent<IsometricCameraRig>();
        SetField(rig, "yawPivot", yaw.transform);
        SetField(rig, "pitchPivot", pitch.transform);
        SetField(rig, "cameraTransform", cameraGo.transform);
        SetField(rig, "settings", settings);
        return rig;
    }

    private static GameObject BuildGround(string name, Vector3 position, float scale, Color color)
    {
        var ground = GameObject.CreatePrimitive(PrimitiveType.Plane);
        ground.name = name;
        ground.transform.position = position;
        ground.transform.localScale = Vector3.one * scale;
        Tint(ground, color);
        return ground;
    }

    private static void BuildTownStructures()
    {
        BuildBuildingBlockout("EldorsManor", new Vector3(-8f, 1f, 5f), new Vector3(4f, 2f, 4f), new Color(0.55f, 0.4f, 0.3f));
        BuildBuildingBlockout("GriffsSmithy", new Vector3(8f, 1f, 5f), new Vector3(4f, 2f, 3f), new Color(0.4f, 0.4f, 0.42f));
        BuildBuildingBlockout("SilverStaveInnTower", new Vector3(0f, 2f, 12f), new Vector3(3f, 4f, 3f), new Color(0.45f, 0.42f, 0.5f));
        BuildBuildingBlockout("MayorsOffice", new Vector3(8f, 1f, -5f), new Vector3(4f, 2f, 3f), new Color(0.5f, 0.47f, 0.35f));
    }

    private static void BuildBuildingBlockout(string name, Vector3 position, Vector3 scale, Color color)
    {
        var building = GameObject.CreatePrimitive(PrimitiveType.Cube);
        building.name = name;
        building.transform.position = position;
        building.transform.localScale = scale;
        Tint(building, color);
    }

    private static void BuildWell(Vector3 position)
    {
        var well = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        well.name = "OldTownWell";
        well.transform.position = position + new Vector3(0f, 0.5f, 0f);
        well.transform.localScale = new Vector3(1.2f, 0.5f, 1.2f);
        Tint(well, new Color(0.4f, 0.4f, 0.4f));
    }

    private static void BuildCaveChamber(string name, Vector3 center, int puglinCount, bool withPickaxes = false, bool withCage = false, bool withTorches = false)
    {
        var root = new GameObject(name);
        root.transform.position = center;

        var rockPrefabs = new[]
        {
            LoadThirdParty("Stylized Nature MegaKit[Standard]/FBX (Unity)/Rock_Medium_1.fbx"),
            LoadThirdParty("Stylized Nature MegaKit[Standard]/FBX (Unity)/Rock_Medium_2.fbx"),
            LoadThirdParty("Stylized Nature MegaKit[Standard]/FBX (Unity)/Rock_Medium_3.fbx"),
        };

        for (var i = 0; i < 8; i++)
        {
            var angle = i / 8f * Mathf.PI * 2f;
            var offset = new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)) * 6f;
            InstantiateUnder(rockPrefabs[i % rockPrefabs.Length], root.transform, center + offset);
        }

        var puglin = LoadThirdParty("Bestiary - Dungeon Monsters Kit[Standard]/Bestiary - Dungeon Monsters Kit[Standard]/Exports/FBX (Unity)/Puglin.fbx");
        for (var i = 0; i < puglinCount; i++)
        {
            var offset = new Vector3(Mathf.Cos(i * 2.4f), 0f, Mathf.Sin(i * 2.4f)) * 3f;
            InstantiateUnder(puglin, root.transform, center + offset);
        }

        if (withPickaxes)
        {
            var pickaxe = LoadThirdParty("Fantasy Props MegaKit[Standard]/Exports/FBX/Pickaxe_Bronze.fbx");
            InstantiateUnder(pickaxe, root.transform, center + new Vector3(1.5f, 0f, 1f));
        }

        if (withCage)
        {
            var cage = LoadThirdParty("Fantasy Props MegaKit[Standard]/Exports/FBX/Cage_Small.fbx");
            InstantiateUnder(cage, root.transform, center + new Vector3(-2f, 0f, 0f));
        }

        if (withTorches)
        {
            var torch = LoadThirdParty("Fantasy Props MegaKit[Standard]/Exports/FBX/Torch_Metal.fbx");
            InstantiateUnder(torch, root.transform, center + new Vector3(2f, 1f, -2f));
            InstantiateUnder(torch, root.transform, center + new Vector3(-2f, 1f, 2f));
        }
    }

    private static void BuildTreeRing()
    {
        var treePaths = new[] { "CommonTree_1", "CommonTree_2", "CommonTree_3", "CommonTree_4", "CommonTree_5" };
        var root = new GameObject("Trees");
        for (var i = 0; i < 16; i++)
        {
            var prefab = LoadThirdParty($"Stylized Nature MegaKit[Standard]/FBX (Unity)/{treePaths[i % treePaths.Length]}.fbx");
            if (prefab == null) continue;
            var angle = i / 16f * Mathf.PI * 2f;
            var position = new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)) * 25f;
            InstantiateUnder(prefab, root.transform, position);
        }
    }

    private static GameObject CreateNpc(string name, Vector3 position, bool isFemale, Color tint)
    {
        var prefabPath = isFemale
            ? "Universal Base Characters[Standard]/Universal Base Characters[Standard]/Base Characters/Unity/Superhero_Female_FullBody.fbx"
            : "Universal Base Characters[Standard]/Universal Base Characters[Standard]/Base Characters/Unity/Superhero_Male_FullBody.fbx";
        var prefab = LoadThirdParty(prefabPath);
        var npc = InstantiateUnder(prefab, null, position);
        npc.name = name;
        if (npc != null) Tint(npc, tint);
        return npc;
    }

    private static GameObject CreatePlayer(Vector3 position)
    {
        var prefab = LoadThirdParty("Universal Base Characters[Standard]/Universal Base Characters[Standard]/Base Characters/Unity/Superhero_Male_FullBody.fbx");
        var player = InstantiateUnder(prefab, null, position);
        player.name = "Player";
        player.tag = "Player";
        if (player.GetComponent<Collider>() == null) player.AddComponent<CapsuleCollider>();

        // Unity never raises OnTriggerEnter/Exit between two colliders unless at least one side has a
        // Rigidbody -- NavMeshAgent moves the transform directly and doesn't add one itself, so without this
        // every DialogueTriggerZone/TeleportTriggerZone in the scene silently never fires. Kinematic + no
        // gravity: the agent already owns movement, a dynamic Rigidbody would fight it with physics forces.
        var rigidbody = player.AddComponent<Rigidbody>();
        rigidbody.isKinematic = true;
        rigidbody.useGravity = false;

        player.AddComponent<NavMeshAgent>();
        player.AddComponent<NavMeshCharacterMotor>();
        return player;
    }

    private static void CreateDialogueTrigger(string name, Vector3 position, DialogueDefinition dialogue, PanelSettings panelSettings, bool oneShot)
    {
        var go = new GameObject(name, typeof(BoxCollider));
        go.transform.position = position;
        var box = go.GetComponent<BoxCollider>();
        box.size = new Vector3(3f, 3f, 3f);
        box.isTrigger = true;

        var trigger = go.AddComponent<DialogueTriggerZone>();
        SetField(trigger, "dialogue", dialogue);
        SetField(trigger, "oneShot", oneShot);
        SetField(trigger, "panelSettings", panelSettings);
    }

    private static void CreateTeleportTrigger(string name, Vector3 position, Transform destination, bool oneShot)
    {
        var go = new GameObject(name, typeof(BoxCollider));
        go.transform.position = position + new Vector3(0f, 0.5f, 0f);
        var box = go.GetComponent<BoxCollider>();
        box.size = new Vector3(2f, 2f, 2f);
        box.isTrigger = true;

        var teleport = go.AddComponent<TeleportTriggerZone>();
        SetField(teleport, "destination", destination);
        SetField(teleport, "oneShot", oneShot);
    }

    private static GameObject LoadThirdParty(string relativePath)
    {
        var path = $"{ThirdPartyRoot}/{relativePath}";
        var asset = AssetDatabase.LoadAssetAtPath<GameObject>(path);
        if (asset == null) Debug.LogWarning($"[OakhavenContentAuthor] Could not find third-party asset at {path}.");
        return asset;
    }

    private static GameObject InstantiateUnder(GameObject prefab, Transform parent, Vector3 position)
    {
        if (prefab == null) return null;
        var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
        if (parent != null) instance.transform.SetParent(parent, false);
        instance.transform.position = position;
        return instance;
    }

    private static void Tint(GameObject go, Color color)
    {
        var material = new Material(Shader.Find("Standard")) { color = color };
        foreach (var renderer in go.GetComponentsInChildren<Renderer>()) renderer.sharedMaterial = material;
    }
}
