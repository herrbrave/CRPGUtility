using CRPG.InputSystem;
using CRPG.Rules.SRD5;
using Unity.AI.Navigation;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UIElements;

namespace CRPG.Samples.CharacterCreator.Editor
{
    /// <summary>
    /// Authors the CharacterCreator sample's PanelSettings content and its one real scene. Run "1. Author
    /// Character Prefab" first. Mirrors BasicCrpgSceneAuthor's own "rebuild from scratch, load-or-create
    /// assets, reload-by-path-after-NewScene" conventions.
    /// </summary>
    internal static class CharacterCreatorSceneAuthor
    {
        private const string ContentFolder = "Packages/com.crpgtoolkit.core/Samples/CharacterCreator/Content";
        private const string PrefabFolder = "Packages/com.crpgtoolkit.core/Samples/CharacterCreator/Content/Prefabs";
        private const string ScenesFolder = "Packages/com.crpgtoolkit.core/Samples/CharacterCreator/Scenes";
        private const string ScenePath = "Packages/com.crpgtoolkit.core/Samples/CharacterCreator/Scenes/CharacterCreator.unity";
        private const string PanelSettingsPath = "Packages/com.crpgtoolkit.core/Samples/CharacterCreator/Content/character-creator-panel-settings.asset";

        // The package's own real, already-shipped ruleset provider -- referenced directly, same "real
        // consumer" precedent as Task 5.2's Fate ruleset. No new SRD5 content authoring for this sample.
        private const string RulesetProviderPath = "Packages/com.crpgtoolkit.core/Rules/SRD5e2014/Content/SRD5RulesetProvider.asset";

        [MenuItem("CRPG Toolkit/Samples/Character Creator/2. Author Scene And Content")]
        public static void Author()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;

            AuthorContent();
            AssetDatabase.SaveAssets();
            AuthorScene();

            Debug.Log($"[CharacterCreatorSceneAuthor] Authored {ScenePath} and its content.");
        }

        private static void AuthorContent()
        {
            EnsureFolder(ContentFolder);

            if (AssetDatabase.LoadAssetAtPath<PanelSettings>(PanelSettingsPath) != null) return;
            var settings = ScriptableObject.CreateInstance<PanelSettings>();
            AssetDatabase.CreateAsset(settings, PanelSettingsPath);
        }

        private static void AuthorScene()
        {
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            var ground = GameObject.CreatePrimitive(PrimitiveType.Plane);
            ground.name = "Ground";
            ground.transform.localScale = new Vector3(4f, 1f, 4f); // 40x40
            var groundMaterial = new Material(Shader.Find("Universal Render Pipeline/Lit")) { color = new Color(0.4f, 0.5f, 0.4f) };
            ground.GetComponent<MeshRenderer>().sharedMaterial = groundMaterial;

            var light = new GameObject("Directional Light").AddComponent<Light>();
            light.type = LightType.Directional;
            light.transform.rotation = Quaternion.Euler(50f, -30f, 0f);

            var cameraGo = new GameObject("Main Camera");
            cameraGo.tag = "MainCamera";
            var camera = cameraGo.AddComponent<Camera>();
            cameraGo.transform.position = new Vector3(0f, 14f, -11f);
            cameraGo.transform.rotation = Quaternion.Euler(55f, 0f, 0f);

            var spawnAnchorGo = new GameObject("InitialSpawnAnchor");
            spawnAnchorGo.transform.position = new Vector3(0f, 0f, 0f);

            var surface = ground.AddComponent<NavMeshSurface>();
            surface.BuildNavMesh();

            var inputGo = new GameObject("InputSystemCommandSource");
            var inputSource = inputGo.AddComponent<InputSystemCommandSource>();
            var serializedInput = new SerializedObject(inputSource);
            serializedInput.FindProperty("raycastCamera").objectReferenceValue = camera;
            serializedInput.FindProperty("hittableMask").intValue = ~0;
            serializedInput.ApplyModifiedProperties();

            // Reloaded fresh by path (not created moments ago in this same method) since a ScriptableObject
            // reference can read back as a Unity "fake null" once EditorSceneManager.NewScene's domain-reset-
            // adjacent unload happens -- the same class of bug BasicCrpgSceneAuthor's own report documents.
            var panelSettings = AssetDatabase.LoadAssetAtPath<PanelSettings>(PanelSettingsPath);
            var uiGo = new GameObject("CharacterCreatorView");
            var document = uiGo.AddComponent<UIDocument>();
            document.panelSettings = panelSettings;
            var view = uiGo.AddComponent<CharacterCreatorView>();

            var hero = AssetDatabase.LoadAssetAtPath<GameObject>($"{PrefabFolder}/Hero.prefab");
            if (hero == null)
                Debug.LogError("[CharacterCreatorSceneAuthor] Hero prefab not found -- run \"1. Author Character Prefab\" first.");

            var rulesetProvider = AssetDatabase.LoadAssetAtPath<SRD5RulesetProvider>(RulesetProviderPath);
            if (rulesetProvider == null)
                Debug.LogError($"[CharacterCreatorSceneAuthor] {RulesetProviderPath} not found -- run \"CRPG Toolkit/Rules/Refresh SRD5 Ruleset Provider\" first.");

            var bootstrapGo = new GameObject("CharacterCreatorBootstrap");
            var bootstrap = bootstrapGo.AddComponent<CharacterCreatorBootstrap>();
            var serializedBootstrap = new SerializedObject(bootstrap);
            serializedBootstrap.FindProperty("rulesetProvider").objectReferenceValue = rulesetProvider;
            serializedBootstrap.FindProperty("heroPrefab").objectReferenceValue = hero;
            serializedBootstrap.FindProperty("initialSpawnAnchor").objectReferenceValue = spawnAnchorGo.transform;
            serializedBootstrap.FindProperty("inputSource").objectReferenceValue = inputSource;
            serializedBootstrap.FindProperty("view").objectReferenceValue = view;
            serializedBootstrap.ApplyModifiedProperties();

            EnsureFolder(ScenesFolder);
            EditorSceneManager.SaveScene(scene, ScenePath);
        }

        private static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            var parent = System.IO.Path.GetDirectoryName(path)?.Replace('\\', '/');
            var folderName = System.IO.Path.GetFileName(path);
            if (!string.IsNullOrEmpty(parent) && !AssetDatabase.IsValidFolder(parent)) EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, folderName);
        }
    }
}
