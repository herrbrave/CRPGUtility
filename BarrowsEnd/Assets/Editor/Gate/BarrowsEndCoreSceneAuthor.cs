using System.IO;
using BarrowsEnd.Gate;
using BarrowsEnd.Shell;
using CRPG.Camera;
using CRPG.Exploration;
using CRPG.InputSystem;
using CRPG.UI;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UIElements;

namespace BarrowsEnd.Editor
{
    /// <summary>
    /// Task 3.4: authors <c>Core.unity</c>, the persistent scene that owns everything that outlives a location: the authored camera rig, the input source
    /// (raycast camera = the rig's, cameraRig set so zoom works, hittable mask that excludes baked wall boxes), the <see cref="ShellBootstrap"/> that starts
    /// the session and loads the first location, and a small panel. Also puts every scene in the build settings (Core first). Never hand-edited.
    /// </summary>
    public static class BarrowsEndCoreSceneAuthor
    {
        public const string ScenePath = "Assets/Scenes/Core.unity";

        [MenuItem("Barrows End/Content/Author Core Scene")]
        public static void Author()
        {
            BarrowsEndCameraAuthor.Author();
            BarrowsEndGearAuthor.Author();
            BarrowsEndLocationAuthor.AuthorAll();
            BarrowsEndCharacterAuthor.Author();
            BarrowsEndCreatorAuthor.AuthorPanelSettings(); // the creator/title panel settings: this author never created them (a fresh clone would render nothing)
            Directory.CreateDirectory("Assets/Scenes");
            AssetDatabase.Refresh();

            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            // Loaded AFTER NewScene (assets loaded before it can turn into fake-null references).
            var ruleset = AssetDatabase.LoadAssetAtPath<BarrowsEndRulesetProvider>(BarrowsEndGearAuthor.RulesetPath);
            var partyPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(BarrowsEndCharacterAuthor.SpriteCharacterPath);
            var rigPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(BarrowsEndCameraAuthor.RigPrefabPath);
            var panelSettings = AssetDatabase.LoadAssetAtPath<PanelSettings>(PhaseTwoGateSceneAuthor.PanelSettingsPath);
            var creatorSettings = AssetDatabase.LoadAssetAtPath<PanelSettings>(BarrowsEndCreatorAuthor.PanelSettingsPath)
                                  ?? throw new System.InvalidOperationException($"{BarrowsEndCreatorAuthor.PanelSettingsPath} is missing.");
            var creatorPicks = AssetDatabase.LoadAssetAtPath<TextAsset>(BarrowsEndCreatorAuthor.PicksPath) ?? throw new System.InvalidOperationException($"{BarrowsEndCreatorAuthor.PicksPath} is missing.");
            var creatorTable = AssetDatabase.LoadAssetAtPath<TextAsset>(BarrowsEndCreatorAuthor.ClassTablePath) ?? throw new System.InvalidOperationException($"{BarrowsEndCreatorAuthor.ClassTablePath} is missing.");
            if (panelSettings == null) throw new System.InvalidOperationException($"{PhaseTwoGateSceneAuthor.PanelSettingsPath} is missing.");

            var rigGo = (GameObject)PrefabUtility.InstantiatePrefab(rigPrefab);
            var rig = rigGo.GetComponent<IsometricCameraRig>();
            var follow = rigGo.GetComponent<PartyCameraFollowBehaviour>();
            var camera = rigGo.GetComponentInChildren<Camera>();

            var inputGo = new GameObject("Input");
            var input = inputGo.AddComponent<InputSystemCommandSource>();
            var inputSo = new SerializedObject(input);
            inputSo.FindProperty("raycastCamera").objectReferenceValue = camera;
            inputSo.FindProperty("cameraRig").objectReferenceValue = rig;
            inputSo.FindProperty("hittableMask.m_Bits").uintValue = (uint)BarrowsEndLayers.RouterMask; // unsigned bits: a plain int would clamp ~mask to 0
            inputSo.ApplyModifiedPropertiesWithoutUndo();

            var bootGo = new GameObject("ShellBootstrap");
            var boot = bootGo.AddComponent<ShellBootstrap>();
            var so = new SerializedObject(boot);
            so.FindProperty("ruleset").objectReferenceValue = ruleset;
            so.FindProperty("partyPrefab").objectReferenceValue = partyPrefab;
            so.FindProperty("startingScenePath").stringValue = BarrowsEndLocationAuthor.TownPath;
            so.FindProperty("startingDestinationId").stringValue = BarrowsEndLocationAuthor.TownStart;
            so.FindProperty("inputSource").objectReferenceValue = input;
            so.FindProperty("cameraRig").objectReferenceValue = rig;
            so.FindProperty("cameraFollow").objectReferenceValue = follow;
            var members = GateLayout.DefaultMembers();
            var membersProp = so.FindProperty("members");
            membersProp.arraySize = members.Length;
            for (var i = 0; i < members.Length; i++)
            {
                var element = membersProp.GetArrayElementAtIndex(i);
                element.FindPropertyRelative("displayName").stringValue = members[i].displayName;
                element.FindPropertyRelative("spawnId").stringValue = members[i].spawnId;
                element.FindPropertyRelative("maxHp").intValue = members[i].maxHp;
                Strings(element.FindPropertyRelative("appearance"), members[i].appearance);
                Strings(element.FindPropertyRelative("gear"), members[i].gear);
            }

            so.ApplyModifiedPropertiesWithoutUndo();

            // Task 4.3: the title and creator screens (both on the creator panel settings, sorting order 10) and the start flow wiring. The real game starts at the title;
            // ShellBootstrap.StartModeOverride lets the tests that boot straight into the town keep doing so.
            var titleGo = new GameObject("TitleScreen");
            titleGo.AddComponent<UIDocument>().panelSettings = creatorSettings;
            var title = titleGo.AddComponent<ShellTitleScreen>();
            var creatorGo = new GameObject("CharacterCreator");
            creatorGo.AddComponent<UIDocument>().panelSettings = creatorSettings;
            var creator = creatorGo.AddComponent<CharacterCreatorScreen>();
            so.FindProperty("startMode").enumValueIndex = (int)ShellStartMode.Title;
            so.FindProperty("titleScreen").objectReferenceValue = title;
            so.FindProperty("creatorScreen").objectReferenceValue = creator;
            so.FindProperty("creatorPicks").objectReferenceValue = creatorPicks;
            so.FindProperty("creatorClassTable").objectReferenceValue = creatorTable;
            so.ApplyModifiedPropertiesWithoutUndo();

            var panelGo = new GameObject("ShellPanel");
            var document = panelGo.AddComponent<UIDocument>();
            document.panelSettings = panelSettings;
            var panel = panelGo.AddComponent<ShellPanel>();
            var panelSo = new SerializedObject(panel);
            panelSo.FindProperty("bootstrap").objectReferenceValue = boot;
            panelSo.ApplyModifiedPropertiesWithoutUndo();

            EditorSceneManager.SaveScene(scene, ScenePath);
            EditorBuildSettings.scenes = new[]
            {
                new EditorBuildSettingsScene(ScenePath, true),
                new EditorBuildSettingsScene(BarrowsEndLocationAuthor.TownPath, true),
                new EditorBuildSettingsScene(BarrowsEndLocationAuthor.CavePath, true),
                new EditorBuildSettingsScene(PhaseTwoGateSceneAuthor.ScenePath, true),
                new EditorBuildSettingsScene(PhaseTwoGateSceneAuthor.LookSpikeScenePath, true),
            };
            Debug.Log($"[BarrowsEnd] Core scene authored: {ScenePath}");
        }

        [MenuItem("Barrows End/Open Campaign Core")]
        public static void Open()
        {
            if (!File.Exists(ScenePath)) Author();
            EditorSceneManager.OpenScene(ScenePath);
        }

        private static void Strings(SerializedProperty array, string[] values)
        {
            array.arraySize = values.Length;
            for (var i = 0; i < values.Length; i++) array.GetArrayElementAtIndex(i).stringValue = values[i];
        }
    }
}
