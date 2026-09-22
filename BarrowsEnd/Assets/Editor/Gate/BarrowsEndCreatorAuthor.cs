using System.IO;
using BarrowsEnd.Shell;
using CRPG.UI;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UIElements;

namespace BarrowsEnd.Editor
{
    /// <summary>
    /// Task 4.2: authors the creator's <see cref="PanelSettings"/> asset and the dev scene (<c>Creator/CreatorPreview.unity</c>) that hosts the
    /// <see cref="CharacterCreatorScreen"/> so the creator can be opened and clicked through on its own. Idempotent: both assets are updated in place (GUIDs stay),
    /// and the panel settings are set EXPLICITLY every run, so a hand edit or a Unity default cannot drift past the tests.
    /// <para>The panel settings are what the preview needs to be pixel-exact (Task 4.1 spike): <see cref="PanelScaleMode.ConstantPixelSize"/> at scale 1 (fractional panel
    /// scaling would blur the integer-scaled sprites), the runtime theme assigned, and every dynamic-atlas filter active (an atlassed or bilinear texture blurs at any scale).</para>
    /// </summary>
    public static class BarrowsEndCreatorAuthor
    {
        public const string PanelSettingsPath = "Assets/Content/Creator/creator-panel-settings.asset";
        public const string ThemePath = "Assets/UI Toolkit/UnityThemes/UnityDefaultRuntimeTheme.tss";
        public const string PicksPath = "Assets/Content/Creator/CreatorPicks.json";
        public const string ClassTablePath = "Assets/Content/Creator/CreatorClassTable.json";
        public const string SceneDir = "Assets/Scenes/Creator";
        public const string ScenePath = SceneDir + "/CreatorPreview.unity";
        public const int SortingOrder = 10;

        public const DynamicAtlasFilters AllAtlasFilters = DynamicAtlasFilters.Readability | DynamicAtlasFilters.Size | DynamicAtlasFilters.Format | DynamicAtlasFilters.ColorSpace | DynamicAtlasFilters.FilterMode;

        public static PanelSettings AuthorPanelSettings()
        {
            Directory.CreateDirectory(Path.GetDirectoryName(PanelSettingsPath));
            AssetDatabase.Refresh();

            var settings = AssetDatabase.LoadAssetAtPath<PanelSettings>(PanelSettingsPath);
            if (settings == null)
            {
                settings = ScriptableObject.CreateInstance<PanelSettings>();
                AssetDatabase.CreateAsset(settings, PanelSettingsPath);
            }

            var theme = AssetDatabase.LoadAssetAtPath<ThemeStyleSheet>(ThemePath)
                        ?? throw new System.InvalidOperationException($"{ThemePath} is missing: the runtime theme the creator panel needs.");
            settings.themeStyleSheet = theme;
            settings.scaleMode = PanelScaleMode.ConstantPixelSize;
            settings.scale = 1f;
            settings.sortingOrder = SortingOrder; // the title and creator draw above the gate ShellPanel (sortingOrder 0)
            var atlas = settings.dynamicAtlasSettings;
            atlas.activeFilters = AllAtlasFilters;
            settings.dynamicAtlasSettings = atlas;

            EditorUtility.SetDirty(settings);
            AssetDatabase.SaveAssets();
            return settings;
        }

        [MenuItem("Barrows End/Content/Author Creator Preview Scene")]
        public static void Author()
        {
            AuthorPanelSettings();
            Directory.CreateDirectory(SceneDir);
            AssetDatabase.Refresh();

            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            // Loaded AFTER NewScene (assets loaded before it can turn into fake-null references).
            var ruleset = AssetDatabase.LoadAssetAtPath<BarrowsEndRulesetProvider>(BarrowsEndGearAuthor.RulesetPath)
                          ?? throw new System.InvalidOperationException("The ruleset is missing: run 'Barrows End/Content/Author Gear And Ruleset' first.");
            var picks = AssetDatabase.LoadAssetAtPath<TextAsset>(PicksPath) ?? throw new System.InvalidOperationException($"{PicksPath} is missing.");
            var table = AssetDatabase.LoadAssetAtPath<TextAsset>(ClassTablePath) ?? throw new System.InvalidOperationException($"{ClassTablePath} is missing.");
            var panelSettings = AssetDatabase.LoadAssetAtPath<PanelSettings>(PanelSettingsPath);

            // UI Toolkit needs no camera, but a scene with none warns "no cameras rendering" in the Game view.
            var cameraGo = new GameObject("Camera");
            var camera = cameraGo.AddComponent<Camera>();
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(0.08f, 0.09f, 0.11f, 1f);
            camera.orthographic = true;

            var go = new GameObject("CharacterCreator");
            var document = go.AddComponent<UIDocument>();
            document.panelSettings = panelSettings;
            var screen = go.AddComponent<CharacterCreatorScreen>();

            var bootstrap = go.AddComponent<CreatorPreviewBootstrap>();
            var so = new SerializedObject(bootstrap);
            so.FindProperty("ruleset").objectReferenceValue = ruleset;
            so.FindProperty("picks").objectReferenceValue = picks;
            so.FindProperty("classTable").objectReferenceValue = table;
            so.FindProperty("screen").objectReferenceValue = screen;
            so.ApplyModifiedPropertiesWithoutUndo();

            EditorSceneManager.SaveScene(scene, ScenePath);
            Debug.Log($"[BarrowsEnd] Creator preview scene authored: {ScenePath}");
        }

        [MenuItem("Barrows End/Open Creator Preview")]
        public static void Open()
        {
            if (!File.Exists(ScenePath)) Author();
            EditorSceneManager.OpenScene(ScenePath);
        }
    }
}
