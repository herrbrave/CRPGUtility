using System;
using CRPG.UI;
using UnityEngine;
using UnityEngine.InputSystem;

namespace VikingRaid.Game
{
    /// <summary>
    /// The game camera: a perspective camera behind whatever <see cref="Follow"/> returns (the centre of the selected party members), looking along the road, with free control:
    /// <b>W/A/S/D pan, Q/E rotate, mouse wheel zoom, Space recenter</b> (the toolkit's default camera bindings; <see cref="CameraController"/> holds the rules). Panning detaches the camera
    /// until Recenter. In a fight <see cref="TacticalFocus"/> returns the action's middle and the camera eases back and up to frame it; a fight starting or ending clears any pan so the
    /// arena is always framed, and the player can still look around during it. It replaces the Task 3 placeholder camera, so the world's global post-processing volume is enabled on it.
    /// </summary>
    [RequireComponent(typeof(Camera))]
    public sealed class VikingCamera : MonoBehaviour
    {
        [SerializeField] private float explorationDistance = 17f, explorationPitch = 42f, tacticalDistance = 31f, tacticalPitch = 48f, smoothTime = 0.35f;

        private readonly CameraController controller = new();
        private Vector3 velocity, focus;
        private float yaw, yawVelocity, distance, distanceVelocity, pitch, pitchVelocity;
        private bool initialised, wasTactical;
        private int cachedRoadSample = -1;
        private readonly UIToolkitPointerCapture pointerCapture = new();

        public Func<Vector3?> Follow { get; set; }
        public Func<Vector3?> TacticalFocus { get; set; }

        /// <summary>Tests replace the keyboard and mouse with this.</summary>
        public Func<CameraInput> InputOverride { get; set; }

        public CameraController Controller => controller;
        public Vector3 Focus => focus;
        public bool Tactical => TacticalFocus?.Invoke() != null;

        private void LateUpdate()
        {
            var tactical = TacticalFocus?.Invoke();
            var followTarget = tactical ?? Follow?.Invoke();
            if (followTarget == null) return;

            // a fight starting or ending frames the situation, whatever the player had done with the camera
            if (tactical.HasValue != wasTactical) { controller.ClearForSituationChange(); wasTactical = tactical.HasValue; }

            if (!initialised) focus = followTarget.Value;
            var baseYaw = RoadYaw(focus);
            if (!initialised) yaw = baseYaw;
            controller.Step(Time.unscaledDeltaTime, ReadInput(), focus, baseYaw);
            var target = controller.DesiredFocus(followTarget.Value);

            var wantYaw = baseYaw + controller.YawOffset;
            var wantDistance = (tactical.HasValue ? tacticalDistance : explorationDistance) * controller.Zoom;
            var wantPitch = tactical.HasValue ? tacticalPitch : explorationPitch;

            if (!initialised) { distance = wantDistance; pitch = wantPitch; initialised = true; }
            focus = Vector3.SmoothDamp(focus, target, ref velocity, smoothTime, Mathf.Infinity, Time.unscaledDeltaTime);
            yaw = Mathf.SmoothDampAngle(yaw, wantYaw, ref yawVelocity, smoothTime * 1.5f, Mathf.Infinity, Time.unscaledDeltaTime);
            distance = Mathf.SmoothDamp(distance, wantDistance, ref distanceVelocity, smoothTime * 1.5f, Mathf.Infinity, Time.unscaledDeltaTime);
            pitch = Mathf.SmoothDamp(pitch, wantPitch, ref pitchVelocity, smoothTime * 1.5f, Mathf.Infinity, Time.unscaledDeltaTime);

            var rotation = Quaternion.Euler(pitch, yaw, 0f);
            transform.SetPositionAndRotation(focus + Vector3.up * 1.2f - rotation * Vector3.forward * distance, rotation);
        }

        private CameraInput ReadInput()
        {
            if (InputOverride != null) return InputOverride();

            var input = new CameraInput();
            var keyboard = Keyboard.current;
            if (keyboard != null)
            {
                input.Pan = new Vector2((keyboard.dKey.isPressed ? 1f : 0f) - (keyboard.aKey.isPressed ? 1f : 0f), (keyboard.wKey.isPressed ? 1f : 0f) - (keyboard.sKey.isPressed ? 1f : 0f));
                input.Rotate = (keyboard.eKey.isPressed ? 1f : 0f) - (keyboard.qKey.isPressed ? 1f : 0f);
                input.Recenter = keyboard.spaceKey.wasPressedThisFrame;
            }

            var mouse = Mouse.current;
            if (mouse != null && !pointerCapture.IsPointerOverUI(mouse.position.ReadValue())) input.Zoom = mouse.scroll.ReadValue().y;
            return input;
        }

        /// <summary>The yaw (degrees) that looks along the road at the point of the road nearest <paramref name="position"/>.</summary>
        private float RoadYaw(Vector3 position)
        {
            var best = float.MaxValue; var bestS = 0f;
            var count = (int)(WorldLayout.RoadLength / 2f);
            var start = cachedRoadSample < 0 ? 0 : Mathf.Max(0, cachedRoadSample - 20);
            var end = cachedRoadSample < 0 ? count : Mathf.Min(count, cachedRoadSample + 20);
            for (var i = start; i <= end; i++)
            {
                var d = WorldLayout.PointAt(i * 2f) - position; d.y = 0f;
                if (d.sqrMagnitude < best) { best = d.sqrMagnitude; bestS = i * 2f; cachedRoadSample = i; }
            }

            var tangent = WorldLayout.TangentAt(bestS);
            return Mathf.Atan2(tangent.x, tangent.z) * Mathf.Rad2Deg;
        }
    }
}
