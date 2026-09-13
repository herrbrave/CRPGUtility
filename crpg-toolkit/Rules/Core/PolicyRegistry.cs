using System;
using System.Collections.Generic;
using System.Linq;

namespace CRPG.Rules
{
    /// <summary>
    /// Holds the concrete rule/policy instances (<c>IArmorClassRule</c>, <c>ISkillCheckRule</c>, ...) a ruleset
    /// provider registers during session construction, keyed by the interface type a consumer asks for --
    /// mirrors <see cref="CRPG.Core.RulesDatabase"/>'s own "register once, resolve by lookup" shape, but for
    /// behavior/policy instead of content data. A GameSession-scoped instance; not a static/global registry.
    /// </summary>
    public sealed class PolicyRegistry
    {
        private readonly Dictionary<Type, object> policiesByType = new();

        /// <summary>Throws if a policy for <typeparamref name="TPolicy"/> is already registered -- a duplicate
        /// registration means ambiguous provider composition (two providers both claiming the same interface),
        /// which must be caught immediately rather than silently overwritten.</summary>
        public void Register<TPolicy>(TPolicy policy) where TPolicy : class
        {
            if (policy == null) throw new ArgumentNullException(nameof(policy));

            if (policiesByType.ContainsKey(typeof(TPolicy)))
                throw new InvalidOperationException($"A policy for '{typeof(TPolicy).Name}' is already registered.");

            policiesByType[typeof(TPolicy)] = policy;
        }

        public bool TryGet<TPolicy>(out TPolicy policy) where TPolicy : class
        {
            if (policiesByType.TryGetValue(typeof(TPolicy), out var found))
            {
                policy = (TPolicy)found;
                return true;
            }

            policy = null;
            return false;
        }

        public TPolicy Get<TPolicy>() where TPolicy : class
        {
            if (TryGet<TPolicy>(out var policy)) return policy;
            throw new InvalidOperationException($"No policy is registered for '{typeof(TPolicy).Name}'.");
        }

        /// <summary>Throws a single <see cref="RulesetValidationException"/> naming every missing type at once
        /// (not just the first) -- "required unregistered policies block session start with useful
        /// diagnostics."</summary>
        public void ValidateRequired(IReadOnlyList<Type> requiredPolicyTypes)
        {
            if (requiredPolicyTypes == null || requiredPolicyTypes.Count == 0) return;

            var missing = requiredPolicyTypes.Where(type => !policiesByType.ContainsKey(type)).ToList();
            if (missing.Count == 0) return;

            throw new RulesetValidationException(
                $"Missing required ruleset policies: {string.Join(", ", missing.Select(type => type.Name))}.");
        }
    }
}
