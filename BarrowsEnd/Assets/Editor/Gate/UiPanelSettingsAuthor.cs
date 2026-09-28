using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace BarrowsEnd.Editor
{
    /// <summary>
    /// Task 5.0 (D6): the pixel-exact panel-settings recipe (originally <see cref="BarrowsEndCreatorAuthor.AuthorPanelSettings"/>'s
    /// own hand-rolled body, before Task 5.3 replaced it with a call to this method), generalised to take its
    /// theme and scale mode explicitly rather than hard-coding them, and callable for any asset path.
    /// <para><b>5.0 called this only for the two NEW assets it created (the HUD and the gallery/modal-new panel
    /// settings); it did NOT yet touch <see cref="BarrowsEndCreatorAuthor.AuthorPanelSettings"/> or
    /// <c>PhaseTwoGateSceneAuthor</c>'s shipped gate asset</b> -- switching either mid-5.0 would have re-skinned/
    /// resized the live shell before anything was wired up, contradicting "nothing shipped changes" for that
    /// task. Task 5.3 explicitly opted the creator's asset in (matching the plan this doc comment already
    /// predicted); the gate's own asset remains untouched (it ships as <c>PanelScaleMode.ConstantPhysicalSize</c>
    /// today -- verified directly, not the <c>ScaleWithScreenSize</c> this comment used to guess).
    /// 5.1 and 5.3 each explicitly opt their existing asset into this author when they re-skin that screen.</para>
    /// </summary>
    public static class UiPanelSettingsAuthor
    {
        public const int AllAtlasFilters = (int)(DynamicAtlasFilters.Readability | DynamicAtlasFilters.Size | DynamicAtlasFilters.Format | DynamicAtlasFilters.ColorSpace | DynamicAtlasFilters.FilterMode);

        public static PanelSettings Author(string path, int sortingOrder, ThemeStyleSheet theme, PanelScaleMode scaleMode = PanelScaleMode.ConstantPixelSize)
        {
            if (theme == null) throw new System.InvalidOperationException($"Author('{path}'): theme must not be null.");

            var dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
            AssetDatabase.Refresh();

            var settings = AssetDatabase.LoadAssetAtPath<PanelSettings>(path);
            if (settings == null)
            {
                settings = ScriptableObject.CreateInstance<PanelSettings>();
                AssetDatabase.CreateAsset(settings, path);
            }

            settings.themeStyleSheet = theme;
            settings.scaleMode = scaleMode;
            if (scaleMode == PanelScaleMode.ConstantPixelSize) settings.scale = 1f;
            settings.sortingOrder = sortingOrder;
            var atlas = settings.dynamicAtlasSettings;
            atlas.activeFilters = (DynamicAtlasFilters)AllAtlasFilters;
            settings.dynamicAtlasSettings = atlas;

            EditorUtility.SetDirty(settings);
            AssetDatabase.SaveAssets();
            return settings;
        }
    }
}
