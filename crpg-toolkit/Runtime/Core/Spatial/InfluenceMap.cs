using System.Collections.Generic;
using UnityEngine;

namespace CRPG.Core
{
    /// <summary>
    /// A battlefield-scoped spatial field, like CombatEncounter -- not a DefinitionAsset, since which hazards
    /// exist is world/encounter state, not reusable authored data. Considerations sample it by layer name
    /// (spec's "difficult terrain", "dangerous areas like fire, acid") to answer "is this position bad" without
    /// the AI system needing any bespoke hazard-avoidance mechanism of its own.
    /// </summary>
    public sealed class InfluenceMap
    {
        private readonly List<InfluenceSource> sources = new();

        public void AddSource(InfluenceSource source) => sources.Add(source);

        public bool RemoveSource(RuntimeEntityId id)
        {
            for (var i = 0; i < sources.Count; i++)
            {
                if (sources[i].Id != id) continue;
                sources.RemoveAt(i);
                return true;
            }

            return false;
        }

        /// <summary>Sum of every same-layer source's falloff-weighted contribution at this position. 0 if no
        /// source on this layer reaches this far.</summary>
        public float Sample(string layer, Vector3 position)
        {
            var total = 0f;
            foreach (var source in sources)
            {
                if (source.Layer != layer) continue;
                if (source.Radius <= 0f) continue;

                var distance = Vector3.Distance(source.Position, position);
                if (distance > source.Radius) continue;

                // Evaluated directly at distance/radius (0 = center, 1 = edge) -- the intuitive authoring
                // direction for a falloff curve ("how strong is this right at the center, tapering to nothing
                // at the edge"). An inverted (1 - normalizedDistance) convention was tried first and reliably
                // produced backwards test curves even from the person who wrote it; not worth the confusion.
                var normalizedDistance = distance / source.Radius;
                total += source.Falloff.Evaluate(normalizedDistance) * source.Magnitude;
            }

            return total;
        }

        public IReadOnlyList<InfluenceSource> SourcesInLayer(string layer)
        {
            var result = new List<InfluenceSource>();
            foreach (var source in sources)
                if (source.Layer == layer) result.Add(source);
            return result;
        }
    }
}
