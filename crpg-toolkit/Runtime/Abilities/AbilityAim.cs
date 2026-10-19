using UnityEngine;

namespace CRPG.Abilities
{
    /// <summary>
    /// Where an aimed ability was pointed: a point on the ground and a flat, normalised direction (from the caster toward the point for a click-aimed ability). <see cref="None"/> for abilities that take creatures. A readonly value: the game's targeting gate builds one,
    /// the service validates it, derives the creatures inside the shape from it and hands it to the effects.
    /// </summary>
    public readonly struct AbilityAim
    {
        public AbilityAim(Vector3 point, Vector3 direction)
        {
            Point = point;
            direction.y = 0f;
            Direction = direction.sqrMagnitude > 1e-6f ? direction.normalized : Vector3.forward;
            IsSet = true;
        }

        public Vector3 Point { get; }
        public Vector3 Direction { get; }
        public bool IsSet { get; }

        public static AbilityAim None => default;

        /// <summary>The usual aim: the point the player clicked, the direction from the caster to it.</summary>
        public static AbilityAim FromCaster(Vector3 casterPosition, Vector3 point) => new(point, point - casterPosition);
    }
}
