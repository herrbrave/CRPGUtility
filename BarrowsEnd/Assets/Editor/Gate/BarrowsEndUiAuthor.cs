using System.IO;
using BarrowsEnd.Shell;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UIElements;

namespace BarrowsEnd.Editor
{
    /// <summary>
    /// Task 5.0, Steps 4/5: authors the skin's own panel-settings assets (HUD sortingOrder 0, gallery sortingOrder
    /// 10 -- both NEW assets; see D6, this does NOT touch the gate/creator panel settings) and the Skin Gallery dev
    /// scene. Idempotent, like <see cref="BarrowsEndCreatorAuthor"/>.
    /// </summary>
    public static class BarrowsEndUiAuthor
    {
        public const string ThemePath = "Assets/UI/Skin/BarrowsEndTheme.tss";
        public const string HudPanelSettingsPath = "Assets/Content/Ui/hud-panel-settings.asset";
        public const string GalleryPanelSettingsPath = "Assets/Content/Ui/gallery-panel-settings.asset";
        public const int HudSortingOrder = 0;
        public const int GallerySortingOrder = 10;
        public const string GallerySceneDir = "Assets/Scenes/Dev";
        public const string GalleryScenePath = GallerySceneDir + "/SkinGallery.unity";

        public static PanelSettings AuthorHudPanelSettings() =>
            UiPanelSettingsAuthor.Author(HudPanelSettingsPath, HudSortingOrder, LoadTheme());

        public static PanelSettings AuthorGalleryPanelSettings() =>
            UiPanelSettingsAuthor.Author(GalleryPanelSettingsPath, GallerySortingOrder, LoadTheme());

        private static ThemeStyleSheet LoadTheme()
        {
            var theme = AssetDatabase.LoadAssetAtPath<ThemeStyleSheet>(ThemePath);
            if (theme == null) throw new System.InvalidOperationException($"{ThemePath} is missing: run 'Barrows End/UI/Build Skin Parts' first.");
            return theme;
        }

        [MenuItem("Barrows End/UI/Open Skin Gallery")]
        public static void OpenGallery()
        {
            AuthorHudPanelSettings();
            var gallerySettings = AuthorGalleryPanelSettings();
            Directory.CreateDirectory(GallerySceneDir);
            AssetDatabase.Refresh();

            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            // Loaded AFTER NewScene (assets loaded before it can turn into fake-null references -- the project's
            // own established gotcha).
            var reloadedSettings = AssetDatabase.LoadAssetAtPath<PanelSettings>(GalleryPanelSettingsPath);

            var host = new GameObject("SkinGallery");
            var document = host.AddComponent<UIDocument>();
            document.panelSettings = reloadedSettings;
            host.AddComponent<SkinGalleryScreen>();

            EditorSceneManager.SaveScene(scene, GalleryScenePath);
            AssetDatabase.SaveAssets();
        }
    }
}
