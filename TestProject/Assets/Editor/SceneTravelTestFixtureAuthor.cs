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
