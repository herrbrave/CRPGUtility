using UnityEngine;

namespace CRPG.Maps
{
    /// <summary>
    /// Pure world-XZ / texture-UV / viewport-local coordinate conversion for a north-up location map. No
    /// Physics, NavMesh, Camera, UI or scene access -- just arithmetic over four numbers. World +X is the right
    /// of the map and world +Z is the top. Viewport-local coordinates use a top-left origin in UI points; UV
    /// uses a bottom-left origin (matching UI Toolkit's Image API), so the one vertical flip lives here and
    /// nowhere else.
    /// </summary>
    /// <remarks>
    /// <see cref="BoundsXZ"/> is the exact world rectangle the background image represents (Rect.x is world X,
    /// Rect.y is world Z) -- not the gameplay camera's view and not a terrain bound. World height Y is never
    /// an input: the map is single-floor.
    /// </remarks>
    public readonly struct MapProjection
    {
        public MapProjection(Rect boundsXZ, Vector2 viewportSize, Vector2 centerXZ, float scale)
        {
            BoundsXZ = boundsXZ;
            ViewportSize = viewportSize;
            CenterXZ = centerXZ;
            Scale = scale;
        }

        /// <summary>World rectangle covered by the full background image.</summary>
        public Rect BoundsXZ { get; }

        /// <summary>Viewport size in UI points.</summary>
        public Vector2 ViewportSize { get; }

        /// <summary>World (x, z) shown at the viewport's centre.</summary>
        public Vector2 CenterXZ { get; }

        /// <summary>UI points per world metre.</summary>
        public float Scale { get; }

        /// <summary>False when any input is non-finite, the bounds are empty, or the viewport/scale is not
        /// positive -- e.g. before layout has produced a real size. Callers defer projection until valid.</summary>
        public bool IsValid =>
            IsFinite(BoundsXZ.x) && IsFinite(BoundsXZ.y) && IsFinite(BoundsXZ.width) && IsFinite(BoundsXZ.height) &&
            BoundsXZ.width > 0f && BoundsXZ.height > 0f &&
            IsFinite(ViewportSize.x) && IsFinite(ViewportSize.y) && ViewportSize.x > 0f && ViewportSize.y > 0f &&
            IsFinite(CenterXZ.x) && IsFinite(CenterXZ.y) &&
            IsFinite(Scale) && Scale > 0f;

        /// <summary>Viewport-local position (top-left origin) of world point (x, z).</summary>
        public Vector2 WorldToViewport(float x, float z) => new Vector2(
            ViewportSize.x * 0.5f + (x - CenterXZ.x) * Scale,
            ViewportSize.y * 0.5f - (z - CenterXZ.y) * Scale);

        /// <summary>As <see cref="WorldToViewport(float, float)"/>; the world point's Y is ignored.</summary>
        public Vector2 WorldToViewport(Vector3 world) => WorldToViewport(world.x, world.z);

        /// <summary>World (x, z) under viewport-local point <paramref name="local"/> (top-left origin).</summary>
        public Vector2 ViewportToWorld(Vector2 local) => new Vector2(
            CenterXZ.x + (local.x - ViewportSize.x * 0.5f) / Scale,
            CenterXZ.y + (ViewportSize.y * 0.5f - local.y) / Scale);

        /// <summary>Texture UV (bottom-left origin, 0..1 across the image) of world point (x, z). Not clamped:
        /// points outside the image give values outside 0..1.</summary>
        public Vector2 WorldToUv(float x, float z) => new Vector2(
            (x - BoundsXZ.x) / BoundsXZ.width,
            (z - BoundsXZ.y) / BoundsXZ.height);

        /// <summary>World (x, z) of texture UV (bottom-left origin).</summary>
        public Vector2 UvToWorld(Vector2 uv) => new Vector2(
            BoundsXZ.x + uv.x * BoundsXZ.width,
            BoundsXZ.y + uv.y * BoundsXZ.height);

        /// <summary>Where the full background image sits inside the viewport, viewport-local with a top-left
        /// origin (x = left, y = top). Position an image element with this rect and clip it with the viewport.</summary>
        public Rect ImageRect => new Rect(
            ViewportSize.x * 0.5f + (BoundsXZ.xMin - CenterXZ.x) * Scale,
            ViewportSize.y * 0.5f - (BoundsXZ.yMax - CenterXZ.y) * Scale,
            BoundsXZ.width * Scale,
            BoundsXZ.height * Scale);

        /// <summary>True when world point (x, z) lies inside the image rectangle (edges inclusive).</summary>
        public bool ContainsWorld(float x, float z) =>
            x >= BoundsXZ.xMin && x <= BoundsXZ.xMax && z >= BoundsXZ.yMin && z <= BoundsXZ.yMax;

        /// <summary>True when viewport-local point lies inside the viewport rectangle (edges inclusive) --
        /// the test a marker needs for boundary clipping.</summary>
        public bool ContainsViewport(Vector2 local) =>
            local.x >= 0f && local.x <= ViewportSize.x && local.y >= 0f && local.y <= ViewportSize.y;

        /// <summary>The scale at which the whole image just fits the viewport without distortion:
        /// min(W / width, H / height). Zero for an unusable viewport or bounds.</summary>
        public static float FitScale(Rect boundsXZ, Vector2 viewportSize)
        {
            if (!(boundsXZ.width > 0f) || !(boundsXZ.height > 0f) || !(viewportSize.x > 0f) || !(viewportSize.y > 0f))
                return 0f;
            return Mathf.Min(viewportSize.x / boundsXZ.width, viewportSize.y / boundsXZ.height);
        }

        private static bool IsFinite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
    }
}
