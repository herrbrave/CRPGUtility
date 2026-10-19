using System.Collections.Generic;
using CRPG.Core;
using UnityEngine;

namespace CRPG.Abilities
{
    /// <summary>Who could be inside a shape: the creatures near a centre. The service cannot enumerate the world itself (the character registry is lookup-only), so the game supplies this (its combatants plus any live agents). With no source an aimed SHAPE is refused in the preflight.</summary>
    public interface ITargetCandidateSource
    {
        IEnumerable<RuntimeEntityId> CandidatesNear(Vector3 centre, float radius);
    }

    /// <summary>The game's say on a clicked ground point for an aimed ability: snaps it (standing room, the right deck), refuses it with a reason (a wall in the way, someone standing there). Both the targeting gate (for the preview) and the service's Validate (the authority) ask it, so they always agree. Optional.</summary>
    public interface IAimSurface
    {
        bool TryAccept(RuntimeEntityId source, AbilityDefinition ability, Vector3 clicked, out Vector3 point, out string reason);
    }

    public enum RelocationStyle { Teleport, Leap }

    /// <summary>Moves a creature somewhere (Misty Step, Leap). Synchronous: the creature is AT the destination when this returns, so the rules, the AI and the position providers are right at once; any flourish (a fade, an arc) is the implementation's presentation, run afterwards.</summary>
    public interface IRelocationService
    {
        bool TryRelocate(RuntimeEntityId entity, Vector3 destination, RelocationStyle style);
    }
}
