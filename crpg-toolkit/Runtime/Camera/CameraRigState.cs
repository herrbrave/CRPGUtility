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

        public void FocusOn(Vector3 point) => FocusPoint = point;

        /// <summary>Moves the focus point along the camera's flattened forward/right axes (ignores pitch),
        /// e.g. for WASD/middle-drag panning.</summary>
        public void Pan(Vector2 input, float deltaTime)
        {
            var yawRotation = Quaternion.Euler(0f, Yaw, 0f);
            var right = yawRotation * Vector3.right;
            var forward = yawRotation * Vector3.forward;
            FocusPoint += (right * input.x + forward * input.y) * (settings.PanSpeed * deltaTime);
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
    }
}
