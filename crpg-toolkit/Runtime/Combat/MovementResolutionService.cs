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
