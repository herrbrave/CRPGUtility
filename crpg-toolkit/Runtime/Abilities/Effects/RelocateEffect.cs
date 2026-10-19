using System;
using UnityEngine;

namespace CRPG.Abilities
{
    /// <summary>Moves the CASTER to the ability's aim point through the game's <see cref="IRelocationService"/>: Misty Step (<see cref="RelocationStyle.Teleport"/>), a leap (<see cref="RelocationStyle.Leap"/>). Needs an aimed ability (a Point target). A destination the game refuses does nothing (the aim surface refuses it at validation, so this is a last-moment guard); the service preflight keeps a caster from being charged when no relocation service is wired.</summary>
    [Serializable]
    public sealed class RelocateEffect : AbilityEffect
    {
        [SerializeField] private RelocationStyle style = RelocationStyle.Teleport;

        public RelocationStyle Style => style;

        public override void Apply(AbilityExecutionContext context)
        {
            if (!context.HasAim || context.Relocation == null) return;
            context.Relocation.TryRelocate(context.Source, context.Aim.Point, style);
        }
    }
}
