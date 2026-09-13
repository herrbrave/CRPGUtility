using System.Collections.Generic;
using CRPG.Core;
using CRPG.Dialogue;
using CRPG.Gameplay.Interactions;
using CRPG.Gameplay.Interactions.Adapters;
using CRPG.Inventory;
using Unity.AI.Navigation;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.UIElements;

// Dogfood-only tooling (see third_party_assets / TestProject conventions) -- not part of the shipped package.
//
// Task 3.3: authors the InteractionLab scene proving the shared interaction lifecycle end to end, per the
// user's explicit choice to prove this with a NEW dedicated scene rather than migrating Oakhaven's
// DialogueTriggerZone/TeleportTriggerZone (which stay completely untouched). Mirrors CharacterLabSceneAuthor's
// own "rebuild from scratch and overwrite every run" approach -- this scene is a disposable lab fixture, not
// shipped or referenced content, so there is no cross-asset GUID worth preserving the way a prefab has.
public static class InteractionLabSceneAuthor
{
    private const string ScenePath = "Assets/Scenes/InteractionLab.unity";
    private const string CharacterPrefabPath = "Assets/Resources/NPCs/BaseHumanMaleCharacter.prefab";
    private const string SeedItemPath = "Assets/Resources/Items/ItemDefinition_PeasantBoots.asset";
    private const string DialogueAssetPath = "Assets/InteractionLab/DialogueDefinition_InteractionLabGreeting.asset";
    private const string PanelSettingsPath = "Assets/FighterDemo/CampPanelSettings.asset";

    [MenuItem("CRPG Toolkit/Interactions/Author Interaction Lab Scene")]
    public static void Author()
    {
        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;

        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

        var ground = GameObject.CreatePrimitive(PrimitiveType.Plane);
        ground.name = "Ground";
        ground.transform.localScale = new Vector3(6f, 1f, 6f);

        var light = new GameObject("Directional Light");
        light.AddComponent<Light>().type = LightType.Directional;
        light.transform.rotation = Quaternion.Euler(50f, -30f, 0f);

        var camera = new GameObject("Main Camera");
        camera.tag = "MainCamera";
        camera.AddComponent<Camera>();
        camera.transform.position = new Vector3(0f, 14f, -12f);
        camera.transform.rotation = Quaternion.Euler(45f, 0f, 0f);

        var spawnPoint = new GameObject("SpawnPoint");
        spawnPoint.transform.position = new Vector3(0f, 0f, -6f);

        var characterPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(CharacterPrefabPath);
        if (characterPrefab == null) Debug.LogError($"[InteractionLabSceneAuthor] Character prefab not found at {CharacterPrefabPath}.");

        // Spaced well apart -- the door's own corridor walls must not reach into a neighboring fixture's
        // collider (confirmed live: a tighter spacing let the door's wide walls occlude the loot chest's own
        // raycast target).
        var talkAdapter = AuthorTalkFixture(new Vector3(-12f, 0f, 0f));
        var lootAdapter = AuthorLootFixture(new Vector3(-4f, 0f, 0f));
        AuthorDoorFixture(new Vector3(4f, 0f, 0f));
        AuthorTeleportFixture(new Vector3(12f, 0f, 0f), new Vector3(12f, 0f, 10f));

        // Bake AFTER the door's wall geometry exists (static colliders affect the bake) but the door's own
        // NavMeshObstacle is deliberately NOT baked into the mesh -- it carves dynamically on top of it, which
        // is the entire point of using it instead of a plain Collider.
        var surface = ground.AddComponent<NavMeshSurface>();
        surface.BuildNavMesh();

        var seedItem = AssetDatabase.LoadAssetAtPath<ItemDefinition>(SeedItemPath);
        if (seedItem == null) Debug.LogError($"[InteractionLabSceneAuthor] Seed item not found at {SeedItemPath} -- chest will be authored empty.");

        var harnessGo = new GameObject("InteractionLabHarness");
        var harness = harnessGo.AddComponent<InteractionLabHarness>();
        var serialized = new SerializedObject(harness);
        serialized.FindProperty("characterPrefab").objectReferenceValue = characterPrefab;
        serialized.FindProperty("spawnPoint").objectReferenceValue = spawnPoint.transform;
        serialized.FindProperty("mainCamera").objectReferenceValue = camera.GetComponent<Camera>();
        serialized.FindProperty("talkAdapter").objectReferenceValue = talkAdapter;
        serialized.FindProperty("lootAdapter").objectReferenceValue = lootAdapter;
        serialized.FindProperty("seedLootItem").objectReferenceValue = seedItem;
        serialized.ApplyModifiedProperties();

        EnsureFolder("Assets/Scenes");
        EditorSceneManager.SaveScene(scene, ScenePath);
        Debug.Log($"[InteractionLabSceneAuthor] Authored {ScenePath} (Talk/Loot/Door/Teleport fixtures + InteractionLabHarness).");
    }

    /// <summary>Builds a WorldInteractable fixture shell, INACTIVE, with a real, reachable, explicitly-
    /// authored anchor placed BESIDE (never inside) the object itself -- WorldInteractable requires this, no
    /// implicit fallback. Built inactive because Awake/OnEnable fire immediately on AddComponent regardless of
    /// Edit/Play mode, and the interactable adapters below carry [RequireComponent(typeof(WorldInteractable))],
    /// which would otherwise auto-add it and run its Awake before the anchor is configured. Callers must call
    /// <see cref="FinishFixture"/> once the adapter is fully configured.</summary>
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

    private static TalkInteractionAdapter AuthorTalkFixture(Vector3 position)
    {
        var (fixtureGo, anchor) = CreateFixtureShell("Talk_NPC", position, new Vector3(0f, 0f, -1.5f), PrimitiveType.Capsule);

        var adapter = fixtureGo.AddComponent<TalkInteractionAdapter>();
        var dialogue = AuthorMinimalDialogue();
        var panelSettings = AssetDatabase.LoadAssetAtPath<PanelSettings>(PanelSettingsPath);
        if (panelSettings == null) Debug.LogError($"[InteractionLabSceneAuthor] PanelSettings not found at {PanelSettingsPath} -- dialogue UI will build but never render.");
        var serializedAdapter = new SerializedObject(adapter);
        serializedAdapter.FindProperty("dialogue").objectReferenceValue = dialogue;
        serializedAdapter.FindProperty("panelSettings").objectReferenceValue = panelSettings;
        serializedAdapter.ApplyModifiedProperties();

        FinishFixture(fixtureGo, anchor);
        return adapter;
    }

    private static LootInteractionAdapter AuthorLootFixture(Vector3 position)
    {
        var (fixtureGo, anchor) = CreateFixtureShell("Loot_Chest", position, new Vector3(0f, 0f, -1.5f), PrimitiveType.Cube);
        fixtureGo.transform.localScale = new Vector3(1.2f, 1f, 1f); // tall enough for a reliable click target

        // Not seeded here: LootContainerState is plain runtime C# state (not [SerializeField]), so anything
        // added to it at edit time is lost the moment the scene reloads for Play. InteractionLabHarness seeds
        // it at actual runtime instead, via the seedLootItem reference wired below.
        var adapter = fixtureGo.AddComponent<LootInteractionAdapter>();

        FinishFixture(fixtureGo, anchor);
        return adapter;
    }

    private static void AuthorDoorFixture(Vector3 position)
    {
        // A short corridor -- two static wall segments with a doorway-sized gap at `position` -- so opening/
        // closing the door provably changes real NavMesh traversability, not just a visual flag. Kept narrow
        // (not the full-corridor width the dedicated automated test uses) so it stays clear of neighboring
        // fixtures regardless of exact spacing.
        CreateStaticWall(position + new Vector3(-1.7f, 1f, 0f), new Vector3(1.5f, 2f, 1f));
        CreateStaticWall(position + new Vector3(1.7f, 1f, 0f), new Vector3(1.5f, 2f, 1f));

        var doorGo = new GameObject("Door");
        doorGo.SetActive(false); // see CreateFixtureShell's remarks -- same Awake-on-AddComponent concern
        doorGo.transform.position = position + new Vector3(0f, 1f, 0f);
        var obstacle = doorGo.AddComponent<NavMeshObstacle>();
        obstacle.carving = true;
        obstacle.shape = NavMeshObstacleShape.Box;
        obstacle.size = new Vector3(2.2f, 2f, 1f);

        var adapter = doorGo.AddComponent<UseDoorInteractionAdapter>();
        var serializedAdapter = new SerializedObject(adapter);
        serializedAdapter.FindProperty("obstacle").objectReferenceValue = obstacle;
        serializedAdapter.ApplyModifiedProperties();

        var anchorGo = new GameObject("Door_InteractionAnchor");
        anchorGo.transform.SetParent(doorGo.transform);
        anchorGo.transform.position = position + new Vector3(0f, 0f, -1.5f);

        FinishFixture(doorGo, anchorGo.transform);
    }

    private static void AuthorTeleportFixture(Vector3 position, Vector3 destinationPosition)
    {
        var (fixtureGo, anchor) = CreateFixtureShell("Teleport_Point", position, new Vector3(0f, 0f, -1.5f), PrimitiveType.Cylinder);
        fixtureGo.transform.localScale = new Vector3(1f, 0.5f, 1f); // tall enough for a reliable click target

        var destinationGo = new GameObject("Teleport_Destination");
        destinationGo.transform.position = destinationPosition;

        var adapter = fixtureGo.AddComponent<TeleportInteractionAdapter>();
        var serializedAdapter = new SerializedObject(adapter);
        serializedAdapter.FindProperty("destination").objectReferenceValue = destinationGo.transform;
        serializedAdapter.ApplyModifiedProperties();

        FinishFixture(fixtureGo, anchor);
    }

    private static GameObject CreateStaticWall(Vector3 center, Vector3 size)
    {
        var wall = GameObject.CreatePrimitive(PrimitiveType.Cube);
        wall.name = "Wall";
        wall.transform.position = center;
        wall.transform.localScale = size;
        wall.isStatic = true;
        return wall;
    }

    /// <summary>A single-node conversation (Line -> End) -- enough for a reviewer to see a real DialogueView
    /// open, click Continue, and see it close via the adapter's own completion path. Authored via reflection
    /// on DialogueDefinition's private fields, the same technique Tests/Editor/DialogueTestFactory.cs uses
    /// (that factory lives in the test assembly and isn't referenceable from here).</summary>
    private static DialogueDefinition AuthorMinimalDialogue()
    {
        var existing = AssetDatabase.LoadAssetAtPath<DialogueDefinition>(DialogueAssetPath);
        if (existing != null) return existing;

        var lineId = SerializableGuid.FromString("interaction-lab-line");
        var endId = SerializableGuid.FromString("interaction-lab-end");

        var line = new LineNode();
        SetPrivateField(line, "nodeId", lineId);
        SetPrivateField(line, "speakerName", "Wayfarer");
        SetPrivateField(line, "text", "Safe travels, friend -- the Interaction Lab greets you.");
        SetPrivateField(line, "nextNodeId", endId);

        var end = new EndNode();
        SetPrivateField(end, "nodeId", endId);

        var definition = ScriptableObject.CreateInstance<DialogueDefinition>();
        SetPrivateField(definition, "id", "dlg-interaction-lab-greeting");
        SetPrivateField(definition, "startNodeId", lineId);
        SetPrivateField(definition, "nodes", new List<DialogueNode> { line, end });

        EnsureFolder("Assets/InteractionLab");
        AssetDatabase.CreateAsset(definition, DialogueAssetPath);
        return definition;
    }

    // Walks up the type hierarchy -- DeclaredOnly is required for NonPublic instance fields (unlike public
    // members, GetField doesn't search base classes for them on its own), and several DialogueNode fields
    // (e.g. nodeId) are declared on the DialogueNode base class, not on LineNode/EndNode themselves.
    private static void SetPrivateField(object target, string fieldName, object value)
    {
        var type = target.GetType();
        while (type != null)
        {
            var field = type.GetField(fieldName, System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.DeclaredOnly);
            if (field != null)
            {
                field.SetValue(target, value);
                return;
            }

            type = type.BaseType;
        }

        Debug.LogError($"[InteractionLabSceneAuthor] Field '{fieldName}' not found anywhere in {target.GetType()}'s hierarchy.");
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
