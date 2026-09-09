using System;
using System.Collections.Generic;
using UnityEngine;

namespace CRPG.Abilities
{
    /// <summary>Runs a fixed list of other effects in order -- the "Composite/Sequence" effect from spec §57.</summary>
    [Serializable]
    public sealed class SequenceEffect : AbilityEffect
    {
        [SerializeReference] private List<AbilityEffect> effects = new();

        public override void Apply(AbilityExecutionContext context)
        {
            foreach (var effect in effects) effect.Apply(context);
        }
    }
}
