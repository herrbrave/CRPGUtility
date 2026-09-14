using System;
using CRPG.Actors;
using CRPG.Core;
using UnityEngine;

namespace CRPG.Abilities
{
    [Serializable]
    public sealed class ApplyConditionEffect : AbilityEffect
    {
        [SerializeField] private ConditionDefinition condition;
        [SerializeField] private int duration = -1; // negative = indefinite

        /// <summary>None (the default) means RemainingDuration is set but never ticks -- matches this field's
        /// pre-Task-4.4 behavior exactly, so no existing authored ability starts expiring just because this
        /// task shipped. See CRPG.Combat.ConditionDurationService for how a non-None boundary actually fires.</summary>
        [SerializeField] private DurationBoundary boundary = DurationBoundary.None;

        public override void Apply(AbilityExecutionContext context)
        {
            var remaining = duration < 0 ? (int?)null : duration;

            foreach (var targetId in context.Targets)
            {
                if (!context.Characters.TryGetCharacter(targetId, out var target)) continue;

                target.ApplyCondition(new ConditionInstance(
                    condition.Id, context.Source, targetId, remaining, ownerId: context.OwnerId, boundary: boundary));
                context.OwnedTargets.Add(targetId);
                context.CombatLog?.Log(new ConditionLogEntry(context.Source, targetId, condition.Id, applied: true));
                context.EventBus?.Publish(new GameEventContext(GameEvent.ConditionApplied, context.Source, targetId));
            }
        }
    }
}
