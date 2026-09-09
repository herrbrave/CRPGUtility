using System;
using System.Linq;
using CRPG.Actors;
using CRPG.Core;
using UnityEngine;

namespace CRPG.Abilities
{
    [Serializable]
    public sealed class RemoveConditionEffect : AbilityEffect
    {
        [SerializeField] private ConditionDefinition condition;

        public override void Apply(AbilityExecutionContext context)
        {
            foreach (var targetId in context.Targets)
            {
                if (!context.Characters.TryGetCharacter(targetId, out var target)) continue;

                var existing = target.Conditions.FirstOrDefault(c => c.ConditionId == condition.Id);
                if (existing == null) continue;

                target.RemoveCondition(existing);
                context.CombatLog?.Log(new ConditionLogEntry(context.Source, targetId, condition.Id, applied: false));
                context.EventBus?.Publish(new GameEventContext(GameEvent.ConditionRemoved, context.Source, targetId));
            }
        }
    }
}
