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

        private void Awake()
        {
            Rig = new CameraRigState(settings);
            Rig.FocusOn(transform.position);
            Apply();
        }

        private void LateUpdate() => Apply();

        private void Apply()
        {
            transform.position = Rig.FocusPoint;
            yawPivot.localRotation = Quaternion.Euler(0f, Rig.Yaw, 0f);
            pitchPivot.localRotation = Quaternion.Euler(Rig.Pitch, 0f, 0f);
            cameraTransform.localPosition = Vector3.back * Rig.Zoom;
        }
    }
}
