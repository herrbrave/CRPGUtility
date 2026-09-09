using System;
using CRPG.Core;
using UnityEngine;

namespace CRPG.Abilities
{
    /// <summary>Registers an InfluenceMap zone at each target's position when this ability resolves -- Fireball
    /// leaves fire, Web leaves difficult terrain. A no-op if the resolving context has no Positions/InfluenceMap
    /// wired up (most abilities won't need them). The zone has no built-in duration: nothing in the engine
    /// currently ticks down a duration for anything (ConditionInstance.RemainingDuration has the same gap) --
    /// it persists until something explicitly calls InfluenceMap.RemoveSource.</summary>
    [Serializable]
    public sealed class CreateInfluenceSourceEffect : AbilityEffect
    {
        [SerializeField] private string layer;
        [SerializeField] private float radius = 5f;
        [SerializeField] private float magnitude = 1f;
        [SerializeField] private AnimationCurve falloff = AnimationCurve.Linear(0f, 1f, 1f, 0f);

        public override void Apply(AbilityExecutionContext context)
        {
            if (context.Positions == null || context.InfluenceMap == null) return;

            foreach (var targetId in context.Targets)
            {
                var position = context.Positions.GetPosition(targetId);
                context.InfluenceMap.AddSource(new InfluenceSource(RuntimeEntityId.NewId(), layer, position, radius, magnitude, falloff));
            }
        }
    }
}
