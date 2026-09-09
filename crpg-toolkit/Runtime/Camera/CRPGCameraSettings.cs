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
    }
}
