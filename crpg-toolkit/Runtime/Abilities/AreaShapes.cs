using UnityEngine;

namespace CRPG.Abilities
{
    /// <summary>
    /// The geometry of an aimed ability's area, pure and scene-free (the targeting gate draws it, the service uses it to derive the creatures inside). Units are metres, areas are measured on the floor plane with a vertical band, and a creature is tested at its FEET position.
    /// <list type="bullet">
    /// <item><b>Sphere</b>: radius <c>AreaSize</c>, a 3D distance from the centre raised to chest height (so a creature standing on the aim point is at its centre).</item>
    /// <item><b>Cylinder</b>: radius <c>AreaSize</c> on the floor, feet between the aim point and <c>AreaHeight</c> above it.</item>
    /// <item><b>Area</b>: a square (a cube with a height band) of side <c>AreaSize</c>, facing away from the caster; at the aim point, or (AtCaster) touching the caster in the aim direction.</item>
    /// <item><b>Cone</b>: apex at the caster, axis along the aim direction, length <c>AreaSize</c>, 5e proportions (the width at the far end equals the length).</item>
    /// <item><b>Line</b>: from the caster along the aim direction, length <c>AreaSize</c>, <see cref="LineWidth"/> wide.</item>
    /// </list>
    /// <c>Point</c> and <c>Direction</c> hit no creatures here (a point is a place; a direction is reserved).
    /// </summary>
    public static class AreaShapes
    {
        public const float ChestHeight = 1f, HeightBand = 2f, LineWidth = 1.5f;

        /// <summary>The flat direction an area faces: caster to aim point (the aim's own direction when they coincide).</summary>
        public static Vector3 Facing(Vector3 caster, AbilityAim aim)
        {
            var d = aim.Point - caster; d.y = 0f;
            return d.sqrMagnitude > 1e-6f ? d.normalized : aim.Direction;
        }

        /// <summary>The point the area starts from: the caster for a cone, a line and anything AtCaster, the aim point otherwise.</summary>
        public static Vector3 OriginOf(TargetingDefinition targeting, Vector3 caster, AbilityAim aim) =>
            targeting.Mode == TargetMode.Cone || targeting.Mode == TargetMode.Line || targeting.Anchor == AreaAnchor.AtCaster ? caster : aim.Point;

        /// <summary>How far from <see cref="OriginOf"/> any creature of the shape can be: the radius to ask a candidate source for.</summary>
        public static float Reach(TargetingDefinition targeting)
        {
            var size = Mathf.Max(0f, targeting.AreaSize);
            switch (targeting.Mode)
            {
                case TargetMode.Sphere:
                case TargetMode.Cylinder: return size + 1f;
                case TargetMode.Area: return (targeting.Anchor == AreaAnchor.AtCaster ? size * 1.5f : size * 0.75f) + 1f;
                case TargetMode.Cone:
                case TargetMode.Line: return size + 1f;
                default: return 0f;
            }
        }

        public static bool Contains(TargetingDefinition targeting, Vector3 caster, AbilityAim aim, Vector3 feet)
        {
            if (!targeting.IsShaped) return false;
            if (!aim.IsSet && !targeting.CasterCentred) return false;
            var size = Mathf.Max(0f, targeting.AreaSize);
            var origin = OriginOf(targeting, caster, aim);
            var facing = Facing(caster, aim);
            var offset = feet - origin;
            var flat = new Vector2(offset.x, offset.z);
            switch (targeting.Mode)
            {
                case TargetMode.Sphere:
                    return Vector3.Distance(feet + Vector3.up * ChestHeight, origin + Vector3.up * ChestHeight) <= size;
                case TargetMode.Cylinder:
                    return flat.magnitude <= size && offset.y >= -HeightBand * 0.25f && offset.y <= Mathf.Max(0.1f, targeting.AreaHeight);
                case TargetMode.Area:
                {
                    var centre = targeting.Anchor == AreaAnchor.AtCaster ? origin + facing * (size * 0.5f) : origin;
                    var local = Local(feet - centre, facing);
                    return Mathf.Abs(local.x) <= size * 0.5f && Mathf.Abs(local.y) <= size * 0.5f && Mathf.Abs(offset.y) <= HeightBand;
                }
                case TargetMode.Cone:
                {
                    var local = Local(offset, facing);   // x = lateral, y = along
                    return local.y >= 0f && local.y <= size && Mathf.Abs(local.x) <= local.y * 0.5f + 0.001f && Mathf.Abs(offset.y) <= HeightBand;
                }
                case TargetMode.Line:
                {
                    var local = Local(offset, facing);
                    return local.y >= 0f && local.y <= size && Mathf.Abs(local.x) <= LineWidth * 0.5f && Mathf.Abs(offset.y) <= HeightBand;
                }
                default: return false;
            }
        }

        /// <summary>x = to the right of <paramref name="facing"/>, y = along it.</summary>
        private static Vector2 Local(Vector3 offset, Vector3 facing)
        {
            var right = new Vector3(facing.z, 0f, -facing.x);
            return new Vector2(offset.x * right.x + offset.z * right.z, offset.x * facing.x + offset.z * facing.z);
        }
    }
}
