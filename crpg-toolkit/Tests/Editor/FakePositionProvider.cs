using System.Collections.Generic;
using CRPG.AI;
using CRPG.Core;
using UnityEngine;

namespace CRPG.Tests.EditMode
{
    /// <summary>Minimal IPositionProvider for tests -- no scene/Transform involved.</summary>
    internal sealed class FakePositionProvider : IPositionProvider
    {
        private readonly Dictionary<RuntimeEntityId, Vector3> positions = new();

        public void Set(RuntimeEntityId entity, Vector3 position) => positions[entity] = position;

        public Vector3 GetPosition(RuntimeEntityId entity) => positions.TryGetValue(entity, out var position) ? position : Vector3.zero;
    }
}
