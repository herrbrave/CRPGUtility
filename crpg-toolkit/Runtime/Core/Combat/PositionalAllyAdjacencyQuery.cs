using System;
using System.Collections.Generic;
using UnityEngine;

namespace CRPG.Core
{
    /// <summary>
    /// "Is another enemy of the target next to it" by geometry, for a world without a grid: any conscious ally of the attacker (never the attacker itself, never the target) within <see cref="ReachMetres"/> of the target, flat. Where the attacker stands does not matter, so a bow
    /// qualifies as the SRD intends. The game supplies who the attacker's allies are and who can still threaten (not down).
    /// </summary>
    public sealed class PositionalAllyAdjacencyQuery : IAllyAdjacencyQuery
    {
        public const float DefaultReachMetres = 1.8f;

        private readonly IPositionProvider positions;
        private readonly Func<RuntimeEntityId, IEnumerable<RuntimeEntityId>> alliesOf;
        private readonly Func<RuntimeEntityId, bool> canThreaten;

        public float ReachMetres { get; }

        public PositionalAllyAdjacencyQuery(IPositionProvider positions, Func<RuntimeEntityId, IEnumerable<RuntimeEntityId>> alliesOf, Func<RuntimeEntityId, bool> canThreaten = null, float reachMetres = DefaultReachMetres)
        {
            this.positions = positions ?? throw new ArgumentNullException(nameof(positions));
            this.alliesOf = alliesOf ?? throw new ArgumentNullException(nameof(alliesOf));
            this.canThreaten = canThreaten ?? (_ => true);
            ReachMetres = reachMetres;
        }

        public bool IsAllyAdjacentToTarget(RuntimeEntityId attacker, RuntimeEntityId target)
        {
            if (!attacker.IsValid || !target.IsValid || attacker == target) return false;
            var targetAt = positions.GetPosition(target);
            foreach (var ally in alliesOf(attacker))
            {
                if (!ally.IsValid || ally == attacker || ally == target || !canThreaten(ally)) continue;
                var offset = positions.GetPosition(ally) - targetAt; offset.y = 0f;
                if (offset.magnitude <= ReachMetres) return true;
            }

            return false;
        }
    }
}
