using System.Collections.Generic;
using CRPG.Core;
using UnityEngine;

// Dogfood-only: resolves a combatant's position from the Transform of its 2D scene visual, so the AI's
// Distance consideration and ability-range checks see the same layout the player is looking at. Also the
// IMovementController implementation for this scene -- moving the same Transform is what makes a MoveToward
// decision actually visible on screen, no separate motor/agent needed for a flat 2D test.
public sealed class SceneEntityPositionProvider : IPositionProvider, IMovementController
{
    private readonly Dictionary<RuntimeEntityId, Transform> transformsById = new();

    public void Register(RuntimeEntityId entity, Transform transform) => transformsById[entity] = transform;

    public Vector3 GetPosition(RuntimeEntityId entity) =>
        transformsById.TryGetValue(entity, out var transform) ? transform.position : Vector3.zero;

    public float TryMoveTowards(RuntimeEntityId entity, Vector3 destination, float maxDistance)
    {
        if (!transformsById.TryGetValue(entity, out var transform) || maxDistance <= 0f) return 0f;

        var before = transform.position;
        transform.position = Vector3.MoveTowards(before, destination, maxDistance);
        return Vector3.Distance(before, transform.position);
    }
}
