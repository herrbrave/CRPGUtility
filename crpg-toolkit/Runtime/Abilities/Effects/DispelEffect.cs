using System;
using CRPG.Actors;
using CRPG.Core;
using UnityEngine;

namespace CRPG.Abilities
{
    /// <summary>Task 4.4: a deliberately simplified subset of "dispel magic," NOT full SRD5 compliance -- no
    /// spell-level check, no contested ability check, no player-chosen effect. Removes the MOST RECENTLY applied
    /// ability-owned condition on the target (scanning from the end of its condition list) and, via
    /// CharacterState.RemoveConditionsWithOwner, every OTHER instance sharing that exact same owner -- i.e. every
    /// condition that one specific cast applied to this target, never a broad "purge everything owned" that
    /// would also strip an unrelated caster's condition of a different cast. Conditions with no owner
    /// (environmental/manually-applied, EffectOwnerId.None) are never touched. Deliberately does NOT reach back
    /// to clear the original caster's ConcentratingOnAbilityId even if the dispelled condition came from an
    /// active concentration -- a multi-target concentration spell dispelled off ONE target shouldn't necessarily
    /// end the caster's concentration on the others it still affects, and finding "who is concentrating with
    /// this owner id" would need ICharacterRegistry enumeration the architecture deliberately doesn't provide.</summary>
    [Serializable]
    public sealed class DispelEffect : AbilityEffect
    {
        public override void Apply(AbilityExecutionContext context)
        {
            foreach (var targetId in context.Targets)
            {
                if (!context.Characters.TryGetCharacter(targetId, out var target)) continue;

                var ownerId = EffectOwnerId.None;
                var conditions = target.Conditions;
                for (var i = conditions.Count - 1; i >= 0; i--)
                {
                    if (!conditions[i].OwnerId.IsValid) continue;
                    ownerId = conditions[i].OwnerId;
                    break;
                }

                if (!ownerId.IsValid) continue;

                foreach (var removed in target.RemoveConditionsWithOwner(ownerId))
                {
                    context.CombatLog?.Log(new ConditionLogEntry(context.Source, targetId, removed.ConditionId, applied: false));
                    context.EventBus?.Publish(new GameEventContext(GameEvent.ConditionRemoved, context.Source, targetId));
                }
            }
        }
    }
}
