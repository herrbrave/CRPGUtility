using System.Collections.Generic;
using System.Reflection;
using CRPG.InputSystem;
using CRPG.UI;
using Unity.AI.Navigation;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UIElements;

// Dogfood-only tooling -- not part of the shipped package. Task 4.5 (Gate C): builds/updates the private
// TacticalSlice.unity scene -- a ground plane, one real wall obstacle forcing a detour, baked NavMesh, a
// static top-down camera, party/enemy spawn anchors, the input rig, and the TacticalSliceBootstrap composition
// root. Rebuilds from scratch and overwrites the scene file every run, same convention as
// CharacterLabSceneAuthor/AdventureSliceSceneAuthor -- a disposable lab fixture, not shipped content.
public static class TacticalSliceSceneAuthor
{
    private const string ScenePath = "Assets/Scenes/TacticalSlice.unity";
    private const string BundlePath = "Assets/TacticalSlice/TacticalSliceContentBundle.asset";
    private const string MalePrefabPath = "Assets/Resources/NPCs/BaseHumanMaleCharacter.prefab";
    private const string FemalePrefabPath = "Assets/Resources/NPCs/BaseHumanFemaleCharacter.prefab";
    private const string PanelSettingsPath = "Assets/FighterDemo/CampPanelSettings.asset";

    [MenuItem("CRPG Toolkit/Tactical Slice/Author Scene")]
    public static void Author()
    {
        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;

        var content = AssetDatabase.LoadAssetAtPath<TacticalSliceContentBundle>(BundlePath);
        var malePrefab = AssetDatabase.LoadAssetAtPath<GameObject>(MalePrefabPath);
        var femalePrefab = AssetDatabase.LoadAssetAtPath<GameObject>(FemalePrefabPath);
        var panelSettings = AssetDatabase.LoadAssetAtPath<PanelSettings>(PanelSettingsPath);
        if (content == null || malePrefab == null || femalePrefab == null)
        {
            Debug.LogError("[TacticalSliceSceneAuthor] Missing content bundle or Humanoid prefabs -- run 'Author Content' and confirm CharacterLab prefabs exist first.");
            return;
        }

        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

        var ground = GameObject.CreatePrimitive(PrimitiveType.Plane);
        ground.name = "Ground";
        ground.transform.localScale = new Vector3(4f, 1f, 4f); // 40x40

        // One real obstacle between the two sides' starting positions -- forces a real detour, not a straight
        // line, matching Gate C's "actual navigation around an obstacle" requirement.
        var wall = GameObject.CreatePrimitive(PrimitiveType.Cube);
        wall.name = "Obstacle";
        wall.transform.position = new Vector3(0f, 1f, 0f);
        wall.transform.localScale = new Vector3(10f, 2f, 1f);

        var surface = ground.AddComponent<NavMeshSurface>();
        surface.BuildNavMesh();

        var light = new GameObject("Directional Light");
        light.AddComponent<Light>().type = LightType.Directional;
        light.transform.rotation = Quaternion.Euler(50f, -30f, 0f);

        var camera = new GameObject("Main Camera");
        camera.tag = "MainCamera";
        camera.AddComponent<Camera>();
        camera.transform.position = new Vector3(0f, 16f, -10f);
        camera.transform.rotation = Quaternion.Euler(55f, 0f, 0f);

        var eventSystemGo = new GameObject("EventSystem");
        eventSystemGo.AddComponent<UnityEngine.EventSystems.EventSystem>();
        eventSystemGo.AddComponent<UnityEngine.InputSystem.UI.InputSystemUIInputModule>();

        var inputGo = new GameObject("InputSystemCommandSource");
        var inputSource = inputGo.AddComponent<InputSystemCommandSource>();
        SetField(inputSource, "raycastCamera", camera.GetComponent<Camera>());

        var heroAnchors = CreateAnchors("HeroSpawn", new Vector3(-6f, 0f, -8f), Vector3.right * 2f, 4);
        var goblinAnchors = CreateAnchors("GoblinSpawn", new Vector3(-6f, 0f, 8f), Vector3.right * 2f, 8);

        var hudRootGo = CreateUiPanel("HudRoot", panelSettings);
        var hudRoot = hudRootGo.AddComponent<HudRoot>();
        var turnOrderView = hudRootGo.AddComponent<TurnOrderView>();
        var abilityBarView = hudRootGo.AddComponent<AbilityBarView>();

        var bootstrapGo = new GameObject("TacticalSliceBootstrap");
        var bootstrap = bootstrapGo.AddComponent<TacticalSliceBootstrap>();
        SetField(bootstrap, "content", content);
        SetField(bootstrap, "maleHumanoidPrefab", malePrefab);
        SetField(bootstrap, "femaleHumanoidPrefab", femalePrefab);
        SetField(bootstrap, "heroSpawnAnchors", heroAnchors);
        SetField(bootstrap, "goblinSpawnAnchors", goblinAnchors);
        SetField(bootstrap, "inputSource", inputSource);
        SetField(bootstrap, "hudRoot", hudRoot);
        SetField(bootstrap, "turnOrderView", turnOrderView);
        SetField(bootstrap, "abilityBarView", abilityBarView);

        EnsureFolder("Assets/Scenes");
        EditorSceneManager.SaveScene(scene, ScenePath);

        AddSceneToBuildSettings();
        Debug.Log($"[TacticalSliceSceneAuthor] Authored {ScenePath} (ground + obstacle + baked NavMesh, 4 hero + 8 goblin spawn anchors, input, HUD, TacticalSliceBootstrap).");
    }

    private static Transform[] CreateAnchors(string prefix, Vector3 start, Vector3 spacing, int count)
    {
        var anchors = new Transform[count];
        for (var i = 0; i < count; i++)
        {
            var go = new GameObject($"{prefix}_{i + 1}");
            go.transform.position = start + spacing * i;
            anchors[i] = go.transform;
        }

        return anchors;
    }

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

    private static void AddSceneToBuildSettings()
    {
        var existing = new List<EditorBuildSettingsScene>(EditorBuildSettings.scenes);
        if (existing.Exists(s => s.path == ScenePath)) return;

        existing.Add(new EditorBuildSettingsScene(ScenePath, true));
        EditorBuildSettings.scenes = existing.ToArray();
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

        Debug.LogError($"[TacticalSliceSceneAuthor] Field '{fieldName}' not found on {target.GetType().Name}.");
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
