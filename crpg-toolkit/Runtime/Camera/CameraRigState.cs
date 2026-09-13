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
            Pitch = Mathf.Clamp(settings.DefaultPitch, settings.MinimumPitch, settings.MaximumPitch);
            Zoom = Mathf.Clamp(settings.DefaultZoom, settings.MinimumZoom, settings.MaximumZoom);
        }

        public Vector3 FocusPoint { get; private set; }
        public float Yaw { get; private set; }
        public float Pitch { get; }
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

        /// <summary>Continuous rotation, e.g. holding Q/E.</summary>
        public void Rotate(float direction, float deltaTime)
        {
            Yaw = Mathf.Repeat(Yaw + direction * settings.RotationSpeed * deltaTime, 360f);
        }

        /// <summary>Discrete jump by SnapRotationDegrees.</summary>
        public void SnapRotate(float direction)
        {
            Yaw = Mathf.Repeat(Yaw + settings.SnapRotationDegrees * Mathf.Sign(direction), 360f);
        }

        public void ApplyZoom(float delta)
        {
            Zoom = Mathf.Clamp(Zoom + delta * settings.ZoomSpeed, settings.MinimumZoom, settings.MaximumZoom);
        }

        // Fixed order, always: clamp XZ against bounds FIRST, then sample ground height at that
        // already-clamped XZ (not the pre-clamp position) if a provider is set. A provider returning false
        // leaves Y exactly as it was going into this call -- never zeroed or guessed at.
        private Vector3 ApplyBoundsAndHeight(Vector3 point)
        {
            if (boundsXZ.HasValue)
            {
                var rect = boundsXZ.Value;
                point.x = Mathf.Clamp(point.x, rect.xMin, rect.xMax);
                point.z = Mathf.Clamp(point.z, rect.yMin, rect.yMax);
            }

            if (GroundHeightProvider != null && GroundHeightProvider.TryGetGroundHeight(point, out var height))
                point.y = height;

            return point;
        }
    }
}
