using System.Collections.Generic;
using UnityEngine;

namespace CRPG.Camera
{
    /// <summary>Tunable defaults/limits for the reference isometric-ish camera rig. Plain engine
    /// configuration, not ruleset content -- doesn't extend DefinitionAsset or carry a DefinitionId.</summary>
    [CreateAssetMenu(menuName = "CRPG Toolkit/Camera/Camera Settings")]
    public sealed class CRPGCameraSettings : ScriptableObject
    {
        [SerializeField] private float panSpeed = 12f;
        [SerializeField] private float rotationSpeed = 90f;
        [SerializeField] private float zoomSpeed = 5f;
        [SerializeField] private float minimumZoom = 5f;
        [SerializeField] private float maximumZoom = 20f;
        [SerializeField] private float minimumPitch = 30f;
        [SerializeField] private float maximumPitch = 70f;
        [SerializeField] private float defaultPitch = 55f;
        [SerializeField] private float defaultZoom = 12f;
        [SerializeField] private float snapRotationDegrees = 45f;
        [SerializeField] private bool enableEdgeScrolling;
        [SerializeField] private bool followSelection = true;
        [SerializeField] private float followSmoothingSpeed = 8f;
        [SerializeField] private float edgeScrollMarginPixels = 20f;

        [Header("Orthographic pixel-art mode (2D campaign). Off by default: every field below is ignored unless Orthographic is on.")]
        [Tooltip("Drive an orthographic camera by integer pixel scale instead of by distance. Yaw should stay locked (rotationSpeed and snapRotationDegrees 0) so sprite sorting holds.")]
        [SerializeField] private bool orthographic;
        [SerializeField] private int pixelsPerUnit = 16;
        [Tooltip("The integer zoom steps, ascending: one source pixel is this many screen pixels.")]
        [SerializeField] private int[] zoomScales = { 2, 3, 4, 5, 6 };
        [SerializeField] private int defaultScale = 3;
        [Tooltip("Round the position handed to the camera to whole screen pixels so sprites never shimmer while the smoothed follow moves.")]
        [SerializeField] private bool pixelSnapFocus = true;

        public float PanSpeed => panSpeed;
        public float RotationSpeed => rotationSpeed;
        public float ZoomSpeed => zoomSpeed;
        public float MinimumZoom => minimumZoom;
        public float MaximumZoom => maximumZoom;
        public float MinimumPitch => minimumPitch;
        public float MaximumPitch => maximumPitch;
        public float DefaultPitch => defaultPitch;
        public float DefaultZoom => defaultZoom;
        public float SnapRotationDegrees => snapRotationDegrees;
        public bool EnableEdgeScrolling => enableEdgeScrolling;
        public bool FollowSelection => followSelection;
        public float FollowSmoothingSpeed => followSmoothingSpeed;
        public float EdgeScrollMarginPixels => edgeScrollMarginPixels;
        public bool Orthographic => orthographic;
        public int PixelsPerUnit => Mathf.Max(1, pixelsPerUnit);
        public IReadOnlyList<int> ZoomScales => zoomScales;
        public int DefaultScale => defaultScale;
        public bool PixelSnapFocus => pixelSnapFocus;
    }
}
