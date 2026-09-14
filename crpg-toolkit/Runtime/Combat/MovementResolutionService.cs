using System.Collections.Generic;
using CRPG.Abilities;
using CRPG.Actors;
using CRPG.Core;
using UnityEngine;

namespace CRPG.Combat
{
    /// <summary>
    /// Spends a combatant's Movement-like resource to close distance toward another entity, stopping short by
    /// <paramref name="stopDistance"/> (e.g. so a melee combatant ends its move at engagement range rather than
    /// walking into the target). Parallels <see cref="CRPG.Abilities.IAbilityResolutionService"/>'s shape --
    /// "spend a resource, do the thing, log it" -- but movement isn't an ability (no targeting/effects to
    /// resolve), so it gets its own small resolver rather than being modeled as a fake AbilityDefinition.
    /// Optionally triggers opportunity attacks (see MoveToward's own remarks) when reaction support is wired up.
    /// </summary>
    public sealed class MovementResolutionService
    {
        private readonly ICharacterRegistry characters;
        private readonly IPositionProvider positions;
        private readonly IMovementController movementController;
        private readonly ICombatLog combatLog;
        private readonly ReactionService reactionService;
        private readonly IRulesDatabase rulesDatabase;
        private readonly IMovementSpeedRule speedRule;
        private readonly float threatRange;

        public MovementResolutionService(
            ICharacterRegistry characters,
            IPositionProvider positions,
            IMovementController movementController,
            ICombatLog combatLog = null,
            ReactionService reactionService = null,
            IRulesDatabase rulesDatabase = null,
            IMovementSpeedRule speedRule = null,
            float threatRange = 1.5f)
        {
            this.characters = characters;
            this.positions = positions;
            this.movementController = movementController;
            this.combatLog = combatLog;
            this.reactionService = reactionService;
            this.rulesDatabase = rulesDatabase;
            this.speedRule = speedRule;
            this.threatRange = threatRange;
        }

        public bool CanMove(RuntimeEntityId entity, DefinitionId movementResourceId) =>
            characters.TryGetCharacter(entity, out var character)
            && character.TryGetResource(movementResourceId, out var movement)
            && movement.Current > 0
            && EffectiveCap(entity, movement) > 0f;

        /// <summary>Task 4.3: public wrapper around <see cref="EffectiveCap"/> so
        /// CRPG.Gameplay.Actions.MoveActionExecution can ask "how much more can this entity afford right now"
        /// (for clipping a planned segment to what's actually affordable) without duplicating the speed-rule
        /// logic. 0 if the entity has no such resource at all.</summary>
        public float GetEffectiveRemainingBudget(RuntimeEntityId entity, DefinitionId movementResourceId) =>
            characters.TryGetCharacter(entity, out var character) && character.TryGetResource(movementResourceId, out var movement)
                ? EffectiveCap(entity, movement)
                : 0f;

        /// <summary>Task 4.3: public wrapper around spending the movement resource directly (bypassing
        /// <see cref="MoveToward"/>'s own single whole-move spend), so a multi-segment real-navigation move
        /// (CRPG.Gameplay.Actions.MoveActionExecution) can settle its own cumulative-distance-based spend
        /// through the same resource vocabulary as everything else in this class, rather than reaching into
        /// CharacterState/ResourceState directly. A no-op if the entity/resource can't be resolved or amount
        /// is not positive.</summary>
        public void SpendMovement(RuntimeEntityId entity, DefinitionId movementResourceId, int amount)
        {
            if (amount <= 0) return;
            if (characters.TryGetCharacter(entity, out var character) && character.TryGetResource(movementResourceId, out var movement))
                movement.Spend(amount);
        }

        /// <summary>Caps the Movement resource's remaining Current by any condition currently reducing this
        /// entity's speed (Grappled, Exhaustion, ...) -- seeded from the resource's own Max (the character's
        /// true speed for the turn), not Current, so e.g. a halving condition halves the real speed rather than
        /// whatever happens to be left after partial movement already spent this turn. Doesn't mutate the
        /// resource itself, so the reduction disappears on its own once the condition does. No-op (returns
        /// Current unchanged) if no IMovementSpeedRule was supplied.</summary>
        private float EffectiveCap(RuntimeEntityId entity, ResourceState movement) =>
            speedRule == null ? movement.Current : Mathf.Min(movement.Current, speedRule.ComputeEffectiveSpeed(entity, movement.Max));

        /// <summary>Moves <paramref name="entity"/> toward <paramref name="towardTarget"/>, spending at most
        /// the entity's remaining Movement resource and never closing past <paramref name="stopDistance"/>
        /// away from the target. A no-op (and no log entry) if the entity has no Movement resource left or is
        /// already within stopDistance.
        /// <para><paramref name="potentialReactors"/> (optional): candidates to check for opportunity attacks.
        /// Anyone in this list who was within threatRange of the mover before the move and isn't after gets a
        /// ReactionOpportunity resolved via the already-existing ReactionService, using whatever of their
        /// granted abilities are tagged Reaction. Resolved synchronously right after the move completes, not a
        /// true interrupt of a partially-finished move -- see ReactionService's own remarks on why that's the
        /// deliberate scope here, not a gap. No-ops (silently skips the opportunity-attack check) if
        /// potentialReactors, ReactionService, or IRulesDatabase weren't supplied.</para></summary>
        public void MoveToward(
            RuntimeEntityId entity, RuntimeEntityId towardTarget, DefinitionId movementResourceId, float stopDistance,
            IReadOnlyList<RuntimeEntityId> potentialReactors = null)
        {
            if (!characters.TryGetCharacter(entity, out var character)) return;
            if (!character.TryGetResource(movementResourceId, out var movement) || movement.Current <= 0) return;

            var selfPosition = positions.GetPosition(entity);
            var targetPosition = positions.GetPosition(towardTarget);
            var fullDistance = Vector3.Distance(selfPosition, targetPosition);
            var desiredDistance = Mathf.Max(0f, fullDistance - stopDistance);
            var moveDistance = Mathf.Min(desiredDistance, EffectiveCap(entity, movement));
            if (moveDistance <= 0f) return;

            var threatsBefore = GetThreatsInRange(entity, potentialReactors);

            var actualDistance = movementController.TryMoveTowards(entity, targetPosition, moveDistance);
            if (actualDistance <= 0f) return;

            movement.Spend(Mathf.CeilToInt(actualDistance));
            combatLog?.Log(new MovementLogEntry(entity, towardTarget, actualDistance));

            if (threatsBefore != null) TriggerOpportunityAttacks(entity, threatsBefore);
        }

        private HashSet<RuntimeEntityId> GetThreatsInRange(RuntimeEntityId entity, IReadOnlyList<RuntimeEntityId> potentialReactors)
        {
            if (potentialReactors == null || reactionService == null || rulesDatabase == null) return null;

            var inRange = new HashSet<RuntimeEntityId>();
            var selfPosition = positions.GetPosition(entity);
            foreach (var reactorId in potentialReactors)
            {
                if (Vector3.Distance(selfPosition, positions.GetPosition(reactorId)) <= threatRange) inRange.Add(reactorId);
            }

            return inRange;
        }

        private void TriggerOpportunityAttacks(RuntimeEntityId mover, HashSet<RuntimeEntityId> threatsBefore)
        {
            var selfPosition = positions.GetPosition(mover);
            foreach (var reactorId in threatsBefore)
            {
                if (Vector3.Distance(selfPosition, positions.GetPosition(reactorId)) <= threatRange) continue; // still in range -- didn't leave

                if (!characters.TryGetCharacter(reactorId, out var reactor)) continue;
                var candidates = GatherReactionAbilities(reactor);
                if (candidates.Count == 0) continue;

                var opportunity = new ReactionOpportunity(GameEvent.LeftThreatenedArea, reactorId, mover);
                reactionService.ResolveOpportunity(opportunity, candidates, new[] { mover });
            }
        }

        /// <summary>Task 4.3: the geometric half of "stop before interrupt points" -- for a real-navigation
        /// multi-segment move (CRPG.Gameplay.Actions.MoveActionExecution), finds every point along ONE planned
        /// corner-to-corner segment where the mover's distance to a candidate reactor crosses threatRange,
        /// via closed-form segment/circle intersection (no per-tick sampling). A segment that starts and ends
        /// already inside (or already outside) a given reactor's range produces no crossing for that reactor --
        /// correct, since SRD5's own rule only cares about actually leaving a threatened area, and a reactor
        /// the mover was already within range of before this whole move began was never "left" by this
        /// segment. No-op (empty list) if potentialReactors or Positions weren't supplied.</summary>
        public IReadOnlyList<ThreatBoundaryCrossing> FindThreatBoundaryCrossings(
            Vector3 segmentStart, Vector3 segmentEnd, IReadOnlyList<RuntimeEntityId> potentialReactors)
        {
            var results = new List<ThreatBoundaryCrossing>();
            if (potentialReactors == null || positions == null) return results;

            var segment = segmentEnd - segmentStart;
            var segmentLengthSquared = segment.sqrMagnitude;
            if (segmentLengthSquared <= 0f) return results;

            var segmentLength = Mathf.Sqrt(segmentLengthSquared);

            foreach (var reactorId in potentialReactors)
            {
                var center = positions.GetPosition(reactorId);
                var f = segmentStart - center;

                var a = segmentLengthSquared;
                var b = 2f * Vector3.Dot(f, segment);
                var c = Vector3.Dot(f, f) - threatRange * threatRange;
                var discriminant = b * b - 4f * a * c;
                if (discriminant < 0f) continue; // this segment never touches the reactor's threat circle

                var sqrtDiscriminant = Mathf.Sqrt(discriminant);
                // The parabola a*t^2+b*t+c opens upward (a > 0): distance-squared-minus-r-squared is negative
                // (inside the circle) between the two roots -- t1 is always the entering root, t2 the leaving
                // one, regardless of where segmentStart/segmentEnd sit relative to the circle.
                var t1 = (-b - sqrtDiscriminant) / (2f * a);
                var t2 = (-b + sqrtDiscriminant) / (2f * a);

                if (t1 >= 0f && t1 <= 1f) results.Add(new ThreatBoundaryCrossing(t1 * segmentLength, reactorId, entering: true));
                if (t2 >= 0f && t2 <= 1f) results.Add(new ThreatBoundaryCrossing(t2 * segmentLength, reactorId, entering: false));
            }

            results.Sort((x, y) => x.DistanceAlongSegment.CompareTo(y.DistanceAlongSegment));
            return results;
        }

        /// <summary>Task 4.3: the single-crossing version of <see cref="TriggerOpportunityAttacks"/>'s existing
        /// whole-move loop, driven by one <see cref="ThreatBoundaryCrossing"/> from
        /// <see cref="FindThreatBoundaryCrossings"/> instead of a before/after HashSet diff. Only an
        /// <paramref name="entering"/> == false crossing (leaving the threatened area) can ever trigger a
        /// reaction -- SRD5's own rule, applied here rather than left for the Gameplay-layer caller to know
        /// about. No-op (returns false) if ReactionService/IRulesDatabase weren't supplied, the reactor can't
        /// be resolved, or it has no eligible Reaction-tagged ability.</summary>
        public bool ResolveReactionForCrossing(RuntimeEntityId mover, RuntimeEntityId reactorId, bool entering)
        {
            if (entering) return false;
            if (reactionService == null || rulesDatabase == null) return false;
            if (!characters.TryGetCharacter(reactorId, out var reactor)) return false;

            var candidates = GatherReactionAbilities(reactor);
            if (candidates.Count == 0) return false;

            var opportunity = new ReactionOpportunity(GameEvent.LeftThreatenedArea, reactorId, mover);
            return reactionService.ResolveOpportunity(opportunity, candidates, new[] { mover });
        }

        private List<AbilityDefinition> GatherReactionAbilities(CharacterState character)
        {
            var abilities = new List<AbilityDefinition>();
            foreach (var featureId in character.Features)
            {
                if (!rulesDatabase.TryResolve<FeatureDefinition>(featureId, out var feature)) continue;

                foreach (var abilityId in feature.GrantedAbilityIds)
                {
                    if (rulesDatabase.TryResolve<AbilityDefinition>(abilityId, out var ability) && (ability.Tags & AbilityTag.Reaction) != 0)
                        abilities.Add(ability);
                }
            }

            return abilities;
        }
    }
}
