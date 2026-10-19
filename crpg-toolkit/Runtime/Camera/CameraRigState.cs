using UnityEngine;

namespace CRPG.Camera
{
    /// <summary>
    /// Pure yaw/pitch/zoom/focus state for the reference isometric-ish camera rig, independent of any actual
    /// Transform or input source so it's unit-testable and swappable. <see cref="IsometricCameraRig"/> is the
    /// only thing that reads this to move real transforms.
    /// </summary>
    public sealed class CameraRigState
    {
        private readonly CRPGCameraSettings settings;
        private Rect? boundsXZ;
        private bool manualOverrideActive;

        public CameraRigState(CRPGCameraSettings settings)
        {
            this.settings = settings;
            BasePitch = Mathf.Clamp(settings.DefaultPitch, settings.MinimumPitch, settings.MaximumPitch);
            Zoom = Mathf.Clamp(settings.DefaultZoom, settings.MinimumZoom, settings.MaximumZoom);
            scaleIndex = NearestScaleIndex(settings.DefaultScale);
        }

        private int scaleIndex;
        private float viewportHeightPixels;
        private float viewportAspect = 16f / 9f;

        public Vector3 FocusPoint { get; private set; }
        public float Yaw { get; private set; }
        /// <summary>The pitch at the default zoom (and when the settings have no close-up offset).</summary>
        public float BasePitch { get; }

        /// <summary>The pitch at the current zoom: <see cref="PitchFor"/>.</summary>
        public float Pitch => PitchFor(Zoom);

        /// <summary>The pitch for a camera distance: the base pitch, lowered by <see cref="CRPGCameraSettings.CloseUpPitchOffset"/> as the camera comes in from the default zoom to the minimum (so zooming in looks more level at the characters), clamped to the pitch limits. Fixed in orthographic mode.</summary>
        public float PitchFor(float zoom)
        {
            if (settings.Orthographic || Mathf.Approximately(settings.CloseUpPitchOffset, 0f)) return BasePitch;
            var closeness = 1f - Mathf.InverseLerp(settings.MinimumZoom, Mathf.Max(settings.MinimumZoom + 0.01f, settings.DefaultZoom), zoom);
            return Mathf.Clamp(BasePitch + settings.CloseUpPitchOffset * Mathf.Clamp01(closeness), settings.MinimumPitch, settings.MaximumPitch);
        }

        /// <summary>What a mouse-wheel reading means for <see cref="ApplyZoom"/>: scrolling UP zooms IN. The reading is normalised to notches first. A perspective rig zooms in by shrinking the camera distance (a negative delta); the orthographic pixel rig steps its scale up (a positive one).</summary>
        public static float WheelToZoomDelta(bool orthographic, float wheel)
        {
            // Windows reports a wheel notch as +-120 (other platforms and touchpads report small fractions): normalise to NOTCHES, at most three per event, or one notch would fling the camera to its far limit
            var notches = Mathf.Clamp(Mathf.Abs(wheel) >= 10f ? wheel / 120f : wheel, -3f, 3f);
            return orthographic ? notches : -notches;
        }
        public float Zoom { get; private set; }

        /// <summary>True while a manual pan is in effect -- <see cref="FollowTarget"/> no-ops until
        /// <see cref="ResumeFollow"/> is called (an explicit Recenter command) or <see cref="SnapTo"/> runs
        /// (a teleport always resumes following). Manual pan wins; it is never silently pulled back.</summary>
        public bool IsManualOverrideActive => manualOverrideActive;

        /// <summary>Reserved seam for ground-height following (Docs/Design's "terrain handling") -- see
        /// <see cref="IGroundHeightProvider"/>'s own remarks. Null (default) leaves FocusPoint.y untouched.</summary>
        public IGroundHeightProvider GroundHeightProvider { get; set; }

        /// <summary>Constrains FocusPoint's XZ to this world-space rect from now on (re-applied on every
        /// subsequent mutation) -- null (default) means unclamped, today's behavior.</summary>
        public void SetBounds(Rect? worldBoundsXZ) => boundsXZ = worldBoundsXZ;

        /// <summary>Sets the focus point directly -- kept for existing callers (e.g. an initial one-time
        /// placement) as a synonym for <see cref="SnapTo"/>: same bounds/height application, same "always
        /// resumes following" behavior.</summary>
        public void FocusOn(Vector3 point) => SnapTo(point);

        /// <summary>Instantly moves the focus point to <paramref name="point"/> -- the explicit "this was a
        /// teleport/load, don't fly across the map" entry point the design doc calls for. Always clears
        /// <see cref="IsManualOverrideActive"/>: a snap is a deliberate re-center, not something a manual pan
        /// should keep overriding.</summary>
        public void SnapTo(Vector3 point)
        {
            FocusPoint = ApplyBoundsAndHeight(point);
            manualOverrideActive = false;
        }

        /// <summary>Smoothly moves the focus point toward <paramref name="point"/> (framerate-independent
        /// exponential smoothing, rate from <see cref="CRPGCameraSettings.FollowSmoothingSpeed"/>) -- the
        /// "smoothed LateUpdate follow" the design doc calls for. A no-op while a manual pan is overriding
        /// (<see cref="IsManualOverrideActive"/>); call <see cref="ResumeFollow"/> first.</summary>
        public void FollowTarget(Vector3 point, float deltaTime)
        {
            if (manualOverrideActive) return;

            var t = 1f - Mathf.Exp(-settings.FollowSmoothingSpeed * deltaTime);
            FocusPoint = ApplyBoundsAndHeight(Vector3.Lerp(FocusPoint, point, t));
        }

        /// <summary>The explicit Recenter command: releases a manual pan's override so <see cref="FollowTarget"/>
        /// resumes on the next call. A no-op if no override is active.</summary>
        public void ResumeFollow() => manualOverrideActive = false;

        /// <summary>Moves the focus point along the camera's flattened forward/right axes (ignores pitch),
        /// e.g. for WASD/edge-pan. Sets <see cref="IsManualOverrideActive"/> -- a manual pan always suspends
        /// follow until an explicit <see cref="ResumeFollow"/>, never a silent persistent offset.</summary>
        public void Pan(Vector2 input, float deltaTime)
        {
            var yawRotation = Quaternion.Euler(0f, Yaw, 0f);
            var right = yawRotation * Vector3.right;
            var forward = yawRotation * Vector3.forward;
            var moved = FocusPoint + (right * input.x + forward * input.y) * (settings.PanSpeed * deltaTime);

            FocusPoint = ApplyBoundsAndHeight(moved);
            manualOverrideActive = true;
        }

        /// <summary>Continuous rotation at the settings' rotation speed (a held key or button; the default input map has none now: middle-drag uses <see cref="RotateByDegrees"/>).</summary>
        public void Rotate(float direction, float deltaTime)
        {
            Yaw = Mathf.Repeat(Yaw + direction * settings.RotationSpeed * deltaTime, 360f);
        }

        /// <summary>Rotates by an exact number of degrees (a mouse drag), wrapped into one turn.</summary>
        public void RotateByDegrees(float degrees) => Yaw = Mathf.Repeat(Yaw + degrees, 360f);

        /// <summary>Discrete jump by SnapRotationDegrees.</summary>
        public void SnapRotate(float direction)
        {
            Yaw = Mathf.Repeat(Yaw + settings.SnapRotationDegrees * Mathf.Sign(direction), 360f);
        }

        /// <summary>Sets the yaw to an absolute angle (wrapped into one turn), e.g. to restore a view after a scripted camera. Not a re-centre: it does not touch the follow override.</summary>
        public void SetYaw(float degrees) => Yaw = Mathf.Repeat(degrees, 360f);

        /// <summary>Sets the perspective zoom (the camera distance parameter) to an absolute value inside the settings' limits. Ignored in orthographic mode, where zoom is the integer scale list (use <see cref="StepScale"/>).</summary>
        public void SetZoom(float zoom)
        {
            if (settings.Orthographic) return;
            Zoom = Mathf.Clamp(zoom, settings.MinimumZoom, settings.MaximumZoom);
        }

        public void ApplyZoom(float delta)
        {
            if (settings.Orthographic)
            {
                StepScale(delta > 0f ? 1 : delta < 0f ? -1 : 0); // one notch = one integer scale step; the camera distance never changes
                return;
            }

            Zoom = Mathf.Clamp(Zoom + delta * settings.ZoomSpeed, settings.MinimumZoom, settings.MaximumZoom);
        }

        // ---------------------------------------------------------------------------------------------
        // Orthographic pixel-art mode (2D campaign). Everything below is inert unless settings.Orthographic.
        // ---------------------------------------------------------------------------------------------

        public bool Orthographic => settings.Orthographic;

        /// <summary>The current integer zoom scale (screen pixels per source pixel). 0 when the settings list no scales.</summary>
        public int Scale => settings.ZoomScales != null && settings.ZoomScales.Count > 0 ? settings.ZoomScales[scaleIndex] : 0;

        /// <summary>Camera.orthographicSize that makes one source pixel exactly <see cref="Scale"/> screen pixels tall for the viewport last given to
        /// <see cref="SetViewport"/>. 0 unless orthographic, until a viewport is known, or when there is no scale.</summary>
        public float OrthographicSize => settings.Orthographic && Scale > 0 && viewportHeightPixels > 0f ? viewportHeightPixels / (2f * settings.PixelsPerUnit * Scale) : 0f;

        /// <summary>Half the visible world rectangle (x = half width, y = half depth) at the current scale, or zero when unknown.</summary>
        public Vector2 HalfExtents => new(OrthographicSize * viewportAspect, OrthographicSize);

        /// <summary>The viewport the camera renders into (pixel height and width/height aspect). Re-applies the bounds clamp when it changes, so a
        /// map is never shown off-centre because the window was resized.</summary>
        public void SetViewport(float heightPixels, float aspect)
        {
            if (heightPixels <= 0f || aspect <= 0f) return;
            if (Mathf.Approximately(heightPixels, viewportHeightPixels) && Mathf.Approximately(aspect, viewportAspect)) return;

            viewportHeightPixels = heightPixels;
            viewportAspect = aspect;
            FocusPoint = ApplyBoundsAndHeight(FocusPoint);
        }

        /// <summary>Steps one zoom scale in (+1) or out (-1), clamped to the list ends. Re-clamps the focus, since the visible area changed.</summary>
        public void StepScale(int direction)
        {
            if (settings.ZoomScales == null || settings.ZoomScales.Count == 0 || direction == 0) return;
            scaleIndex = Mathf.Clamp(scaleIndex + (direction > 0 ? 1 : -1), 0, settings.ZoomScales.Count - 1);
            FocusPoint = ApplyBoundsAndHeight(FocusPoint);
        }

        /// <summary>The focus point as it should be handed to the camera transform: with pixel snapping on, X and Z are rounded to whole screen pixels
        /// (1 / (pixelsPerUnit * scale) world units). Presentation only: <see cref="FocusPoint"/> stays the unsnapped source of truth, so snapping
        /// can never drift the follow.</summary>
        public Vector3 PresentationFocus
        {
            get
            {
                if (!settings.Orthographic || !settings.PixelSnapFocus || Scale <= 0) return FocusPoint;
                var step = 1f / (settings.PixelsPerUnit * Scale);
                return new Vector3(Mathf.Round(FocusPoint.x / step) * step, FocusPoint.y, Mathf.Round(FocusPoint.z / step) * step);
            }
        }

        private int NearestScaleIndex(int wanted)
        {
            var scales = settings.ZoomScales;
            if (scales == null || scales.Count == 0) return 0;
            var best = 0;
            for (var i = 1; i < scales.Count; i++)
                if (Mathf.Abs(scales[i] - wanted) < Mathf.Abs(scales[best] - wanted)) best = i;
            return best;
        }

        // With a half extent (orthographic: half the visible size) the VISIBLE rectangle stays inside [min, max]; when the map is smaller than the view on
        // this axis the camera centres on the map instead. With half = 0 this is the original plain clamp of the focus point.
        private static float ClampAxis(float value, float min, float max, float half)
        {
            if (half <= 0f) return Mathf.Clamp(value, min, max);
            if (max - min <= 2f * half) return (min + max) * 0.5f;
            return Mathf.Clamp(value, min + half, max - half);
        }

        // Fixed order, always: clamp XZ against bounds FIRST, then sample ground height at that
        // already-clamped XZ (not the pre-clamp position) if a provider is set. A provider returning false
        // leaves Y exactly as it was going into this call -- never zeroed or guessed at.
        private Vector3 ApplyBoundsAndHeight(Vector3 point)
        {
            if (boundsXZ.HasValue)
            {
                var rect = boundsXZ.Value;
                var half = settings.Orthographic ? HalfExtents : Vector2.zero;
                point.x = ClampAxis(point.x, rect.xMin, rect.xMax, half.x);
                point.z = ClampAxis(point.z, rect.yMin, rect.yMax, half.y);
            }

            if (GroundHeightProvider != null && GroundHeightProvider.TryGetGroundHeight(point, out var height))
                point.y = height;

            return point;
        }
    }
}
