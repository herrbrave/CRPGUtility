using System;
using CRPG.Camera;
using CRPG.Core;
using CRPG.Gameplay;
using CRPG.UI;
using UnityEngine;
using UnityEngine.InputSystem;

namespace CRPG.InputSystem
{
    /// <summary>
    /// The real Input System adapter: reads the generated <see cref="CRPGInputActions"/> wrapper's callbacks
    /// (not raw <c>UnityEngine.Input</c> polling -- that's what the FighterDemo's legacy
    /// <c>MouseClickCommandSource</c> uses) and forwards every one of them into a <see cref="PlayerCommandRouter"/>
    /// (click/drag/select/move/interact/cancel) or directly onto an optional <see cref="IsometricCameraRig"/>
    /// (pan/zoom/rotate/recenter). This class stays deliberately thin for the former; the latter is genuinely
    /// this class's own job since a camera rig isn't part of the click-resolution pipeline.
    /// </summary>
    [AddComponentMenu("CRPG/Input System Command Source")]
    public sealed class InputSystemCommandSource : MonoBehaviour, IPlayerCommandSource, CRPGInputActions.IExplorationActions
    {
        [SerializeField] private UnityEngine.Camera raycastCamera;
        [SerializeField] private LayerMask hittableMask = ~0;
        [SerializeField] private float dragThresholdPixels = 10f;
        [SerializeField] private IsometricCameraRig cameraRig;

        private CRPGInputActions actions;
        private PlayerCommandRouter router;
        private readonly IPointerCaptureProvider pointerCapture = new UIToolkitPointerCapture();
        private Vector2 lastPointerPosition;
        private Vector2 heldPanInput;
        private float heldRotateInput;

        // Lazily constructed rather than only in Awake: [SerializeField] values are already deserialized by
        // the time ANY code can run against this instance, but Unity does not guarantee this component's own
        // Awake() runs before another script's Awake/Start tries to subscribe to Select/Move/Interact/Cancel
        // below. A lazy, idempotent accessor makes that ordering irrelevant instead of risking a
        // NullReferenceException on an out-of-order subscription.
        private PlayerCommandRouter Router =>
            router ??= new PlayerCommandRouter(new SceneSelectableSource(), pointerCapture, hittableMask, dragThresholdPixels);

        public event Action<SelectCommand> Select { add => Router.Select += value; remove => Router.Select -= value; }
        public event Action<MoveCommand> Move { add => Router.Move += value; remove => Router.Move -= value; }
        public event Action<InteractCommand> Interact { add => Router.Interact += value; remove => Router.Interact -= value; }
        public event Action Cancel { add => Router.Cancel += value; remove => Router.Cancel -= value; }

        private UnityEngine.Camera ActiveCamera => raycastCamera != null ? raycastCamera : UnityEngine.Camera.main;

        private void Awake()
        {
            actions = new CRPGInputActions();
            actions.Exploration.SetCallbacks(this);
        }

        private void OnEnable() => actions.Exploration.Enable();

        private void OnDisable()
        {
            actions.Exploration.Disable();

            // A disabled-then-re-enabled adapter must never resume a stale pan/rotate from before it was
            // disabled -- there is no "canceled" callback guaranteed to fire on disable for a still-held key.
            heldPanInput = Vector2.zero;
            heldRotateInput = 0f;
        }

        private void OnDestroy() => actions?.Dispose();

        // Held camera input (Pan, Rotate) and edge-pan are applied here, every frame, rather than only inside
        // their callbacks -- a PassThrough action's callback fires when its VALUE changes, not continuously
        // while held steady, so callback-only application would move the camera only at the instant a key is
        // pressed/released, not for the whole duration it's held. The UI/modal check is re-evaluated fresh
        // every frame too, so a modal opening mid-hold stops camera movement on the very next frame rather
        // than only the next time the input itself changes.
        private void Update()
        {
            if (cameraRig == null) return;
            if (pointerCapture.IsPointerOverUI(lastPointerPosition)) return;

            var pan = heldPanInput + ComputeEdgePan();
            if (pan != Vector2.zero) cameraRig.Rig.Pan(pan, Time.deltaTime);

            if (heldRotateInput != 0f) cameraRig.Rig.Rotate(heldRotateInput, Time.deltaTime);
        }

        private Vector2 ComputeEdgePan()
        {
            var settings = cameraRig.Settings;
            if (settings == null || !settings.EnableEdgeScrolling) return Vector2.zero;

            var margin = settings.EdgeScrollMarginPixels;
            var pan = Vector2.zero;
            if (lastPointerPosition.x <= margin) pan.x -= 1f;
            else if (lastPointerPosition.x >= Screen.width - margin) pan.x += 1f;
            if (lastPointerPosition.y <= margin) pan.y -= 1f;
            else if (lastPointerPosition.y >= Screen.height - margin) pan.y += 1f;
            return pan;
        }

        public void OnPoint(InputAction.CallbackContext context)
        {
            lastPointerPosition = context.ReadValue<Vector2>();
            Router.HandlePointerMove(lastPointerPosition);
        }

        public void OnClick(InputAction.CallbackContext context)
        {
            var camera = ActiveCamera;
            if (camera == null) return;

            if (context.started)
            {
                Router.HandlePointerDown(lastPointerPosition, camera.ScreenPointToRay(lastPointerPosition));
            }
            else if (context.canceled)
            {
                var shiftHeld = actions.Exploration.ShiftModifier.IsPressed();
                Router.HandlePointerUp(lastPointerPosition, camera.ScreenPointToRay(lastPointerPosition), camera, shiftHeld);
            }
        }

        public void OnCancel(InputAction.CallbackContext context)
        {
            if (context.performed) Router.HandleCancel();
        }

        // ShiftModifier is read on demand (IsPressed(), above) rather than acted on here -- it's a modifier,
        // not an independent gesture.
        public void OnShiftModifier(InputAction.CallbackContext context) { }

        // Held, continuous -- see Update()'s own remarks. Cleared on `canceled` (key released) so Update()
        // stops applying it immediately rather than on the next value-changed event.
        public void OnPan(InputAction.CallbackContext context) =>
            heldPanInput = context.canceled ? Vector2.zero : context.ReadValue<Vector2>();

        public void OnRotate(InputAction.CallbackContext context) =>
            heldRotateInput = context.canceled ? 0f : context.ReadValue<float>();

        // Zoom stays callback-driven (unlike Pan/Rotate): the mouse-scroll binding delivers a per-tick delta
        // that resets between reads -- a genuine discrete event, not something that needs continuous
        // per-frame re-application while "held."
        public void OnZoom(InputAction.CallbackContext context)
        {
            if (cameraRig == null) return;
            if (pointerCapture.IsPointerOverUI(lastPointerPosition)) return;

            var delta = context.ReadValue<float>();
            if (delta != 0f) cameraRig.Rig.ApplyZoom(delta);
        }

        public void OnRecenter(InputAction.CallbackContext context)
        {
            if (context.performed) cameraRig?.Rig.ResumeFollow();
        }

        // Reserved -- see CRPGInputActionsAuthor's remarks. No ability-targeting system exists yet.
        public void OnAbilityTarget(InputAction.CallbackContext context) { }
    }
}
