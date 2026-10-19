using System;
using System.Collections.Generic;
using System.Linq;
using CRPG.Actors;
using CRPG.Core;
using UnityEngine;

namespace CRPG.Abilities
{
    /// <summary>
    /// The aimed half of the service (docs/Plans/SpiritOfVengeance-CellFightAndLeap.md): an ability pointed at the ground (a Point, a shaped area) is validated from where it is aimed, and the creatures it hits are DERIVED from the shape, not picked. The creature-id
    /// <see cref="Validate(RuntimeEntityId, AbilityDefinition, IReadOnlyList{RuntimeEntityId}, bool, ResourceDefinition)"/> rejects an aimed ability with <see cref="AbilityRejectionReason.AimRequired"/>.
    /// </summary>
    public sealed partial class AbilityResolutionService
    {
        private const float RangeTolerance = 0.01f;

        public AbilityValidationResult Validate(
            RuntimeEntityId source, AbilityDefinition ability, AbilityAim aim,
            bool asRitual = false, ResourceDefinition upcastWithSlot = null) =>
            ValidateAim(source, ability, aim, asRitual, upcastWithSlot, out _, out _);

        /// <summary>The creatures an aimed ability would hit from this aim, WITHOUT validating it (range, line of sight, the aim surface): a preview wants the shape's contents even for an aim that is not allowed yet. Empty for a Point, with no candidate source, or for a
        /// creature-targeted ability.</summary>
        public IReadOnlyList<RuntimeEntityId> ResolveTargets(RuntimeEntityId source, AbilityDefinition ability, AbilityAim aim) =>
            DeriveTargets(source, ability, NormaliseAim(source, ability, aim));

        private AbilityAim NormaliseAim(RuntimeEntityId source, AbilityDefinition ability, AbilityAim aim)
        {
            if (!ability.Targeting.CasterCentred || positions == null) return aim;
            return new AbilityAim(positions.GetPosition(source), Vector3.forward);   // an aura points nowhere: it is on the caster
        }

        private AbilityValidationResult ValidateAim(
            RuntimeEntityId source, AbilityDefinition ability, AbilityAim aim, bool asRitual, ResourceDefinition upcastWithSlot,
            out AbilityAim accepted, out IReadOnlyList<RuntimeEntityId> derived)
        {
            accepted = aim; derived = Array.Empty<RuntimeEntityId>();
            var castingModeRejection = CheckCastingMode(source, ability, asRitual, upcastWithSlot);
            if (castingModeRejection != null) return castingModeRejection;

            var t = ability.Targeting;
            if (!t.RequiresAim && !t.CasterCentred)
                return AbilityValidationResult.Reject(AbilityRejectionReason.InvalidTarget, $"'{ability.DisplayName}' takes creatures, not a point on the ground.");
            if (positions == null) return AbilityValidationResult.Reject(AbilityRejectionReason.InvalidTarget, "Aimed abilities need a position provider.");

            var casterPosition = positions.GetPosition(source);
            aim = NormaliseAim(source, ability, aim);
            if (!aim.IsSet) return AbilityValidationResult.Reject(AbilityRejectionReason.AimRequired, $"'{ability.DisplayName}' needs a point to aim at.");

            if (t.UsesRange)
            {
                if (Vector3.Distance(casterPosition, aim.Point) > t.Range + RangeTolerance)
                    return AbilityValidationResult.Reject(AbilityRejectionReason.OutOfRange, $"That is beyond '{ability.DisplayName}''s range ({t.Range}).");
                if (t.RequireLineOfSight && lineOfSightProvider != null && !lineOfSightProvider.HasLineOfSight(casterPosition, aim.Point))
                    return AbilityValidationResult.Reject(AbilityRejectionReason.NoLineOfSight, "No line of sight to that point.");
            }

            if (AimSurface != null && !t.CasterCentred && (t.Mode == TargetMode.Point || t.UsesRange))
            {
                if (!AimSurface.TryAccept(source, ability, aim.Point, out var snapped, out var reason))
                    return AbilityValidationResult.Reject(AbilityRejectionReason.InvalidAimPoint, string.IsNullOrEmpty(reason) ? "Cannot aim there." : reason);
                aim = new AbilityAim(snapped, snapped - casterPosition);
            }

            if (t.IsShaped)
            {
                if (CandidateSource == null)
                    return AbilityValidationResult.Reject(AbilityRejectionReason.MissingRequiredEffectService, $"'{ability.DisplayName}' is an area: it needs a target candidate source to be wired up.");
                derived = DeriveTargets(source, ability, aim);
            }

            accepted = aim;
            var readinessRejection = CheckReadiness(source, ability, asRitual, upcastWithSlot);
            if (readinessRejection != null) return readinessRejection;
            return AbilityValidationResult.Ok();
        }

        private IReadOnlyList<RuntimeEntityId> DeriveTargets(RuntimeEntityId source, AbilityDefinition ability, AbilityAim aim)
        {
            var t = ability.Targeting;
            if (!t.IsShaped || positions == null || CandidateSource == null || !aim.IsSet) return Array.Empty<RuntimeEntityId>();
            var casterPosition = positions.GetPosition(source);
            var origin = AreaShapes.OriginOf(t, casterPosition, aim);
            var fromCaster = origin == casterPosition;
            var hits = new List<(RuntimeEntityId id, float distance)>();
            var seen = new HashSet<RuntimeEntityId>();
            foreach (var id in CandidateSource.CandidatesNear(origin, AreaShapes.Reach(t)))
            {
                if (!seen.Add(id) || !characters.TryGetCharacter(id, out _)) continue;
                if (fromCaster && id == source) continue;   // a cone, a line or an aura never hits its own caster
                var position = positions.GetPosition(id);
                if (!AreaShapes.Contains(t, casterPosition, aim, position)) continue;
                if (!RelationAllows(source, id, t.Relation)) continue;
                if (t.RequireLineOfEffect && lineOfSightProvider != null && !lineOfSightProvider.HasLineOfSight(origin + Vector3.up * AreaShapes.ChestHeight, position + Vector3.up * AreaShapes.ChestHeight)) continue;
                hits.Add((id, Vector3.Distance(origin, position)));
            }

            IEnumerable<(RuntimeEntityId id, float distance)> ordered = hits.OrderBy(h => h.distance);
            if (t.AreaTargetCap > 0) ordered = ordered.Take(t.AreaTargetCap);
            return ordered.Select(h => h.id).ToList();
        }

        /// <summary>Enemy and Ally filter by faction (a creature whose faction is unknown is excluded from both); every other relation includes everyone, friendly fire too (5e: a Shatter hits whoever stands in it).</summary>
        private bool RelationAllows(RuntimeEntityId source, RuntimeEntityId target, TargetRelation relation)
        {
            if (relation == TargetRelation.Self) return target == source;
            if (relation != TargetRelation.Ally && relation != TargetRelation.Enemy) return true;
            if (factionProvider == null) return true;
            if (!factionProvider.TryGetFaction(source, out var sourceFaction) || !factionProvider.TryGetFaction(target, out var targetFaction)) return false;
            return relation == TargetRelation.Ally ? sourceFaction == targetFaction : sourceFaction != targetFaction;
        }
    }
}
