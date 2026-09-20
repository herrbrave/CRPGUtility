using System;
using UnityEngine;

namespace BarrowsEnd.Gate
{
    /// <summary>
    /// Throwaway camera for the Phase 2 gate scene: orthographic, straight down (pitch 90, yaw 0: the sorting contract needs yaw 0), following
    /// whatever <see cref="Follow"/> returns, at an INTEGER pixel scale (one source pixel = <see cref="Scale"/> screen pixels) by driving
    /// orthographicSize. The real authored camera (yaw lock, zoom steps, bounds) is Phase 3; note that the toolkit's IsometricCameraRig.Zoom
    /// is a camera DISTANCE and does nothing for an orthographic camera.
    /// </summary>
    [RequireComponent(typeof(Camera))]
    public sealed class GateCamera : MonoBehaviour
    {
        public const int PixelsPerUnit = 16;
        public const int MinScale = 2;
        public const int MaxScale = 8;
        private const float Height = 20f;

        [SerializeField, Range(MinScale, MaxScale)] private int scale = 4;

        private Camera cam;

        /// <summary>World position to centre on; null keeps the camera where it is.</summary>
        public Func<Vector3?> Follow { get; set; }

        public int Scale => scale;
        public float OrthographicSize => ViewportHeight / (2f * PixelsPerUnit * scale);

        /// <summary>Pixel height being rendered into: the render target if one is set (a test capture), else the screen.</summary>
        private float ViewportHeight => cam != null && cam.targetTexture != null ? cam.targetTexture.height : Screen.height;

        public void SetScale(int newScale) => scale = Mathf.Clamp(newScale, MinScale, MaxScale);
        public void ZoomIn() => SetScale(scale + 1);
        public void ZoomOut() => SetScale(scale - 1);

        private void Awake()
        {
            cam = GetComponent<Camera>();
            cam.orthographic = true;
        }

        private void LateUpdate()
        {
            if (cam == null) cam = GetComponent<Camera>();
            cam.orthographic = true;
            cam.orthographicSize = OrthographicSize;

            var target = Follow?.Invoke();
            var position = target ?? new Vector3(transform.position.x, 0f, transform.position.z);
            transform.SetPositionAndRotation(new Vector3(position.x, Height, position.z), Quaternion.Euler(90f, 0f, 0f));
        }
    }
}
