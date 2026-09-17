using CRPG.Gameplay.Interactions;
using CRPG.Gameplay.Interactions.Adapters;
using CRPG.InputSystem;
using CRPG.Inventory;
using Unity.AI.Navigation;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace CRPG.Samples.BasicCRPG.Editor
{
    /// <summary>
    /// Authors the BasicCRPG sample's content asset and its one real scene. Run "1. Author Character
    /// Prefabs" first. Mirrors AdventureSliceSceneAuthor's/InteractionLabSceneAuthor's own "rebuild from
    /// scratch, overwrite content, load-or-create assets" conventions, at BasicCRPG's much smaller scope: one
    /// interactable prop, no dialogue/quests/travel/save.
    /// </summary>
    internal static class BasicCrpgSceneAuthor
    {
        private const string ContentFolder = "Packages/com.crpgtoolkit.core/Samples/BasicCRPG/Content";
        private const string PrefabFolder = "Packages/com.crpgtoolkit.core/Samples/BasicCRPG/Content/Prefabs";
        private const string ScenesFolder = "Packages/com.crpgtoolkit.core/Samples/BasicCRPG/Scenes";
        private const string ScenePath = "Packages/com.crpgtoolkit.core/Samples/BasicCRPG/Scenes/BasicCRPG.unity";

        [MenuItem("CRPG Toolkit/Samples/Basic CRPG/2. Author Scene And Content")]
        public static void Author()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;

            // A ScriptableObject reference returned from AuthorContent reliably reads back as a Unity "fake
            // null" by the time AuthorScene's own EditorSceneManager.NewScene call happens -- confirmed live,
            // matching AdventureSliceSceneAuthor's own identical PanelSettings-across-NewScene finding.
            // AuthorScene reloads it fresh by path instead of accepting it as a parameter, the same proven fix.
            AuthorContent();
            AssetDatabase.SaveAssets();
            AuthorScene();

            Debug.Log($"[BasicCrpgSceneAuthor] Authored {ScenePath} and its content.");
        }

        private static ItemDefinition AuthorContent()
        {
            EnsureFolder(ContentFolder);

            var path = $"{ContentFolder}/ItemDefinition_TreasureCoin.asset";
            var existing = AssetDatabase.LoadAssetAtPath<ItemDefinition>(path);
            if (existing != null) return existing;

            var item = ScriptableObject.CreateInstance<ItemDefinition>();
            SetPrivateField(item, "id", "item-treasure-coin-basiccrpg");
            SetPrivateField(item, "displayName", "Treasure Coin");
            AssetDatabase.CreateAsset(item, path);
            return item;
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
            spawnAnchorGo.transform.position = new Vector3(-2f, 0f, -6f);

            // Placed well clear of the party's spawn point so approach movement is real, not a same-spot
            // no-op -- matches AdventureSliceSceneAuthor's own Loot_Chest placement convention.
            var (chestGo, chestAnchor) = CreateFixtureShell("Treasure_Chest", new Vector3(6f, 0f, 4f), new Vector3(0f, 0f, -1.5f));
            var chestAdapter = chestGo.AddComponent<LootInteractionAdapter>();
            var interactable = chestGo.GetComponent<WorldInteractable>();
            interactable.Configure(chestAnchor, 1.5f);
            EditorUtility.SetDirty(interactable);
            chestGo.SetActive(true);

            var surface = ground.AddComponent<NavMeshSurface>();
            surface.BuildNavMesh();

            var inputGo = new GameObject("InputSystemCommandSource");
            var inputSource = inputGo.AddComponent<InputSystemCommandSource>();
            var serializedInput = new SerializedObject(inputSource);
            serializedInput.FindProperty("raycastCamera").objectReferenceValue = camera;
            serializedInput.FindProperty("hittableMask").intValue = ~0;
            serializedInput.ApplyModifiedProperties();

            var hero1 = AssetDatabase.LoadAssetAtPath<GameObject>($"{PrefabFolder}/Hero1.prefab");
            var hero2 = AssetDatabase.LoadAssetAtPath<GameObject>($"{PrefabFolder}/Hero2.prefab");
            if (hero1 == null || hero2 == null)
                Debug.LogError("[BasicCrpgSceneAuthor] Hero1/Hero2 prefabs not found -- run \"1. Author Character Prefabs\" first.");

            var bootstrapGo = new GameObject("BasicCrpgBootstrap");
            var bootstrap = bootstrapGo.AddComponent<BasicCrpgBootstrap>();
            var serializedBootstrap = new SerializedObject(bootstrap);
            var partyMemberPrefabsProp = serializedBootstrap.FindProperty("partyMemberPrefabs");
            partyMemberPrefabsProp.arraySize = 2;
            partyMemberPrefabsProp.GetArrayElementAtIndex(0).objectReferenceValue = hero1;
            partyMemberPrefabsProp.GetArrayElementAtIndex(1).objectReferenceValue = hero2;
            serializedBootstrap.FindProperty("initialSpawnAnchor").objectReferenceValue = spawnAnchorGo.transform;
            // Reloaded fresh by path, exactly like hero1/hero2 just above -- see AuthorContent's own call site
            // for why the treasureItem parameter itself can no longer be trusted by this point.
            var freshTreasureItem = AssetDatabase.LoadAssetAtPath<ItemDefinition>($"{ContentFolder}/ItemDefinition_TreasureCoin.asset");
            serializedBootstrap.FindProperty("treasureItem").objectReferenceValue = freshTreasureItem;
            serializedBootstrap.FindProperty("inputSource").objectReferenceValue = inputSource;
            serializedBootstrap.FindProperty("treasureAdapter").objectReferenceValue = chestAdapter;
            serializedBootstrap.ApplyModifiedProperties();

            EnsureFolder(ScenesFolder);
            EditorSceneManager.SaveScene(scene, ScenePath);
        }

        /// <summary>Built inactive -- LootInteractionAdapter carries [RequireComponent(typeof(WorldInteractable))],
        /// which auto-adds it and runs its Awake (resolving IsConfigured) the instant the adapter is added.
        /// Building inactive first, configuring the anchor, then activating avoids "unconfigured" ever being
        /// observed. Mirrors AdventureSliceSceneAuthor's/InteractionLabSceneAuthor's identical convention.</summary>
        private static (GameObject fixtureGo, Transform anchor) CreateFixtureShell(string name, Vector3 position, Vector3 anchorOffset)
        {
            var fixtureGo = GameObject.CreatePrimitive(PrimitiveType.Cube);
            fixtureGo.name = name;
            fixtureGo.SetActive(false);
            fixtureGo.transform.position = position;
            fixtureGo.transform.localScale = new Vector3(1.2f, 1f, 1f);

            var anchorGo = new GameObject($"{name}_InteractionAnchor");
            anchorGo.transform.SetParent(fixtureGo.transform);
            anchorGo.transform.position = position + anchorOffset;

            return (fixtureGo, anchorGo.transform);
        }

        private static void SetPrivateField(object target, string fieldName, object value)
        {
            var type = target.GetType();
            while (type != null)
            {
                var field = type.GetField(fieldName, System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.DeclaredOnly);
                if (field != null) { field.SetValue(target, value); return; }
                type = type.BaseType;
            }

            Debug.LogError($"[BasicCrpgSceneAuthor] Field '{fieldName}' not found anywhere in {target.GetType()}'s hierarchy.");
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
