using CRPG.AI;
using CRPG.Abilities;
using CRPG.Actors;
using CRPG.Core;
using CRPG.InputSystem;
using CRPG.Rules.SRD5;
using CRPG.UI;
using Unity.AI.Navigation;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UIElements;

namespace CRPG.Samples.TacticalEncounter.Editor
{
    /// <summary>
    /// Authors the TacticalEncounter sample's PanelSettings and its one real scene. Run "1. Author Content"
    /// and "2. Author Combatant Prefabs" first. Mirrors BasicCrpgSceneAuthor's/CharacterCreatorSceneAuthor's
    /// own "rebuild from scratch, reload-by-path-after-NewScene" conventions.
    /// </summary>
    internal static class TacticalEncounterSceneAuthor
    {
        private const string ContentFolder = "Packages/com.crpgtoolkit.core/Samples/TacticalEncounter/Content";
        private const string PrefabFolder = "Packages/com.crpgtoolkit.core/Samples/TacticalEncounter/Content/Prefabs";
        private const string ScenesFolder = "Packages/com.crpgtoolkit.core/Samples/TacticalEncounter/Scenes";
        private const string ScenePath = "Packages/com.crpgtoolkit.core/Samples/TacticalEncounter/Scenes/TacticalEncounter.unity";
        private const string PanelSettingsPath = "Packages/com.crpgtoolkit.core/Samples/TacticalEncounter/Content/tactical-encounter-panel-settings.asset";
        private const string RulesetProviderPath = "Packages/com.crpgtoolkit.core/Rules/SRD5e2014/Content/SRD5RulesetProvider.asset";
        private const string Srd5ContentFolder = "Packages/com.crpgtoolkit.core/Rules/SRD5e2014/Content";

        [MenuItem("CRPG Toolkit/Samples/Tactical Encounter/3. Author Scene")]
        public static void Author()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;

            AuthorPanelSettings();
            AssetDatabase.SaveAssets();
            AuthorScene();

            Debug.Log($"[TacticalEncounterSceneAuthor] Authored {ScenePath}.");
        }

        private static void AuthorPanelSettings()
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
            var groundMaterial = new Material(Shader.Find("Universal Render Pipeline/Lit")) { color = new Color(0.4f, 0.45f, 0.5f) };
            ground.GetComponent<MeshRenderer>().sharedMaterial = groundMaterial;

            var wall = GameObject.CreatePrimitive(PrimitiveType.Cube);
            wall.name = "Obstacle";
            wall.transform.position = new Vector3(0f, 1f, 0f);
            wall.transform.localScale = new Vector3(10f, 2f, 1f);
            var wallMaterial = new Material(Shader.Find("Universal Render Pipeline/Lit")) { color = new Color(0.3f, 0.3f, 0.3f) };
            wall.GetComponent<MeshRenderer>().sharedMaterial = wallMaterial;

            var light = new GameObject("Directional Light").AddComponent<Light>();
            light.type = LightType.Directional;
            light.transform.rotation = Quaternion.Euler(55f, -30f, 0f);

            var cameraGo = new GameObject("Main Camera");
            cameraGo.tag = "MainCamera";
            var camera = cameraGo.AddComponent<Camera>();
            cameraGo.transform.position = new Vector3(0f, 22f, -14f);
            cameraGo.transform.rotation = Quaternion.Euler(58f, 0f, 0f);

            var heroAnchor1 = new GameObject("HeroSpawn_1"); heroAnchor1.transform.position = new Vector3(-3f, 0f, -8f);
            var heroAnchor2 = new GameObject("HeroSpawn_2"); heroAnchor2.transform.position = new Vector3(3f, 0f, -8f);
            var foeAnchor1 = new GameObject("FoeSpawn_1"); foeAnchor1.transform.position = new Vector3(-3f, 0f, 8f);
            var foeAnchor2 = new GameObject("FoeSpawn_2"); foeAnchor2.transform.position = new Vector3(3f, 0f, 8f);

            var surface = ground.AddComponent<NavMeshSurface>();
            surface.collectObjects = CollectObjects.All;
            surface.BuildNavMesh();

            // Deliberately no EventSystem/InputSystemUIInputModule -- CharacterCreator's own UI Toolkit
            // buttons already proved live (real human click-through) that this project's runtime UIDocument
            // panels receive mouse clicks without one. Adding it would also pull in com.unity.ugui as a real
            // new package dependency for no proven benefit -- if a live check ever shows AbilityBarView's
            // buttons genuinely not receiving clicks, revisit this, but don't add the dependency speculatively.
            var inputGo = new GameObject("InputSystemCommandSource");
            var inputSource = inputGo.AddComponent<InputSystemCommandSource>();
            var serializedInput = new SerializedObject(inputSource);
            serializedInput.FindProperty("raycastCamera").objectReferenceValue = camera;
            serializedInput.FindProperty("hittableMask").intValue = ~0;
            serializedInput.ApplyModifiedProperties();

            // Reloaded fresh by path (not held from before NewScene) -- same "fake null across NewScene"
            // discipline every prior *SceneAuthor.cs* in this project already follows.
            var panelSettings = AssetDatabase.LoadAssetAtPath<PanelSettings>(PanelSettingsPath);
            var hudGo = new GameObject("Hud");
            var document = hudGo.AddComponent<UIDocument>();
            document.panelSettings = panelSettings;
            var hudRoot = hudGo.AddComponent<HudRoot>();
            var turnOrderView = hudGo.AddComponent<TurnOrderView>();
            var abilityBarView = hudGo.AddComponent<AbilityBarView>();
            var combatLogPanel = hudGo.AddComponent<CombatLogPanel>();

            var hero = AssetDatabase.LoadAssetAtPath<GameObject>($"{PrefabFolder}/Hero.prefab");
            var foe = AssetDatabase.LoadAssetAtPath<GameObject>($"{PrefabFolder}/Foe.prefab");
            if (hero == null || foe == null)
                Debug.LogError("[TacticalEncounterSceneAuthor] Hero/Foe prefabs not found -- run \"2. Author Combatant Prefabs\" first.");

            var rulesetProvider = AssetDatabase.LoadAssetAtPath<SRD5RulesetProvider>(RulesetProviderPath);
            var strength = AssetDatabase.LoadAssetAtPath<StatDefinition>($"{Srd5ContentFolder}/StatDefinition_Strength.asset");
            var hitPoints = AssetDatabase.LoadAssetAtPath<ResourceDefinition>($"{Srd5ContentFolder}/ResourceDefinition_HitPoints.asset");
            var action = AssetDatabase.LoadAssetAtPath<ResourceDefinition>($"{Srd5ContentFolder}/ResourceDefinition_Action.asset");
            var reaction = AssetDatabase.LoadAssetAtPath<ResourceDefinition>($"{Srd5ContentFolder}/ResourceDefinition_Reaction.asset");
            if (rulesetProvider == null || strength == null || hitPoints == null || action == null || reaction == null)
                Debug.LogError("[TacticalEncounterSceneAuthor] Could not load one or more real SRD5 assets.");

            var meleeStrike = AssetDatabase.LoadAssetAtPath<AbilityDefinition>($"{ContentFolder}/AbilityDefinition_MeleeStrike.asset");
            var opportunityAttack = AssetDatabase.LoadAssetAtPath<AbilityDefinition>($"{ContentFolder}/AbilityDefinition_OpportunityAttack.asset");
            var reactiveStrikes = AssetDatabase.LoadAssetAtPath<FeatureDefinition>($"{ContentFolder}/FeatureDefinition_ReactiveStrikes.asset");
            var movement = AssetDatabase.LoadAssetAtPath<ResourceDefinition>($"{ContentFolder}/ResourceDefinition_Movement.asset");
            var closeDistance = AssetDatabase.LoadAssetAtPath<BehaviorDefinition>($"{ContentFolder}/BehaviorDefinition_CloseDistance.asset");
            var meleeStrikeNearest = AssetDatabase.LoadAssetAtPath<BehaviorDefinition>($"{ContentFolder}/BehaviorDefinition_MeleeStrikeNearest.asset");
            var foeWarrior = AssetDatabase.LoadAssetAtPath<BehaviorSetDefinition>($"{ContentFolder}/BehaviorSetDefinition_FoeWarrior.asset");
            if (meleeStrike == null || opportunityAttack == null || reactiveStrikes == null || movement == null
                || closeDistance == null || meleeStrikeNearest == null || foeWarrior == null)
                Debug.LogError("[TacticalEncounterSceneAuthor] Could not load the sample's own content -- run \"1. Author Content\" first.");

            var bootstrapGo = new GameObject("TacticalEncounterBootstrap");
            var bootstrap = bootstrapGo.AddComponent<TacticalEncounterBootstrap>();
            var serializedBootstrap = new SerializedObject(bootstrap);

            serializedBootstrap.FindProperty("rulesetProvider").objectReferenceValue = rulesetProvider;
            serializedBootstrap.FindProperty("strengthStat").objectReferenceValue = strength;
            serializedBootstrap.FindProperty("hitPointsResource").objectReferenceValue = hitPoints;
            serializedBootstrap.FindProperty("actionResource").objectReferenceValue = action;
            serializedBootstrap.FindProperty("reactionResource").objectReferenceValue = reaction;

            serializedBootstrap.FindProperty("meleeStrike").objectReferenceValue = meleeStrike;
            serializedBootstrap.FindProperty("opportunityAttack").objectReferenceValue = opportunityAttack;
            serializedBootstrap.FindProperty("reactiveStrikes").objectReferenceValue = reactiveStrikes;
            serializedBootstrap.FindProperty("movementResource").objectReferenceValue = movement;
            serializedBootstrap.FindProperty("closeDistanceBehavior").objectReferenceValue = closeDistance;
            serializedBootstrap.FindProperty("meleeStrikeNearestBehavior").objectReferenceValue = meleeStrikeNearest;
            serializedBootstrap.FindProperty("foeWarriorBehaviorSet").objectReferenceValue = foeWarrior;

            serializedBootstrap.FindProperty("heroPrefab").objectReferenceValue = hero;
            serializedBootstrap.FindProperty("foePrefab").objectReferenceValue = foe;

            var heroAnchorsProp = serializedBootstrap.FindProperty("heroSpawnAnchors");
            heroAnchorsProp.arraySize = 2;
            heroAnchorsProp.GetArrayElementAtIndex(0).objectReferenceValue = heroAnchor1.transform;
            heroAnchorsProp.GetArrayElementAtIndex(1).objectReferenceValue = heroAnchor2.transform;

            var foeAnchorsProp = serializedBootstrap.FindProperty("foeSpawnAnchors");
            foeAnchorsProp.arraySize = 2;
            foeAnchorsProp.GetArrayElementAtIndex(0).objectReferenceValue = foeAnchor1.transform;
            foeAnchorsProp.GetArrayElementAtIndex(1).objectReferenceValue = foeAnchor2.transform;

            serializedBootstrap.FindProperty("inputSource").objectReferenceValue = inputSource;
            serializedBootstrap.FindProperty("hudRoot").objectReferenceValue = hudRoot;
            serializedBootstrap.FindProperty("abilityBarView").objectReferenceValue = abilityBarView;
            serializedBootstrap.FindProperty("turnOrderView").objectReferenceValue = turnOrderView;
            serializedBootstrap.FindProperty("combatLogPanel").objectReferenceValue = combatLogPanel;

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
            AssetDatabase.CreateFolder(string.IsNullOrEmpty(parent) ? "Assets" : parent, folderName);
        }
    }
}
