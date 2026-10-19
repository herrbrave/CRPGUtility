using System;
using System.Collections.Generic;
using UnityEngine;

namespace CRPG.Core
{
    /// <summary>
    /// Flanking by geometry, for a world without a grid: the attacker is flanking when it is in melee contact with the target (within <see cref="ReachMetres"/>, flat) and a conscious ally of it is also in contact on the OPPOSITE side, meaning
    /// the angle at the target between the attacker and that ally is at least <see cref="MinimumAngleDegrees"/> (180 is directly across; 120 forgives a loose ring of fighters). Ranged attackers are not in contact, so they never flank.
    /// The game supplies who the attacker's allies are and who can still threaten (not down).
    /// </summary>
    public sealed class PositionalFlankingQuery : IFlankingQuery
    {
        public const float DefaultReachMetres = 2.0f, DefaultMinimumAngleDegrees = 120f;

        private readonly IPositionProvider positions;
        private readonly Func<RuntimeEntityId, IEnumerable<RuntimeEntityId>> alliesOf;
        private readonly Func<RuntimeEntityId, bool> canThreaten;

        public float ReachMetres { get; }
        public float MinimumAngleDegrees { get; }

        public PositionalFlankingQuery(IPositionProvider positions, Func<RuntimeEntityId, IEnumerable<RuntimeEntityId>> alliesOf, Func<RuntimeEntityId, bool> canThreaten = null,
            float reachMetres = DefaultReachMetres, float minimumAngleDegrees = DefaultMinimumAngleDegrees)
        {
            this.positions = positions ?? throw new ArgumentNullException(nameof(positions));
            this.alliesOf = alliesOf ?? throw new ArgumentNullException(nameof(alliesOf));
            this.canThreaten = canThreaten ?? (_ => true);
            ReachMetres = reachMetres; MinimumAngleDegrees = minimumAngleDegrees;
        }

        public bool IsFlanking(RuntimeEntityId attacker, RuntimeEntityId target)
        {
            if (!attacker.IsValid || !target.IsValid || attacker == target) return false;
            var targetAt = positions.GetPosition(target);
            var toAttacker = Flat(positions.GetPosition(attacker) - targetAt);
            if (toAttacker.magnitude > ReachMetres) return false;   // not in melee contact: a bow or a spell does not flank
            foreach (var ally in alliesOf(attacker))
            {
                if (!ally.IsValid || ally == attacker || ally == target || !canThreaten(ally)) continue;
                var toAlly = Flat(positions.GetPosition(ally) - targetAt);
                if (toAlly.magnitude > ReachMetres || toAlly.sqrMagnitude < 1e-6f || toAttacker.sqrMagnitude < 1e-6f) continue;
                if (Vector3.Angle(toAttacker, toAlly) >= MinimumAngleDegrees) return true;
            }

            return false;
        }

        private static Vector3 Flat(Vector3 v) { v.y = 0f; return v; }
    }
}
