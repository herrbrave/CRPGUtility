using System.Collections.Generic;
using CRPG.Core;
using CRPG.Gameplay;
using CRPG.Navigation;
using CRPG.Party;
using UnityEngine;

namespace CRPG.Exploration
{
    /// <summary>Resolves a PartyMoveOrder's formation points onto the NavMesh AND validates each member can
    /// actually path there from where it currently is -- a formation slot that's on-mesh but unreachable
    /// (narrow passage, a split-off member on the far side of an obstacle, a genuinely disconnected NavMesh
    /// island) is a real, reportable failure (<see cref="FormationResolutionOutcome.Unreachable"/>), never
    /// silently swapped for the member's own current position -- that would read back as a no-op "success"
    /// and hide the failure from anything inspecting the outcome.</summary>
    public sealed class PartyMovementService
    {
        private const int MaxFormationRetries = 4;

        private readonly INavMeshService navMesh;
        private readonly ISlotAvoidingNavMeshService slotAvoiding; // null for a backend where characters can overlap (NavMesh)
        private readonly ISelectableSource selectableSource;
        private readonly float sampleRadius;

        public PartyMovementService(INavMeshService navMesh, ISelectableSource selectableSource, float sampleRadius = 5f)
        {
            this.navMesh = navMesh;
            slotAvoiding = navMesh as ISlotAvoidingNavMeshService;
            this.selectableSource = selectableSource;
            this.sampleRadius = sampleRadius;
        }

        public IReadOnlyDictionary<RuntimeEntityId, FormationDestinationResolution> ResolveDestinations(PartyMoveOrder order)
        {
            // GetSelectables() enumerated exactly once regardless of party size -- ISelectableSource makes no
            // promise about being cheap to call repeatedly, and this reads it once per order, not once per
            // assignment.
            var currentPositions = new Dictionary<RuntimeEntityId, Vector3>();
            foreach (var entity in selectableSource.GetSelectables()) currentPositions[entity.Id] = entity.WorldPosition;

            // Spots already taken, for a backend where characters can't share one (see
            // ISlotAvoidingNavMeshService): the current position of every character outside this order (unselected
            // party members, NPCs -- moving or not, which is the conservative choice), each destination handed out
            // earlier in this same order, and the current position of any member found Unreachable (it is
            // definitely staying put). Members INSIDE the order are deliberately not pre-seeded: a party stepping
            // two cells forward would otherwise displace everyone off the cells their teammates are vacating. A
            // NavMesh backend never reads any of this, so its behaviour is unchanged.
            var claimed = new List<Vector3>();
            foreach (var entry in currentPositions)
            {
                if (!order.Assignments.ContainsKey(entry.Key)) claimed.Add(entry.Value);
            }

            var resolved = new Dictionary<RuntimeEntityId, FormationDestinationResolution>(order.Assignments.Count);

            // In member order (the order the formation solver assigned slots in), not Dictionary order: claim
            // order decides who keeps their exact slot, so it must be deterministic.
            foreach (var character in order.Members)
            {
                var requestedDestination = order.Assignments[character];

                // Missing from the selectable source (destroyed, not yet spawned, ...): ISelectableSource's
                // contract is "currently live," not "every party member always." With no known starting
                // position there is nothing to validate reachability against, so this is reported as
                // Unreachable rather than guessed at.
                if (!currentPositions.TryGetValue(character, out var fromPosition))
                {
                    resolved[character] = new FormationDestinationResolution(requestedDestination, null, FormationResolutionOutcome.Unreachable);
                    continue;
                }

                var resolution = ResolveOneMember(fromPosition, requestedDestination, order.Destination, claimed);
                resolved[character] = resolution;
                if (resolution.Outcome == FormationResolutionOutcome.Unreachable) claimed.Add(fromPosition);
            }

            return resolved;
        }

        private FormationDestinationResolution ResolveOneMember(Vector3 fromPosition, Vector3 requestedDestination, Vector3 sharedDestination, List<Vector3> claimed)
        {
            for (var attempt = 0; attempt <= MaxFormationRetries; attempt++)
            {
                // Attempt 0 is the original formation slot. Each later attempt compresses the CANDIDATE
                // toward the shared order destination (never toward the member's own position) -- a real
                // detour around an obstacle must stay valid no matter how long the resulting path is, so
                // there is deliberately no path-length-vs-straight-line ratio check here, only "does a real
                // path exist at all" via TryCalculatePathLength. Only a slot that's still impractically far
                // from where the group is actually going gets pulled toward the destination as a fallback.
                var t = attempt / (float)MaxFormationRetries;
                var candidate = Vector3.Lerp(requestedDestination, sharedDestination, t);

                if (!navMesh.TrySamplePosition(candidate, out var sampled, sampleRadius)) continue;

                // Where characters can't share a spot, a slot that snaps onto an already-taken one is displaced to
                // the nearest free one -- and that is a Fallback, not the slot as requested. (The plain sample above
                // is kept solely to tell displaced from undisturbed for that Exact/Fallback distinction.) If
                // nothing nearby is free, this attempt fails and the next (more compressed toward the destination)
                // is tried.
                var displaced = false;
                if (slotAvoiding != null && claimed.Count > 0)
                {
                    if (!slotAvoiding.TrySamplePositionAvoiding(candidate, out var free, sampleRadius, claimed)) continue;
                    displaced = Vector3.Distance(free, sampled) > 1e-3f;
                    sampled = free;
                }

                if (!navMesh.TryCalculatePathLength(fromPosition, sampled, out _)) continue;

                claimed.Add(sampled);
                var outcome = attempt == 0 && !displaced ? FormationResolutionOutcome.Exact : FormationResolutionOutcome.Fallback;
                return new FormationDestinationResolution(requestedDestination, sampled, outcome);
            }

            return new FormationDestinationResolution(requestedDestination, null, FormationResolutionOutcome.Unreachable);
        }
    }
}
