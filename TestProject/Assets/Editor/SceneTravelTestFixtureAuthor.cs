using CRPG.Gameplay.Scenes;
using Unity.AI.Navigation;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

// Dogfood-only tooling -- not part of the shipped package.
//
// Task 3.6: authors two minimal, content-free real scenes SceneTravelTests loads via SceneManager.LoadSceneAsync
// to exercise the actual scene-loading/placement-validation/warp/rollback machinery -- content preparation
// (NPCs/stores/loot) is already covered separately and more cheaply by the EditMode LocationStatePersistenceTests,
// so these scenes carry NO ILocationContent, only ground+NavMesh+SceneTravelDestination markers. Placed at
// widely separated world positions (each fixture's own origin, 2000 units apart) so their independently baked
// NavMeshes can never spatially overlap regardless of which combination is additively loaded at once during a
// test run.
public static class SceneTravelTestFixtureAuthor
{
    private const string SceneAPath = "Assets/Scenes/Tests/SceneTravelTestA.unity";
    private const string SceneBPath = "Assets/Scenes/Tests/SceneTravelTestB.unity";

    [MenuItem("CRPG Toolkit/Scenes/Author Scene Travel Test Fixtures")]
    public static void Author()
    {
        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;

        AuthorScene(SceneAPath, new Vector3(0f, 0f, 0f), "dest-a");
        AuthorScene(SceneBPath, new Vector3(2000f, 0f, 0f), "dest-b");

        Debug.Log($"[SceneTravelTestFixtureAuthor] Authored {SceneAPath} and {SceneBPath}.");
    }

    // 2D campaign Task 1.5: two more content-free scenes for the GRID movement backend. Each carries a
    // WalkabilityGridProvider (16x8 cells) and a SceneTravelDestination. A is fully open; B is split by a solid wall
    // down x = 8, so "can I path from the left half to the right half" differs between the two -- exactly what a
    // scene-travel round trip must prove is re-pointed with no stale state. Separate from Author() so the committed
    // NavMesh fixtures above are never regenerated.
    private const string GridSceneAPath = "Assets/Scenes/Tests/GridTravelTestA.unity";
    private const string GridSceneBPath = "Assets/Scenes/Tests/GridTravelTestB.unity";

    [MenuItem("CRPG Toolkit/Scenes/Author Grid Travel Test Fixtures")]
    public static void AuthorGridFixtures()
    {
        AuthorGridScene(GridSceneAPath, new Vector3(0f, 0f, 5000f), "grid-dest-a", solidWallAtX8: false);
        AuthorGridScene(GridSceneBPath, new Vector3(2000f, 0f, 5000f), "grid-dest-b", solidWallAtX8: true);
        EnsureInBuildSettings(GridSceneAPath);
        EnsureInBuildSettings(GridSceneBPath);
        AssetDatabase.SaveAssets();
        Debug.Log($"[SceneTravelTestFixtureAuthor] Authored {GridSceneAPath} and {GridSceneBPath}.");
    }

    private static void AuthorGridScene(string path, Vector3 origin, string destinationId, bool solidWallAtX8)
    {
        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

        var blocked = new System.Collections.Generic.List<CRPG.Navigation.Grid.GridCoordinate>();
        if (solidWallAtX8)
        {
            for (var z = 0; z < 8; z++) blocked.Add(new CRPG.Navigation.Grid.GridCoordinate(8, z));
        }

        var providerGo = new GameObject("WalkabilityGridProvider");
        var provider = providerGo.AddComponent<CRPG.Navigation.Grid.WalkabilityGridProvider>();
        provider.Configure(origin, 1f, 16, 8, blocked);
        EditorUtility.SetDirty(provider);

        var destinationGo = new GameObject($"SceneTravelDestination_{destinationId}");
        destinationGo.transform.position = origin + new Vector3(3.5f, 0f, 3.5f); // cell (3,3), left of the wall
        destinationGo.transform.rotation = Quaternion.identity;
        var destination = destinationGo.AddComponent<SceneTravelDestination>();
        var serialized = new SerializedObject(destination);
        serialized.FindProperty("destinationId").stringValue = destinationId;
        serialized.ApplyModifiedProperties();

        EnsureFolder("Assets/Scenes/Tests");
        EditorSceneManager.SaveScene(scene, path);
    }

    private static void EnsureInBuildSettings(string path)
    {
        var scenes = new System.Collections.Generic.List<EditorBuildSettingsScene>(EditorBuildSettings.scenes);
        if (scenes.Exists(s => s.path == path)) return;

        scenes.Add(new EditorBuildSettingsScene(path, true));
        EditorBuildSettings.scenes = scenes.ToArray();
    }

    private static void AuthorScene(string path, Vector3 origin, string destinationId)
    {
        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

        var ground = GameObject.CreatePrimitive(PrimitiveType.Plane);
        ground.name = "Ground";
        ground.transform.position = origin;
        ground.transform.localScale = new Vector3(4f, 1f, 4f);

        var light = new GameObject("Directional Light");
        light.AddComponent<Light>().type = LightType.Directional;
        light.transform.rotation = Quaternion.Euler(50f, -30f, 0f);

        var destinationGo = new GameObject($"SceneTravelDestination_{destinationId}");
        destinationGo.transform.position = origin;
        destinationGo.transform.rotation = Quaternion.identity;
        var destination = destinationGo.AddComponent<SceneTravelDestination>();
        var serialized = new SerializedObject(destination);
        serialized.FindProperty("destinationId").stringValue = destinationId;
        serialized.ApplyModifiedProperties();

        var surface = ground.AddComponent<NavMeshSurface>();
        surface.BuildNavMesh();

        EnsureFolder("Assets/Scenes/Tests");
        EditorSceneManager.SaveScene(scene, path);
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
