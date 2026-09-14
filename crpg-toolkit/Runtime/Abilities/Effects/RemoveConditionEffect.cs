using System;
using CRPG.Actors;
using CRPG.Core;
using UnityEngine;

namespace CRPG.Abilities
{
    /// <summary>A "cure" -- ends the named condition TYPE regardless of which source(s) applied it, removing
    /// every stacked instance if more than one source has it applied (e.g. two different poison sources both
    /// currently Poisoning the target). This is deliberately different from ownership-scoped removal
    /// (ConcentrationEffectCleanup.Break, DispelEffect), which removes only ONE specific cast's instance(s) and
    /// leaves an unrelated source's condition of the same type untouched -- see CharacterState.RemoveConditions
    /// vs. RemoveConditionsWithOwner.</summary>
    [Serializable]
    public sealed class RemoveConditionEffect : AbilityEffect
    {
        [SerializeField] private ConditionDefinition condition;

        public override void Apply(AbilityExecutionContext context)
        {
            foreach (var targetId in context.Targets)
            {
                if (!context.Characters.TryGetCharacter(targetId, out var target)) continue;

                foreach (var removed in target.RemoveConditions(c => c.ConditionId == condition.Id))
                {
                    context.CombatLog?.Log(new ConditionLogEntry(context.Source, targetId, removed.ConditionId, applied: false));
                    context.EventBus?.Publish(new GameEventContext(GameEvent.ConditionRemoved, context.Source, targetId));
                }
            }
        }
    }
}
