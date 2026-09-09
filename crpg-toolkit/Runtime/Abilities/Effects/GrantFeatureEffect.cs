using System;
using CRPG.Actors;
using UnityEngine;

namespace CRPG.Abilities
{
    [Serializable]
    public sealed class GrantFeatureEffect : AbilityEffect
    {
        [SerializeField] private FeatureDefinition feature;
        [SerializeField] private bool applyToSelf;

        public override void Apply(AbilityExecutionContext context)
        {
            foreach (var id in EffectTargets.Resolve(context, applyToSelf))
            {
                if (context.Characters.TryGetCharacter(id, out var character)) character.GrantFeature(feature.Id);
            }
        }
    }
}
