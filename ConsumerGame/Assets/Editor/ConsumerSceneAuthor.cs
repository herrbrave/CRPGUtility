using CRPG.Consumer.Fate;
using CRPG.Dialogue;
using Unity.AI.Navigation;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;

namespace CRPG.Consumer.Game.Editor
{
    /// <summary>
    /// Builds the compact scene: ground + baked NavMesh, a static overview camera, primitive party/hostile/NPC
    /// spawns are left to <see cref="ConsumerGameBootstrap"/> at runtime (this script only wires the prefab
    /// references), and the HUD. Run <c>Consumer Game/Author Character Prefabs</c> and
    /// <c>Consumer Game/Author Dialogue And UI Content</c> first.
    /// </summary>
    internal static class ConsumerSceneAuthor
    {
        private const string ScenePath = "Assets/Scenes/ConsumerGame.unity";
        private const string PrefabFolder = "Assets/Game/Prefabs";
        private const string ContentFolder = "Assets/Game/Content";
        private const string FateContentFolder = "Assets/Rules/Fate/Content";

        [MenuItem("Consumer Game/Author Scene")]
        public static void AuthorScene()
        {
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            var ground = GameObject.CreatePrimitive(PrimitiveType.Plane);
            ground.name = "Ground";
            ground.transform.localScale = new Vector3(2f, 1f, 2f); // 20x20 units
            var groundMaterial = new Material(Shader.Find("Universal Render Pipeline/Lit")) { color = new Color(0.5f, 0.55f, 0.5f) };
            ground.GetComponent<MeshRenderer>().sharedMaterial = groundMaterial;
            var surface = ground.AddComponent<NavMeshSurface>();
            surface.BuildNavMesh();

            var light = new GameObject("Directional Light").AddComponent<Light>();
            light.type = LightType.Directional;
            light.transform.rotation = Quaternion.Euler(50f, -30f, 0f);

            var cameraGo = new GameObject("Main Camera");
            cameraGo.tag = "MainCamera";
            var camera = cameraGo.AddComponent<UnityEngine.Camera>();
            cameraGo.transform.position = new Vector3(0f, 14f, -11f);
            cameraGo.transform.rotation = Quaternion.Euler(55f, 0f, 0f);

            // PanelSettings loaded/created AFTER NewScene, per this project's own established gotcha
            // (NewScene can silently null out an already-loaded asset reference held before the call).
            var hudPanelSettings = AssetDatabase.LoadAssetAtPath<PanelSettings>($"{ContentFolder}/consumer-hud-panel-settings.asset");
            var dialoguePanelSettings = AssetDatabase.LoadAssetAtPath<PanelSettings>($"{ContentFolder}/consumer-dialogue-panel-settings.asset");
            var dialogue = AssetDatabase.LoadAssetAtPath<DialogueDefinition>($"{ContentFolder}/dialogue-fate-greeting.asset");
            var provider = AssetDatabase.LoadAssetAtPath<FateRulesetProvider>($"{FateContentFolder}/provider-fate-condensed.asset");
            var partyPrefab = AssetDatabase.LoadAssetAtPath<GameObject>($"{PrefabFolder}/PartyCapsule.prefab");
            var hostilePrefab = AssetDatabase.LoadAssetAtPath<GameObject>($"{PrefabFolder}/HostileCapsule.prefab");
            var npcPrefab = AssetDatabase.LoadAssetAtPath<GameObject>($"{PrefabFolder}/NpcCapsule.prefab");

            var hudGo = new GameObject("Hud", typeof(UIDocument));
            var hudDocument = hudGo.GetComponent<UIDocument>();
            hudDocument.panelSettings = hudPanelSettings;
            var hud = hudGo.AddComponent<ConsumerHud>();

            // The real package input adapter -- CRPGInputActions.inputactions + its generated wrapper class
            // already ship in crpg-toolkit/Integrations/InputSystem/, authored once via
            // "CRPG Toolkit/Input/Author Default Input Actions"; no from-scratch command source needed.
            var inputGo = new GameObject("InputSource");
            var commandSource = inputGo.AddComponent<CRPG.InputSystem.InputSystemCommandSource>();
            SetField(commandSource, "raycastCamera", camera);
            SetField(commandSource, "hittableMask", (LayerMask)~0);

            var bootstrapGo = new GameObject("ConsumerGameBootstrap");
            var bootstrap = bootstrapGo.AddComponent<ConsumerGameBootstrap>();
            SetField(bootstrap, "provider", provider);
            SetField(bootstrap, "partyPrefab", partyPrefab);
            SetField(bootstrap, "hostilePrefab", hostilePrefab);
            SetField(bootstrap, "npcPrefab", npcPrefab);
            SetField(bootstrap, "commandSource", commandSource);
            SetField(bootstrap, "hud", hud);
            SetField(bootstrap, "dialogue", dialogue);
            SetField(bootstrap, "dialoguePanelSettings", dialoguePanelSettings);

            if (!AssetDatabase.IsValidFolder("Assets/Scenes")) AssetDatabase.CreateFolder("Assets", "Scenes");
            EditorSceneManager.SaveScene(scene, ScenePath);

            var buildScenes = new[] { new EditorBuildSettingsScene(ScenePath, true) };
            EditorBuildSettings.scenes = buildScenes;

            Debug.Log("[ConsumerSceneAuthor] Authored " + ScenePath);
        }

        private static void SetField(object target, string fieldName, object value)
        {
            var type = target.GetType();
            while (type != null)
            {
                var field = type.GetField(fieldName, System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.DeclaredOnly);
                if (field != null) { field.SetValue(target, value); return; }
                type = type.BaseType;
            }

            throw new System.Exception($"Field '{fieldName}' not found on {target.GetType().Name} or its base types.");
        }
    }
}
