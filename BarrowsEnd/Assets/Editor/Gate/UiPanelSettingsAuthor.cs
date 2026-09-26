using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace BarrowsEnd.Editor
{
    /// <summary>
    /// Task 5.0 (D6): the pixel-exact panel-settings recipe (<see cref="BarrowsEndCreatorAuthor.AuthorPanelSettings"/>'s
    /// precedent -- <c>ConstantPixelSize</c> scale 1, the theme assigned, all five dynamic-atlas filters), generalised
    /// to take its theme and scale mode explicitly rather than hard-coding them, and callable for any asset path.
    /// <para><b>5.0 calls this only for the two NEW assets it creates (the HUD and the gallery/modal-new panel
    /// settings). It does NOT touch <see cref="BarrowsEndCreatorAuthor.AuthorPanelSettings"/> or
    /// <c>PhaseTwoGateSceneAuthor</c>'s shipped gate asset</b> -- the gate asset ships as
    /// <c>PanelScaleMode.ScaleWithScreenSize</c> at 1200x800 today, and switching it inside this task would
    /// re-skin/resize the live shell before anything is wired up, contradicting "nothing shipped changes."
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
