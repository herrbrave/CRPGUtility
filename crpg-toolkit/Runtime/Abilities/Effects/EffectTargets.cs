using System.Collections.Generic;
using CRPG.Core;

namespace CRPG.Abilities
{
    /// <summary>Shared helper for effects that can apply either to the caster (spending the caster's own
    /// resource, granting the caster a feature) or to the ability's chosen targets.</summary>
    internal static class EffectTargets
    {
        public static IReadOnlyList<RuntimeEntityId> Resolve(AbilityExecutionContext context, bool applyToSelf) =>
            applyToSelf ? new[] { context.Source } : context.Targets;
    }
}
