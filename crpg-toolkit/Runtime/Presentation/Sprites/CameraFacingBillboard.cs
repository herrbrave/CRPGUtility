using UnityEngine;

namespace CRPG.Presentation.Sprites
{
    /// <summary>
    /// Keeps a sprite card parallel to the camera plane: world rotation = camera rotation, every frame, after the
    /// camera rig has moved (<c>[DefaultExecutionOrder(100)]</c>; IsometricCameraRig runs at order 0). A full billboard,
    /// valid for any pitch/yaw, unmirrored. At a top-down pitch of 90 the card lies flat with its up direction along
    /// world +Z and faces away from the camera, so it needs a double-sided (Cull Off) material such as Sprites-Default.
    /// <para>Rotation only -- position and scale are never touched. The parent's rotation (FacingController turns
    /// CharacterRoot) is overwritten in world space, which is the intended decoupling.</para>
    /// </summary>
    [DefaultExecutionOrder(100)]
    public sealed class CameraFacingBillboard : MonoBehaviour
    {
        [Tooltip("Optional. Leave empty to follow Camera.main. Set for split-screen or an offscreen preview camera.")]
        [SerializeField] private Camera cameraOverride;

        private Camera resolved;
        private bool warnedNoCamera;

        public Camera CameraOverride { get => cameraOverride; set => cameraOverride = value; }

        private void LateUpdate()
        {
            var cam = cameraOverride != null ? cameraOverride : ResolveMain();
            if (cam == null)
            {
                if (!warnedNoCamera)
                {
                    warnedNoCamera = true;
                    Debug.LogWarning($"[CameraFacingBillboard] No camera to face (no override and no Camera.main) on '{name}'.", this);
                }
                return;
            }

            warnedNoCamera = false;
            transform.rotation = cam.transform.rotation;
        }

        // A destroyed Camera is not reference-null, so this uses Unity's overloaded == throughout.
        private Camera ResolveMain()
        {
            if (resolved == null) resolved = Camera.main;
            return resolved;
        }
    }
}
