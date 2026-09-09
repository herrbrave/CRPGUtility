using System;
using System.Collections.Generic;
using CRPG.Core;

namespace CRPG.AI
{
    /// <summary>
    /// Resolves a Consideration's InputKey to a raw float -- mirrors FeatureModifier.QueryKey's convention of
    /// a bare string the engine doesn't interpret. Built-in keys cover only what's derivable from already-
    /// generic state; a ruleset/game registers its own (flanking, threat, ...) via Register, same extension
    /// shape as everywhere else in the toolkit.
    /// </summary>
    public sealed class ConsiderationInputRegistry
    {
        private const string SelfResourcePercentPrefix = "SelfResourcePercent:";
        private const string TargetResourcePercentPrefix = "TargetResourcePercent:";
        private const string InfluenceMapPrefix = "InfluenceMap:";

        private readonly Dictionary<string, Func<ConsiderationContext, float>> providers = new();

        public ConsiderationInputRegistry()
        {
            Register("Distance", context => UnityEngine.Vector3.Distance(
                context.Positions.GetPosition(context.Self), context.Positions.GetPosition(context.Target)));
            Register("RandomChance", context => context.RandomSource.Next(0, 10001) / 10000f);
        }

        public void Register(string inputKey, Func<ConsiderationContext, float> provider) => providers[inputKey] = provider;

        /// <returns>0 for an unresolved key -- a typo silently scores that Consideration as 0, not a crash.</returns>
        public float GetInput(string inputKey, ConsiderationContext context)
        {
            if (providers.TryGetValue(inputKey, out var provider)) return provider(context);

            // Structured keys are parsed on demand rather than pre-registered per resource id/layer, since the
            // set of resources and hazard layers isn't known until ruleset/encounter content is loaded.
            if (TryGetResourcePercent(inputKey, context, out var percent)) return percent;
            if (TryGetInfluenceMapSample(inputKey, context, out var influence)) return influence;

            return 0f;
        }

        private static bool TryGetResourcePercent(string inputKey, ConsiderationContext context, out float percent)
        {
            percent = 0f;

            string prefix;
            RuntimeEntityId who;
            if (inputKey.StartsWith(SelfResourcePercentPrefix, StringComparison.Ordinal)) { prefix = SelfResourcePercentPrefix; who = context.Self; }
            else if (inputKey.StartsWith(TargetResourcePercentPrefix, StringComparison.Ordinal)) { prefix = TargetResourcePercentPrefix; who = context.Target; }
            else return false;

            var resourceId = new DefinitionId(inputKey.Substring(prefix.Length));
            if (!context.Characters.TryGetCharacter(who, out var character)) return false;
            if (!character.TryGetResource(resourceId, out var resource) || resource.Max <= 0) return false;

            percent = (float)resource.Current / resource.Max;
            return true;
        }

        private static bool TryGetInfluenceMapSample(string inputKey, ConsiderationContext context, out float value)
        {
            value = 0f;
            if (context.InfluenceMap == null) return false;
            if (!inputKey.StartsWith(InfluenceMapPrefix, StringComparison.Ordinal)) return false;

            var remainder = inputKey.Substring(InfluenceMapPrefix.Length); // "<layer>:Self" or "<layer>:Target"
            var separatorIndex = remainder.LastIndexOf(':');
            if (separatorIndex < 0) return false;

            var layer = remainder.Substring(0, separatorIndex);
            var who = remainder.Substring(separatorIndex + 1);
            var entity = who switch
            {
                "Self" => context.Self,
                "Target" => context.Target,
                _ => RuntimeEntityId.None,
            };
            if (!entity.IsValid) return false;

            value = context.InfluenceMap.Sample(layer, context.Positions.GetPosition(entity));
            return true;
        }
    }
}
