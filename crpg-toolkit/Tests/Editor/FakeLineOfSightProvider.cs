using CRPG.Core;
using UnityEngine;

namespace CRPG.Tests.EditMode
{
    /// <summary>Minimal ILineOfSightProvider for tests -- no real Physics scene involved
    /// (PhysicsLineOfSightProvider itself gets its own narrow PlayMode test for the real Physics.Linecast path).</summary>
    internal sealed class FakeLineOfSightProvider : ILineOfSightProvider
    {
        public bool AlwaysBlocked;

        public bool HasLineOfSight(Vector3 from, Vector3 to) => !AlwaysBlocked;
    }
}
