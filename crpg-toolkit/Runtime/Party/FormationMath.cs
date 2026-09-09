using UnityEngine;

namespace CRPG.Party
{
    /// <summary>Shared geometry for the formation solvers: everything happens on the XZ plane.</summary>
    internal static class FormationMath
    {
        public static Vector3 FlattenFacing(Vector3 facing)
        {
            var flat = new Vector3(facing.x, 0f, facing.z);
            return flat.sqrMagnitude < 0.0001f ? Vector3.forward : flat.normalized;
        }

        public static Vector3 RightOf(Vector3 flatFacing) => Vector3.Cross(Vector3.up, flatFacing);
    }
}
