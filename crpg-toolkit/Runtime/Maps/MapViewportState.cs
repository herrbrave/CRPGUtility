using System;
using UnityEngine;

namespace CRPG.Maps
{
    /// <summary>How a <see cref="MapViewportState"/> picks its base scale and limits its zoom and pan. Two
    /// presets cover the two views; nothing else needs to construct these by hand.</summary>
    public sealed class MapViewportOptions
    {
        /// <summary>World metres visible across the viewport width at zoom factor 1. Zero or less means "fit the
        /// whole image" instead (the area map's behaviour).</summary>
        public float VisibleWorldWidth { get; set; }

        /// <summary>Lowest zoom factor, relative to the base scale.</summary>
        public float MinZoom { get; set; } = 1f;

        /// <summary>Highest zoom factor, relative to the base scale.</summary>
        public float MaxZoom { get; set; } = 1f;

        /// <summary>When true the centre is kept so the view never shows space beyond the image on an axis the
        /// image can fill (and is centred on an axis it cannot). The minimap leaves this off so it can keep the
        /// tracked character centred even at the image edge.</summary>
        public bool ClampToBounds { get; set; }

        /// <summary>Minimap defaults: 80 m across the viewport, 0.5x-4x zoom, no bounds clamp.</summary>
        public static MapViewportOptions Minimap(float visibleWorldWidth = 80f) => new MapViewportOptions
        {
            VisibleWorldWidth = visibleWorldWidth,
            MinZoom = 0.5f,
            MaxZoom = 4f,
            ClampToBounds = false,
        };

        /// <summary>Area-map defaults: whole image fitted, 1x-8x zoom, bounds clamped.</summary>
        public static MapViewportOptions Area() => new MapViewportOptions
        {
            VisibleWorldWidth = 0f,
            MinZoom = 1f,
            MaxZoom = 8f,
            ClampToBounds = true,
        };
    }

    /// <summary>
    /// Centre / scale / fit / pan / zoom state for one map view over a fixed image rectangle. Plain C#: no input
    /// polling, no UI, no scene access -- the owning view feeds it the viewport size and pointer deltas and reads
    /// <see cref="Projection"/> back. Two instances (minimap and area map) share one definition but never share
    /// state.
    /// </summary>
    /// <remarks>
    /// Until a real (positive, finite) viewport size has been supplied there is no layout: <see cref="HasLayout"/>
    /// is false, <see cref="Projection"/> is an invalid projection, and pan/zoom-at-point calls are no-ops, so a
    /// zero-sized first layout pass can never produce a divide-by-zero or a bogus centre.
    /// </remarks>
    public sealed class MapViewportState
    {
        private readonly MapViewportOptions options;

        public MapViewportState(Rect boundsXZ, MapViewportOptions options)
        {
            if (options == null) throw new ArgumentNullException(nameof(options));
            if (!IsFinite(boundsXZ.x) || !IsFinite(boundsXZ.y) || !IsFinite(boundsXZ.width) || !IsFinite(boundsXZ.height)
                || !(boundsXZ.width > 0f) || !(boundsXZ.height > 0f))
                throw new ArgumentException("Map bounds must be finite with positive width and height.", nameof(boundsXZ));
            if (!(options.MinZoom > 0f) || options.MaxZoom < options.MinZoom)
                throw new ArgumentException("Zoom range must be positive with MaxZoom >= MinZoom.", nameof(options));

            BoundsXZ = boundsXZ;
            this.options = options;
            Center = boundsXZ.center;
            ZoomFactor = 1f;
        }

        public Rect BoundsXZ { get; }

        /// <summary>Viewport size in UI points; <see cref="Vector2.zero"/> until layout supplies one.</summary>
        public Vector2 ViewportSize { get; private set; }

        public bool HasLayout { get; private set; }

        /// <summary>World (x, z) at the viewport centre.</summary>
        public Vector2 Center { get; private set; }

        /// <summary>Zoom relative to <see cref="BaseScale"/>, within the options' range.</summary>
        public float ZoomFactor { get; private set; }

        /// <summary>UI points per metre at zoom factor 1 for the current viewport; 0 without layout.</summary>
        public float BaseScale { get; private set; }

        /// <summary>UI points per metre now (<see cref="BaseScale"/> x <see cref="ZoomFactor"/>).</summary>
        public float Scale => BaseScale * ZoomFactor;

        /// <summary>The current projection, or a default (invalid) one when there is no layout yet.</summary>
        public MapProjection Projection => HasLayout
            ? new MapProjection(BoundsXZ, ViewportSize, Center, Scale)
            : default;

        /// <summary>Supplies the viewport size from layout. A zero, negative or non-finite size clears the
        /// layout (deferring projection). The first valid size starts at zoom factor 1; later sizes keep the
        /// current zoom factor and centre and re-apply the bounds clamp.</summary>
        public void SetViewportSize(Vector2 size)
        {
            if (!IsFinite(size.x) || !IsFinite(size.y) || !(size.x > 0f) || !(size.y > 0f))
            {
                HasLayout = false;
                ViewportSize = Vector2.zero;
                BaseScale = 0f;
                return;
            }

            ViewportSize = size;
            BaseScale = options.VisibleWorldWidth > 0f
                ? size.x / options.VisibleWorldWidth
                : MapProjection.FitScale(BoundsXZ, size);
            HasLayout = BaseScale > 0f;
            ApplyClamp();
        }

        /// <summary>Restores zoom factor 1 and the image centre (the area map's Fit Location).</summary>
        public void Fit()
        {
            ZoomFactor = 1f;
            Center = BoundsXZ.center;
            ApplyClamp();
        }

        /// <summary>Sets the world (x, z) shown at the viewport centre, keeping the zoom. Clamped to the image
        /// when the options ask for it (Recenter on the active character); unclamped otherwise (minimap follow).</summary>
        public void CenterOn(Vector2 worldXZ)
        {
            if (!IsFinite(worldXZ.x) || !IsFinite(worldXZ.y)) return;
            Center = worldXZ;
            ApplyClamp();
        }

        /// <summary>Drags the map by a pointer delta in viewport points (positive x = pointer moved right,
        /// positive y = pointer moved down): the world slides with the pointer.</summary>
        public void PanByViewportDelta(Vector2 delta)
        {
            if (!HasLayout) return;
            var s = Scale;
            Center = new Vector2(Center.x - delta.x / s, Center.y + delta.y / s);
            ApplyClamp();
        }

        /// <summary>Multiplies the zoom by <paramref name="factor"/>, keeping the world point under viewport-local
        /// <paramref name="anchorLocal"/> fixed on screen (up to the bounds clamp).</summary>
        public void ZoomAt(Vector2 anchorLocal, float factor)
        {
            if (!HasLayout) return;
            SetZoomAt(anchorLocal, ZoomFactor * factor);
        }

        /// <summary>Multiplies the zoom about the viewport centre (the zoom buttons).</summary>
        public void ZoomAtCenter(float factor)
        {
            if (!HasLayout) { ZoomFactor = ClampZoom(ZoomFactor * factor); return; }
            SetZoomAt(ViewportSize * 0.5f, ZoomFactor * factor);
        }

        /// <summary>Sets an absolute zoom factor about the viewport centre.</summary>
        public void SetZoomFactor(float zoomFactor)
        {
            if (!HasLayout) { ZoomFactor = ClampZoom(zoomFactor); return; }
            SetZoomAt(ViewportSize * 0.5f, zoomFactor);
        }

        private void SetZoomAt(Vector2 anchorLocal, float requestedZoom)
        {
            if (!IsFinite(requestedZoom) || !(requestedZoom > 0f)) return;

            var before = Projection;
            var anchorWorld = before.ViewportToWorld(anchorLocal);

            ZoomFactor = ClampZoom(requestedZoom);
            var s = Scale;
            Center = new Vector2(
                anchorWorld.x - (anchorLocal.x - ViewportSize.x * 0.5f) / s,
                anchorWorld.y + (anchorLocal.y - ViewportSize.y * 0.5f) / s);
            ApplyClamp();
        }

        private float ClampZoom(float zoom) => Mathf.Clamp(zoom, options.MinZoom, options.MaxZoom);

        private void ApplyClamp()
        {
            if (!options.ClampToBounds || !HasLayout) return;

            var s = Scale;
            Center = new Vector2(
                ClampAxis(Center.x, ViewportSize.x / s, BoundsXZ.xMin, BoundsXZ.xMax),
                ClampAxis(Center.y, ViewportSize.y / s, BoundsXZ.yMin, BoundsXZ.yMax));
        }

        /// <summary>Visible span at least as wide as the image on this axis: centre the image. Otherwise keep
        /// the centre far enough in that no space beyond the image edges is exposed.</summary>
        private static float ClampAxis(float center, float visibleSpan, float min, float max)
        {
            if (visibleSpan >= max - min) return (min + max) * 0.5f;
            return Mathf.Clamp(center, min + visibleSpan * 0.5f, max - visibleSpan * 0.5f);
        }

        private static bool IsFinite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
    }
}
