using UnityEngine;

namespace VikingRaid.Game
{
    /// <summary>One frame of camera input: pan (x right, y forward, both -1..1), rotate (-1..1), a zoom tick (+ in, - out) and whether Recenter was pressed.</summary>
    public struct CameraInput
    {
        public Vector2 Pan;
        public float Rotate;
        public float Zoom;
        public bool Recenter;
    }

    /// <summary>
    /// The camera's control rules, with no Unity objects (so they are unit-tested): WASD pans relative to where the camera is looking, Q/E rotate, the wheel zooms, Space recenters
    /// (the toolkit's own default bindings). Panning detaches the camera from whatever it was following; it stays where the player put it until Recenter, or until the situation changes
    /// (a fight starting or ending clears it, so the arena is always framed). The focus cannot be panned further than <see cref="MaxDistanceFromWorld"/> metres from the walkable ground.
    /// </summary>
    public sealed class CameraController
    {
        public const float PanSpeed = 16f, RotateDegreesPerSecond = 90f, MinZoom = 0.28f, MaxZoom = 1.9f, ZoomStep = 0.08f, MaxDistanceFromWorld = 45f;

        private Vector3 manualFocus;

        public bool ManualOverride { get; private set; }
        public float YawOffset { get; private set; }
        public float Zoom { get; private set; } = 1f;

        /// <summary>The world position the camera should look at: the manual position if the player panned away, else what the game wants it to follow.</summary>
        public Vector3 DesiredFocus(Vector3 followTarget) => ManualOverride ? manualFocus : followTarget;

        /// <param name="currentFocus">Where the camera is looking now (a manual pan starts from here, so there is no jump).</param>
        /// <param name="baseYaw">The yaw (degrees) looking along the road; <see cref="YawOffset"/> is added.</param>
        public void Step(float deltaTime, CameraInput input, Vector3 currentFocus, float baseYaw)
        {
            if (input.Recenter) { Recenter(); }

            if (Mathf.Abs(input.Rotate) > 0.01f) YawOffset = Mathf.Repeat(YawOffset + input.Rotate * RotateDegreesPerSecond * deltaTime + 180f, 360f) - 180f;
            if (Mathf.Abs(input.Zoom) > 0.01f) Zoom = Mathf.Clamp(Zoom - Mathf.Sign(input.Zoom) * ZoomStep, MinZoom, MaxZoom);

            if (input.Pan.sqrMagnitude < 0.0001f) return;
            if (!ManualOverride) { ManualOverride = true; manualFocus = currentFocus; }

            var yaw = (baseYaw + YawOffset) * Mathf.Deg2Rad;
            var forward = new Vector3(Mathf.Sin(yaw), 0f, Mathf.Cos(yaw));
            var right = new Vector3(forward.z, 0f, -forward.x);
            var move = (right * input.Pan.x + forward * input.Pan.y) * (PanSpeed * Zoom * deltaTime);   // farther out pans faster, so the speed feels constant on screen
            var candidate = manualFocus + move;
            if (WorldLayout.EdgeDistance(candidate) <= MaxDistanceFromWorld) manualFocus = candidate;
        }

        /// <summary>Resume following and look along the road again.</summary>
        public void Recenter() { ManualOverride = false; YawOffset = 0f; }

        /// <summary>A fight started or ended: frame it, whatever the player had done with the camera.</summary>
        public void ClearForSituationChange() => Recenter();
    }
}
