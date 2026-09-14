using UnityEngine;

namespace CRPG.Core
{
    /// <summary>Reference ILineOfSightProvider backed by UnityEngine.Physics.Linecast -- mirrors
    /// NavMeshService's own precedent (a concrete, Unity-API-backed default shipped in the package, since
    /// Physics.Linecast is the standard Unity way to do this, exactly like UnityEngine.AI.NavMesh is for
    /// navigation). obstructionMask is a required constructor parameter, not a default -- "which layers block
    /// sight" is a composition-root decision this class must never guess at (no silent
    /// Physics.DefaultRaycastLayers fallback).</summary>
    public sealed class PhysicsLineOfSightProvider : ILineOfSightProvider
    {
        private readonly LayerMask obstructionMask;

        public PhysicsLineOfSightProvider(LayerMask obstructionMask)
        {
            this.obstructionMask = obstructionMask;
        }

        public bool HasLineOfSight(Vector3 from, Vector3 to) => !Physics.Linecast(from, to, obstructionMask);
    }
}
