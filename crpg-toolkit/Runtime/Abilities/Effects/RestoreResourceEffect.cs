using System;
using CRPG.Actors;
using CRPG.Core;
using UnityEngine;

namespace CRPG.Abilities
{
    [Serializable]
    public sealed class RestoreResourceEffect : AbilityEffect
    {
        [SerializeField] private ResourceDefinition resource;
        [SerializeField] private int amount = 1;
        [SerializeField] private bool applyToSelf;

        public override void Apply(AbilityExecutionContext context)
        {
            foreach (var id in EffectTargets.Resolve(context, applyToSelf))
            {
                if (context.Characters.TryGetCharacter(id, out var character) && character.TryGetResource(resource.Id, out var resourceState))
                    resourceState.Restore(amount);
            }
        }
    }
}
