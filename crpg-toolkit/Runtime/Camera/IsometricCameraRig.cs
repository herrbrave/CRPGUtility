using UnityEngine;

namespace CRPG.Camera
{
    /// <summary>
    /// Reference isometric-ish camera rig: CameraRoot (this transform) -&gt; YawPivot -&gt; PitchPivot -&gt; Camera.
    /// All actual pan/rotate/zoom/focus logic lives in <see cref="CameraRigState"/> so it's testable without a
    /// scene; this component only copies that state onto the transform hierarchy every frame. It reads no
    /// input itself -- an Integrations/InputSystem adapter (or any other input source) calls Pan/Rotate/Zoom/
    /// FocusOn on <see cref="Rig"/> directly, so this assembly never depends on the Input System package.
    /// </summary>
    public sealed class IsometricCameraRig : MonoBehaviour
    {
        [SerializeField] private Transform yawPivot;
        [SerializeField] private Transform pitchPivot;
        [SerializeField] private Transform cameraTransform;
        [SerializeField] private CRPGCameraSettings settings;

        public CameraRigState Rig { get; private set; }

        /// <summary>The settings this rig was built from -- exposed so an input adapter can read
        /// configuration (e.g. <see cref="CRPGCameraSettings.EnableEdgeScrolling"/>) without needing its own
        /// separate reference to the same asset.</summary>
        public CRPGCameraSettings Settings => settings;

        private void Awake()
        {
            Rig = new CameraRigState(settings);
            Rig.FocusOn(transform.position);
            Apply();
        }

        private void LateUpdate() => Apply();

        private UnityEngine.Camera cachedCamera;

        // Orthographic pixel-art mode: hand the rig the viewport being rendered (the render target's height when one is set, else the screen), then
        // make the camera's orthographicSize match the integer scale. The camera stays at its fixed distance (Zoom): scale, not distance, is the zoom.
        private void ApplyOrthographic()
        {
            if (cachedCamera == null) cachedCamera = cameraTransform.GetComponent<UnityEngine.Camera>();
            if (cachedCamera == null) return;

            var height = cachedCamera.targetTexture != null ? cachedCamera.targetTexture.height : cachedCamera.pixelHeight;
            var width = cachedCamera.targetTexture != null ? cachedCamera.targetTexture.width : cachedCamera.pixelWidth;
            if (height > 0) Rig.SetViewport(height, (float)width / height);

            cachedCamera.orthographic = true;
            var size = Rig.OrthographicSize;
            if (size > 0f) cachedCamera.orthographicSize = size;
        }

        private void Apply()
        {
            if (settings.Orthographic) ApplyOrthographic();

            transform.position = settings.Orthographic ? Rig.PresentationFocus : Rig.FocusPoint;
            yawPivot.localRotation = Quaternion.Euler(0f, Rig.Yaw, 0f);
            pitchPivot.localRotation = Quaternion.Euler(Rig.Pitch, 0f, 0f);
            cameraTransform.localPosition = Vector3.back * Rig.Zoom;
        }
    }
}
