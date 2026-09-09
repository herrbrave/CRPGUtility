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

        public override void Apply(AbilityExecutionContext context)
        {
            var remaining = duration < 0 ? (int?)null : duration;

            foreach (var targetId in context.Targets)
            {
                if (!context.Characters.TryGetCharacter(targetId, out var target)) continue;

                target.ApplyCondition(new ConditionInstance(condition.Id, context.Source, targetId, remaining));
                context.CombatLog?.Log(new ConditionLogEntry(context.Source, targetId, condition.Id, applied: true));
                context.EventBus?.Publish(new GameEventContext(GameEvent.ConditionApplied, context.Source, targetId));
            }
        }
    }
}
