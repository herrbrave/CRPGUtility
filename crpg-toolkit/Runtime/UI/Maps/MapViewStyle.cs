using UnityEngine;

namespace CRPG.UI
{
    /// <summary>
    /// Every size and colour the minimap and the area map share, in one place, so the two views and the legend
    /// can never drift apart. Placeholder look in the same spirit as the other procedural HUD panels; a re-skin
    /// replaces <see cref="Default"/> rather than hunting through view code.
    /// </summary>
    public sealed class MapViewStyle
    {
        public static MapViewStyle Default { get; } = new MapViewStyle();

        // Viewport
        public Color ViewportBackground = new Color(0.10f, 0.11f, 0.13f, 1f);
        public Color PanelBackground = new Color(0f, 0f, 0f, 0.55f);
        public Color TextColor = Color.white;
        public Color ButtonBackground = new Color(0.22f, 0.23f, 0.27f, 0.95f);
        public Color ButtonBorder = new Color(0.5f, 0.5f, 0.55f, 1f);

        // Party markers. Each state differs in shape and size, never colour alone.
        public float ActiveSize = 14f;
        public Color ActiveFill = new Color(1f, 0.85f, 0.2f, 1f);
        public Color ActiveOutline = new Color(0.05f, 0.05f, 0.05f, 1f);
        public float ActiveOutlineWidth = 2f;

        public float SelectedSize = 12f;
        public Color SelectedRing = new Color(1f, 1f, 1f, 1f);
        public float SelectedRingWidth = 2f;

        public float UnselectedSize = 8f;
        public Color UnselectedFill = new Color(0.8f, 0.8f, 0.8f, 0.65f);

        // Landmarks. The default symbol is a diamond (a square turned 45 degrees).
        public float LandmarkSize = 14f;
        public float LandmarkDiamondSide = 10f;
        public Color LandmarkOutline = new Color(0.05f, 0.05f, 0.05f, 1f);

        // Minimap
        public float MinimapSize = 240f;
        public float MinimapMargin = 16f;
        public float MinimapPadding = 4f;
        public float MinimapMinimumSize = 96f;
        public float MinimapMaxViewportFraction = 0.4f;
        public float MinimapSamplesPerSecond = 20f;

        // Zoom
        public float ButtonZoomFactor = 1.5f;
        public float WheelZoomFactor = 1.15f;

        // Hover label
        public Color HoverLabelBackground = new Color(0f, 0f, 0f, 0.85f);
        public float HoverLabelOffset = 12f;
    }
}
