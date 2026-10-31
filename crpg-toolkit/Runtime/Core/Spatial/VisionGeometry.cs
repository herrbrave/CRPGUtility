using UnityEngine;

namespace CRPG.Core
{
    /// <summary>The pure geometry of a <see cref="VisionProfile"/>: is a point inside the observer's cone or awareness circle. Obstruction, light and conditions are the perception query's business.</summary>
    public static class VisionGeometry
    {
        public static bool InSightArea(Vector3 eye, float yawDegrees, VisionProfile profile, Vector3 point)
        {
            var offset = point - eye;
            var distance = offset.magnitude;
            if (profile.AwarenessRadius > 0f && distance <= profile.AwarenessRadius) return true;
            if (profile.ConeRange > 0f && distance > profile.ConeRange) return false;
            if (profile.ConeHalfAngle >= 180f) return true;

            var flat = new Vector3(offset.x, 0f, offset.z);
            if (flat.sqrMagnitude < 1e-6f) return true;
            var yaw = yawDegrees * Mathf.Deg2Rad;
            var forward = new Vector3(Mathf.Sin(yaw), 0f, Mathf.Cos(yaw));
            return Vector3.Angle(forward, flat) <= profile.ConeHalfAngle;
        }
    }
}
