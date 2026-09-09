using UnityEngine;

namespace CRPG.Core
{
    /// <summary>One contributor to an InfluenceMap layer -- a hazard, a threat radius, a patch of difficult
    /// terrain. Layer is a free-form string (same convention as FeatureModifier.QueryKey) so the engine never
    /// needs to know "DifficultTerrain" is a ruleset concept.</summary>
    public sealed class InfluenceSource
    {
        public InfluenceSource(RuntimeEntityId id, string layer, Vector3 position, float radius, float magnitude, AnimationCurve falloff)
        {
            Id = id;
            Layer = layer;
            Position = position;
            Radius = radius;
            Magnitude = magnitude;
            Falloff = falloff;
        }

        /// <summary>Lets a caller remove this exact source later (a spell's zone ending, a hazard being cleared).</summary>
        public RuntimeEntityId Id { get; }

        public string Layer { get; }
        public Vector3 Position { get; }
        public float Radius { get; }
        public float Magnitude { get; }

        /// <summary>Authored 1-at-X=0 (center) -> 0-at-X=1 (edge); InfluenceMap evaluates it directly at
        /// distance/radius.</summary>
        public AnimationCurve Falloff { get; }
    }
}
