using Unity.AI.Navigation;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

// Dogfood-only tooling (see third_party_assets / TestProject conventions) -- not part of the shipped package.
//
// Task 2.5: builds/updates the private CharacterLab.unity scene -- a flat baked-NavMesh ground, a light, and
// a CharacterLabHarness anchor -- that CharacterSetupWindow's Preview button stages a character into for
// Gate A's "inspect visuals in PlayMode" acceptance. Rebuilds from scratch and overwrites the scene file each
// run (unlike the GUID-preserving prefab authoring elsewhere in this project) -- a .unity scene has no
// cross-asset GUID references worth preserving the way a prefab does, and this scene is explicitly a
// disposable lab fixture, not shipped or referenced content.
public static class CharacterLabSceneAuthor
{
    private const string ScenePath = "Assets/Scenes/CharacterLab.unity";
    private const string CharacterLabFolder = "Assets/CharacterLab";

    [MenuItem("CRPG Toolkit/Characters/Author Character Lab Scene")]
    public static void Author()
    {
        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;

        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

        var ground = GameObject.CreatePrimitive(PrimitiveType.Plane);
        ground.name = "Ground";
        ground.transform.localScale = new Vector3(5f, 1f, 5f);

        var surface = ground.AddComponent<NavMeshSurface>();
        surface.BuildNavMesh();

        var light = new GameObject("Directional Light");
        var lightComponent = light.AddComponent<Light>();
        lightComponent.type = LightType.Directional;
        light.transform.rotation = Quaternion.Euler(50f, -30f, 0f);

        var anchor = new GameObject("PreviewAnchor");
        anchor.transform.position = Vector3.zero;

        var walkTarget = new GameObject("WalkTarget");
        walkTarget.transform.position = new Vector3(3f, 0f, 0f);

        var harnessGo = new GameObject("CharacterLabHarness");
        var harness = harnessGo.AddComponent<CharacterLabHarness>();
        var serialized = new SerializedObject(harness);
        serialized.FindProperty("anchor").objectReferenceValue = anchor.transform;
        serialized.FindProperty("walkTarget").objectReferenceValue = walkTarget.transform;
        serialized.ApplyModifiedProperties();

        var camera = new GameObject("Main Camera");
        camera.tag = "MainCamera";
        camera.AddComponent<Camera>();
        camera.transform.position = new Vector3(0f, 2.2f, -4f);
        camera.transform.rotation = Quaternion.Euler(15f, 0f, 0f);

        if (!AssetDatabase.IsValidFolder(CharacterLabFolder)) AssetDatabase.CreateFolder("Assets", "CharacterLab");
        EnsureSceneFolder();

        EditorSceneManager.SaveScene(scene, ScenePath);
        Debug.Log($"[CharacterLabSceneAuthor] Authored {ScenePath} (ground + baked NavMesh, light, camera, CharacterLabHarness).");
    }

    private static void EnsureSceneFolder()
    {
        if (!AssetDatabase.IsValidFolder("Assets/Scenes")) AssetDatabase.CreateFolder("Assets", "Scenes");
    }
}
