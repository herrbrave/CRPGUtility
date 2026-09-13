using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using CRPG.Actors;
using CRPG.Camera;
using CRPG.Core;
using CRPG.Dialogue;
using CRPG.Exploration;
using CRPG.Gameplay.Interactions;
using CRPG.Gameplay.Interactions.Adapters;
using CRPG.Gameplay.Scenes;
using CRPG.InputSystem;
using CRPG.Inventory;
using CRPG.Quests;
using CRPG.UI;
using Unity.AI.Navigation;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UIElements;

// Dogfood-only tooling -- not part of the shipped package.
//
// Task 3.6 (Gate B): authors the AdventureSlice fixture -- content assets, the three real scenes (Core +
// Overworld + Interior), and the AdventureSliceBootstrap composition root. Mirrors InteractionLabSceneAuthor's/
// CharacterLabSceneAuthor's own "rebuild from scratch and overwrite every run" approach; this is a disposable
// fixture, not shipped content.
public static class AdventureSliceSceneAuthor
{
    private const string ContentDir = "Assets/AdventureSlice";
    private const string CorePath = "Assets/Scenes/AdventureSlice_Core.unity";
    private const string OverworldPath = "Assets/Scenes/AdventureSlice_Overworld.unity";
    private const string InteriorPath = "Assets/Scenes/AdventureSlice_Interior.unity";
    private const string PanelSettingsPath = "Assets/FighterDemo/CampPanelSettings.asset";
    private const string MalePrefabPath = "Assets/Resources/NPCs/BaseHumanMaleCharacter.prefab";
    private const string FemalePrefabPath = "Assets/Resources/NPCs/BaseHumanFemaleCharacter.prefab";

    private sealed class Content
    {
        public ResourceDefinition HitPoints;
        public ItemDefinition Gold;
        public ItemDefinition Potion;
        public NpcDefinition Merchant;
        public DialogueDefinition MerchantDialogue;
        public QuestDefinition Quest;
        public PanelSettings PanelSettings;
        public GameObject MalePrefab;
        public GameObject FemalePrefab;
    }

    [MenuItem("CRPG Toolkit/Adventure Slice/Author All")]
    public static void AuthorAll()
    {
        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;

        var content = AuthorContent();
        AuthorInteriorScene(content);
        AuthorOverworldScene(content);
        AuthorCoreScene(content);

        AddScenesToBuildSettings();
        Debug.Log("[AdventureSliceSceneAuthor] Authored Core/Overworld/Interior scenes and content.");
    }

    private static Content AuthorContent()
    {
        EnsureFolder(ContentDir);
        EnsureFolder($"{ContentDir}/Dialogue");
        EnsureFolder($"{ContentDir}/Quests");

        var content = new Content
        {
            PanelSettings = AssetDatabase.LoadAssetAtPath<PanelSettings>(PanelSettingsPath),
            MalePrefab = AssetDatabase.LoadAssetAtPath<GameObject>(MalePrefabPath),
            FemalePrefab = AssetDatabase.LoadAssetAtPath<GameObject>(FemalePrefabPath),
        };

        content.HitPoints = CreateOrLoad<ResourceDefinition>($"{ContentDir}/ResourceDefinition_HitPoints.asset", d =>
        {
            SetPrivateField(d, "id", "resource-hp-adventureslice");
            SetPrivateField(d, "displayName", "Hit Points");
        });

        content.Gold = CreateOrLoad<ItemDefinition>($"{ContentDir}/ItemDefinition_Gold.asset", d =>
        {
            SetPrivateField(d, "id", "item-gold-adventureslice");
            SetPrivateField(d, "displayName", "Gold Piece");
            SetPrivateField(d, "maxStackSize", 999);
        });

        content.Potion = CreateOrLoad<ItemDefinition>($"{ContentDir}/ItemDefinition_Potion.asset", d =>
        {
            SetPrivateField(d, "id", "item-potion-adventureslice");
            SetPrivateField(d, "displayName", "Healing Potion");
            SetPrivateField(d, "baseValue", 15f);
        });

        content.Merchant = CreateOrLoad<NpcDefinition>($"{ContentDir}/NpcDefinition_Merchant.asset", d =>
        {
            SetPrivateField(d, "id", "npc-merchant-adventureslice");
            SetPrivateField(d, "displayName", "Wandering Merchant");
            SetPrivateField(d, "visualPrefab", content.FemalePrefab);
        });

        content.Quest = CreateOrLoad<QuestDefinition>($"{ContentDir}/Quests/QuestDefinition_GreetMerchant.asset", d =>
        {
            SetPrivateField(d, "id", "quest-greet-merchant");
            SetPrivateField(d, "displayName", "A Friendly Greeting");
            SetPrivateField(d, "description", "Speak with the wandering merchant in the overworld.");

            var objective = new QuestObjectiveDefinition();
            SetPrivateField(objective, "id", "obj-greet");
            SetPrivateField(objective, "displayName", "Greet the merchant");
            SetPrivateField(objective, "type", QuestObjectiveType.TalkTo);
            SetPrivateField(objective, "trackingKey", "greeted-merchant");
            SetPrivateField(objective, "targetCount", 1);
            SetPrivateField(d, "objectives", new List<QuestObjectiveDefinition> { objective });

            var reward = new QuestItemReward();
            SetPrivateField(reward, "item", content.Potion);
            SetPrivateField(reward, "quantity", 2);
            SetPrivateField(d, "rewards", new List<QuestItemReward> { reward });
        });

        content.MerchantDialogue = AuthorMerchantDialogue(content);

        return content;
    }

    private static DialogueDefinition AuthorMerchantDialogue(Content content)
    {
        var path = $"{ContentDir}/Dialogue/DialogueDefinition_MerchantGreeting.asset";
        var existing = AssetDatabase.LoadAssetAtPath<DialogueDefinition>(path);
        if (existing != null) return existing;

        var greetId = SerializableGuid.FromString("as-merchant-greet");
        var recordId = SerializableGuid.FromString("as-merchant-record");
        var choiceId = SerializableGuid.FromString("as-merchant-choice");
        var storeActionId = SerializableGuid.FromString("as-merchant-store-action");
        var farewellId = SerializableGuid.FromString("as-merchant-farewell");
        var endId = SerializableGuid.FromString("as-merchant-end");

        var greet = new LineNode();
        SetPrivateField(greet, "nodeId", greetId);
        SetPrivateField(greet, "speakerName", "Merchant");
        SetPrivateField(greet, "text", "Welcome, traveler! Fine wares for a fine adventurer.");
        SetPrivateField(greet, "nextNodeId", recordId);

        var record = new ActionNode();
        SetPrivateField(record, "nodeId", recordId);
        var recordEvent = new RecordEventAction();
        SetPrivateField(recordEvent, "key", "greeted-merchant");
        SetPrivateField(record, "action", recordEvent);
        SetPrivateField(record, "nextNodeId", choiceId);

        var browseOption = new ChoiceOption();
        SetPrivateField(browseOption, "text", "Browse your wares.");
        SetPrivateField(browseOption, "nextNodeId", storeActionId);

        var farewellOption = new ChoiceOption();
        SetPrivateField(farewellOption, "text", "Just saying hello.");
        SetPrivateField(farewellOption, "nextNodeId", farewellId);

        var choice = new ChoiceNode();
        SetPrivateField(choice, "nodeId", choiceId);
        SetPrivateField(choice, "options", new List<ChoiceOption> { browseOption, farewellOption });

        var storeAction = new ActionNode();
        SetPrivateField(storeAction, "nodeId", storeActionId);
        SetPrivateField(storeAction, "action", new OpenStoreAction());
        SetPrivateField(storeAction, "nextNodeId", endId);

        var farewell = new LineNode();
        SetPrivateField(farewell, "nodeId", farewellId);
        SetPrivateField(farewell, "speakerName", "Merchant");
        SetPrivateField(farewell, "text", "Safe travels, friend!");
        SetPrivateField(farewell, "nextNodeId", endId);

        var end = new EndNode();
        SetPrivateField(end, "nodeId", endId);

        var definition = ScriptableObject.CreateInstance<DialogueDefinition>();
        SetPrivateField(definition, "id", "dlg-merchant-greeting-adventureslice");
        SetPrivateField(definition, "displayName", "Merchant Greeting");
        SetPrivateField(definition, "startNodeId", greetId);
        SetPrivateField(definition, "nodes", new List<DialogueNode> { greet, record, choice, storeAction, farewell, end });

        AssetDatabase.CreateAsset(definition, path);
        return definition;
    }

    private static void AuthorOverworldScene(Content content)
    {
        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

        var ground = GameObject.CreatePrimitive(PrimitiveType.Plane);
        ground.name = "Ground";
        ground.transform.localScale = new Vector3(6f, 1f, 6f); // 60x60
        ColorizeGround(ground, new Color(0.35f, 0.5f, 0.3f)); // default URP Lit gray-blue reads as sky at a distance

        var light = new GameObject("Directional Light");
        light.AddComponent<Light>().type = LightType.Directional;
        light.transform.rotation = Quaternion.Euler(50f, -30f, 0f);

        var startDestGo = new GameObject("SceneTravelDestination_Start");
        startDestGo.transform.position = new Vector3(0f, 0f, -10f);
        var startDest = startDestGo.AddComponent<SceneTravelDestination>();
        SetPrivateField(startDest, "destinationId", "overworld-start");

        var toInteriorAnchorGo = new GameObject("Travel_ToInterior_Anchor");
        toInteriorAnchorGo.transform.position = new Vector3(15f, 0f, 15f) + new Vector3(0f, 0f, -1.5f);

        var (travelFixture, travelAnchor) = CreateFixtureShell("Travel_ToInterior", new Vector3(15f, 0f, 15f), new Vector3(0f, 0f, -1.5f), PrimitiveType.Cylinder);
        var travelAdapter = travelFixture.AddComponent<TravelInteractionAdapter>();
        SetPrivateField(travelAdapter, "targetScenePath", InteriorPath);
        SetPrivateField(travelAdapter, "destinationId", "interior-entry");
        FinishFixture(travelFixture, travelAnchor);

        var returnDestGo = new GameObject("SceneTravelDestination_ReturnFromInterior");
        returnDestGo.transform.position = new Vector3(15f, 0f, 15f);
        var returnDest = returnDestGo.AddComponent<SceneTravelDestination>();
        SetPrivateField(returnDest, "destinationId", "overworld-from-interior");

        var (talkFixture, talkAnchor) = CreateFixtureShell("Talk_Merchant", new Vector3(-10f, 0f, 0f), new Vector3(0f, 0f, -1.5f), PrimitiveType.Capsule);
        var talkAdapter = talkFixture.AddComponent<TalkInteractionAdapter>();
        SetPrivateField(talkAdapter, "dialogue", content.MerchantDialogue);
        SetPrivateField(talkAdapter, "panelSettings", content.PanelSettings);
        FinishFixture(talkFixture, talkAnchor);

        var (lootFixture, lootAnchor) = CreateFixtureShell("Loot_Chest", new Vector3(10f, 0f, -5f), new Vector3(0f, 0f, -1.5f), PrimitiveType.Cube);
        lootFixture.transform.localScale = new Vector3(1.2f, 1f, 1f);
        var lootAdapter = lootFixture.AddComponent<LootInteractionAdapter>();
        FinishFixture(lootFixture, lootAnchor);

        var surface = ground.AddComponent<NavMeshSurface>();
        surface.BuildNavMesh();

        var contentGo = new GameObject("LocationContent");
        var locationContent = contentGo.AddComponent<LocationContent>();
        SetPrivateField(locationContent, "spawnPoints", new List<NpcSpawnPoint>());

        var storeContentPoint = new StoreContentPoint { StoreId = "store-overworld-merchant", Adapter = talkAdapter };
        storeContentPoint.InitialListings.Add(new StoreListingSeed { Item = content.Potion, Quantity = -1 });
        SetPrivateField(locationContent, "stores", new List<StoreContentPoint> { storeContentPoint });

        var lootContentPoint = new LootContentPoint { ContainerId = "chest-overworld-1", Adapter = lootAdapter };
        lootContentPoint.InitialItems.Add(new LootItemSeed { Item = content.Gold, Quantity = 25 });
        SetPrivateField(locationContent, "lootContainers", new List<LootContentPoint> { lootContentPoint });

        SetPrivateField(locationContent, "hasCameraBounds", true);
        SetPrivateField(locationContent, "cameraBoundsXZ", new Rect(-28f, -28f, 56f, 56f));

        // talkAdapter.Party/Characters/RulesDatabase/Timeline/Inventory/DiceService/CurrencyItemId can't be
        // wired at authoring time -- no live session exists yet. AdventureSliceBootstrap.WireTalkAdapters does
        // this at Play time, for every TalkInteractionAdapter currently loaded (initial boot and after every
        // travel into a location that has one).

        EnsureFolder("Assets/Scenes");
        EditorSceneManager.SaveScene(scene, OverworldPath);
    }

    private static void AuthorInteriorScene(Content content)
    {
        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

        var ground = GameObject.CreatePrimitive(PrimitiveType.Plane);
        ground.name = "Ground";
        ground.transform.position = new Vector3(200f, 0f, 0f); // well separated from Overworld's own world space
        ground.transform.localScale = new Vector3(3f, 1f, 3f);
        ColorizeGround(ground, new Color(0.45f, 0.4f, 0.3f));

        var light = new GameObject("Directional Light");
        light.AddComponent<Light>().type = LightType.Directional;
        light.transform.rotation = Quaternion.Euler(50f, -30f, 0f);

        var entryDestGo = new GameObject("SceneTravelDestination_Entry");
        entryDestGo.transform.position = new Vector3(200f, 0f, -5f);
        var entryDest = entryDestGo.AddComponent<SceneTravelDestination>();
        SetPrivateField(entryDest, "destinationId", "interior-entry");

        var (travelFixture, travelAnchor) = CreateFixtureShell("Travel_ToOverworld", new Vector3(200f, 0f, -5f), new Vector3(0f, 0f, 1.5f), PrimitiveType.Cylinder);
        var travelAdapter = travelFixture.AddComponent<TravelInteractionAdapter>();
        SetPrivateField(travelAdapter, "targetScenePath", OverworldPath);
        SetPrivateField(travelAdapter, "destinationId", "overworld-from-interior");
        FinishFixture(travelFixture, travelAnchor);

        var surface = ground.AddComponent<NavMeshSurface>();
        surface.BuildNavMesh();

        var contentGo = new GameObject("LocationContent");
        var locationContent = contentGo.AddComponent<LocationContent>();
        SetPrivateField(locationContent, "spawnPoints", new List<NpcSpawnPoint>());
        SetPrivateField(locationContent, "stores", new List<StoreContentPoint>());
        SetPrivateField(locationContent, "lootContainers", new List<LootContentPoint>());
        SetPrivateField(locationContent, "hasCameraBounds", true);
        SetPrivateField(locationContent, "cameraBoundsXZ", new Rect(172f, -20f, 56f, 20f));

        EnsureFolder("Assets/Scenes");
        EditorSceneManager.SaveScene(scene, InteriorPath);
    }

    private static void AuthorCoreScene(Content content)
    {
        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

        // IsometricCameraRig.Apply() moves ITS OWN transform to Rig.FocusPoint every frame -- yawPivot must be
        // a child of the rig's own transform (not a sibling standalone object) for that motion to actually
        // carry the yaw/pitch/camera chain along with it.
        var rigGo = new GameObject("IsometricCameraRig");

        var cameraGo = new GameObject("Main Camera");
        cameraGo.tag = "MainCamera";
        var camera = cameraGo.AddComponent<Camera>();
        var yawPivot = new GameObject("YawPivot").transform;
        yawPivot.SetParent(rigGo.transform);
        yawPivot.localPosition = Vector3.zero;
        var pitchPivot = new GameObject("PitchPivot").transform;
        pitchPivot.SetParent(yawPivot);
        cameraGo.transform.SetParent(pitchPivot);
        cameraGo.transform.localPosition = new Vector3(0f, 0f, -12f);

        var cameraSettings = ScriptableObject.CreateInstance<CRPGCameraSettings>();
        AssetDatabase.CreateAsset(cameraSettings, $"{ContentDir}/CRPGCameraSettings_AdventureSlice.asset");

        var rig = rigGo.AddComponent<IsometricCameraRig>();
        SetPrivateField(rig, "yawPivot", yawPivot);
        SetPrivateField(rig, "pitchPivot", pitchPivot);
        SetPrivateField(rig, "cameraTransform", cameraGo.transform);
        SetPrivateField(rig, "settings", cameraSettings);

        var followGo = new GameObject("PartyCameraFollow");
        var follow = followGo.AddComponent<PartyCameraFollowBehaviour>();
        SetPrivateField(follow, "cameraRig", rig);
        SetPrivateField(follow, "settings", cameraSettings);

        var eventSystemGo = new GameObject("EventSystem");
        eventSystemGo.AddComponent<EventSystem>();
        eventSystemGo.AddComponent<UnityEngine.InputSystem.UI.InputSystemUIInputModule>();

        var inputGo = new GameObject("InputSystemCommandSource");
        var inputSource = inputGo.AddComponent<InputSystemCommandSource>();
        SetPrivateField(inputSource, "raycastCamera", camera);
        SetPrivateField(inputSource, "cameraRig", rig);

        // content.PanelSettings (loaded once, early, in AuthorContent) reliably reads back as a Unity "fake
        // null" by the time this method runs -- confirmed live: valid immediately after AssetDatabase.
        // LoadAssetAtPath in AuthorContent, but == null here, after two more NewScene()+NavMeshSurface.
        // BuildNavMesh() cycles happened in between (Interior, Overworld). Re-loading fresh right here, rather
        // than trusting a reference held across that whole multi-scene authoring pipeline, sidesteps it.
        // Task 3.6: one shared UIDocument/PanelSettings for the whole always-on HUD (see HudRoot's own doc
        // comment for why four separate full-screen roots is actively wrong here, not just redundant) --
        // Party/Inventory/QuestLog/Save each get an anchored child region off this single root instead of
        // owning a UIDocument of their own.
        var panelSettings = AssetDatabase.LoadAssetAtPath<PanelSettings>(PanelSettingsPath);
        var hudRoot = CreateUiPanel("HudRoot", panelSettings).AddComponent<HudRoot>();
        var partyView = hudRoot.gameObject.AddComponent<PartyView>();
        var inventoryView = hudRoot.gameObject.AddComponent<InventoryView>();
        var questLogView = hudRoot.gameObject.AddComponent<QuestLogView>();
        var saveView = hudRoot.gameObject.AddComponent<SaveView>();

        var bootstrapGo = new GameObject("AdventureSliceBootstrap");
        var bootstrap = bootstrapGo.AddComponent<AdventureSliceBootstrap>();
        var spawnAnchorGo = new GameObject("InitialSpawnAnchor");
        spawnAnchorGo.transform.position = new Vector3(-2f, 0f, -10f);

        SetPrivateField(bootstrap, "partyMemberPrefabs", new[] { content.MalePrefab, content.FemalePrefab, content.MalePrefab, content.FemalePrefab });
        SetPrivateField(bootstrap, "initialSpawnAnchor", spawnAnchorGo.transform);
        SetPrivateField(bootstrap, "hitPointsResource", content.HitPoints);
        SetPrivateField(bootstrap, "currencyItem", content.Gold);
        SetPrivateField(bootstrap, "otherItems", new List<ItemDefinition> { content.Potion });
        SetPrivateField(bootstrap, "npcDefinitions", new List<NpcDefinition> { content.Merchant });
        SetPrivateField(bootstrap, "dialogues", new List<DialogueDefinition> { content.MerchantDialogue });
        SetPrivateField(bootstrap, "quests", new List<QuestDefinition> { content.Quest });
        SetPrivateField(bootstrap, "startingLocationScenePath", OverworldPath);
        SetPrivateField(bootstrap, "startingDestinationId", "overworld-start");
        SetPrivateField(bootstrap, "inputSource", inputSource);
        SetPrivateField(bootstrap, "cameraRig", rig);
        SetPrivateField(bootstrap, "cameraFollow", follow);
        SetPrivateField(bootstrap, "hudRoot", hudRoot);
        SetPrivateField(bootstrap, "partyView", partyView);
        SetPrivateField(bootstrap, "inventoryView", inventoryView);
        SetPrivateField(bootstrap, "questLogView", questLogView);
        SetPrivateField(bootstrap, "saveView", saveView);

        EnsureFolder("Assets/Scenes");
        EditorSceneManager.SaveScene(scene, CorePath);
    }

    // Cosmetic only -- the default URP/Lit material's own gray-blue tint happened to closely match the
    // skybox's gradient at a distance, making the ground hard to tell apart from empty sky in a screenshot
    // (confirmed via a live eval recolor test, not guessed). Gives each ground plane its own instanced material.
    private static void ColorizeGround(GameObject ground, Color color)
    {
        var renderer = ground.GetComponent<MeshRenderer>();
        var material = new Material(renderer.sharedMaterial) { color = color };
        renderer.sharedMaterial = material;
    }

    // A direct `document.panelSettings = panelSettings;` property assignment did NOT persist through
    // EditorSceneManager.SaveScene -- confirmed live (the saved scene's YAML showed m_PanelSettings:
    // {fileID: 0} despite the in-memory assignment succeeding), which is exactly why every UIDocument-backed
    // panel silently rendered nothing AND blocked every world click (a UIDocument with no PanelSettings has no
    // real panel to pick against). Matches InteractionLabSceneAuthor's own established, proven convention for
    // authoring an asset reference onto a component: go through SerializedObject/ApplyModifiedProperties
    // explicitly rather than trusting a bare property setter to survive the save.
    private static GameObject CreateUiPanel(string name, PanelSettings panelSettings)
    {
        var go = new GameObject(name, typeof(UIDocument));
        var document = go.GetComponent<UIDocument>();
        var serialized = new SerializedObject(document);
        serialized.FindProperty("m_PanelSettings").objectReferenceValue = panelSettings;
        serialized.ApplyModifiedProperties();
        EditorUtility.SetDirty(document);
        return go;
    }

    private static (GameObject fixtureGo, Transform anchor) CreateFixtureShell(string name, Vector3 position, Vector3 anchorOffset, PrimitiveType visual)
    {
        var fixtureGo = GameObject.CreatePrimitive(visual);
        fixtureGo.name = name;
        fixtureGo.SetActive(false);
        fixtureGo.transform.position = position;

        var anchorGo = new GameObject($"{name}_InteractionAnchor");
        anchorGo.transform.SetParent(fixtureGo.transform);
        anchorGo.transform.position = position + anchorOffset;

        return (fixtureGo, anchorGo.transform);
    }

    private static void FinishFixture(GameObject fixtureGo, Transform anchor)
    {
        var interactable = fixtureGo.GetComponent<WorldInteractable>();
        interactable.Configure(anchor, 1.5f);
        EditorUtility.SetDirty(interactable);
        fixtureGo.SetActive(true);
    }

    private static T CreateOrLoad<T>(string path, System.Action<T> configure) where T : ScriptableObject
    {
        var existing = AssetDatabase.LoadAssetAtPath<T>(path);
        if (existing != null) return existing;

        var instance = ScriptableObject.CreateInstance<T>();
        configure(instance);
        AssetDatabase.CreateAsset(instance, path);
        return instance;
    }

    /// <summary>Core MUST be build-index 0 -- a standalone build boots whatever scene sits first in this list,
    /// and this project's shared scene list already had test fixture scenes (SceneTravelTestA/B) registered
    /// ahead of AdventureSlice's own scenes from earlier work in this same session. Explicitly reorders Core/
    /// Overworld/Interior to the FRONT rather than only appending them if missing, so re-running this author
    /// script is idempotent and always leaves the standalone build entry point correct, regardless of what else
    /// has been registered before or since.</summary>
    private static void AddScenesToBuildSettings()
    {
        var existing = new List<EditorBuildSettingsScene>(EditorBuildSettings.scenes);
        var adventureSlicePaths = new HashSet<string> { CorePath, OverworldPath, InteriorPath };

        var rest = existing.Where(s => !adventureSlicePaths.Contains(s.path)).ToList();
        var reordered = new List<EditorBuildSettingsScene>
        {
            new(CorePath, true),
            new(OverworldPath, true),
            new(InteriorPath, true),
        };
        reordered.AddRange(rest);

        EditorBuildSettings.scenes = reordered.ToArray();
    }

    // Walks up the type hierarchy -- required for base-class private fields (DialogueNode.nodeId etc).
    private static void SetPrivateField(object target, string fieldName, object value)
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

        Debug.LogError($"[AdventureSliceSceneAuthor] Field '{fieldName}' not found anywhere in {target.GetType()}'s hierarchy.");
    }

    private static void EnsureFolder(string path)
    {
        if (AssetDatabase.IsValidFolder(path)) return;
        var parent = System.IO.Path.GetDirectoryName(path)?.Replace('\\', '/');
        var folderName = System.IO.Path.GetFileName(path);
        if (!string.IsNullOrEmpty(parent) && !AssetDatabase.IsValidFolder(parent)) EnsureFolder(parent);
        AssetDatabase.CreateFolder(string.IsNullOrEmpty(parent) ? "Assets" : parent, folderName);
    }
}
