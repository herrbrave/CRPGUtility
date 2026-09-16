using System;
using System.Reflection;
using CRPG.Abilities;
using CRPG.Actors;
using CRPG.Core;
using UnityEngine;

namespace CRPG.Tests.Fixtures.AlternateRuleset
{
    /// <summary>
    /// Task 5.1's small, non-d20 <see cref="IRulesetDefinition"/>. Plain C# (not a <see cref="ScriptableObject"/>
    /// with <c>[CreateAssetMenu]</c> like <c>CRPG.Rules.SRD5.SRD5RulesetProvider</c>) since this ruleset is never
    /// authored through the Inspector -- it exists only for <c>RulesetContractTests</c>. Registers a real,
    /// differently-shaped action economy (a single "Action Points" resource spent in different amounts by two
    /// abilities, replacing SRD5's Action/Bonus Action/Reaction split entirely) plus <see cref="AlternateCheckRule"/>.
    /// <see cref="RulesetId"/> is a constructor argument, not hardcoded, so a test can prove nothing in the
    /// behavior path actually reads it.
    /// </summary>
    public sealed class AlternateRulesetProvider : IRulesetDefinition, IDisposable
    {
        public static readonly DefinitionId ActionPointsResourceId = new("resource-action-points");
        public static readonly DefinitionId QuickJabAbilityId = new("ability-quick-jab");
        public static readonly DefinitionId HeavyJabAbilityId = new("ability-heavy-jab");

        private bool disposed;

        public AlternateRulesetProvider(string rulesetId = "test-alternate")
        {
            RulesetId = rulesetId;

            ActionPoints = CreateResourceDefinition(ActionPointsResourceId, "Action Points");
            QuickJab = CreateAbilityDefinition(QuickJabAbilityId, "Quick Jab", ActionPoints, cost: 1);
            HeavyJab = CreateAbilityDefinition(HeavyJabAbilityId, "Heavy Jab", ActionPoints, cost: 2);
        }

        public string RulesetId { get; }

        public ResourceDefinition ActionPoints { get; }
        public AbilityDefinition QuickJab { get; }
        public AbilityDefinition HeavyJab { get; }

        public void Register(IRulesetRegistrationBuilder builder)
        {
            builder.RegisterDefinitions(new IDefinition[] { ActionPoints, QuickJab, HeavyJab });
            builder.RegisterPolicy<ISkillCheckRule>(new AlternateCheckRule(builder.Characters, builder.RulesDatabase));
        }

        /// <summary>Destroys the transient ScriptableObjects this provider created. <c>GameSession.Dispose()</c>
        /// does not destroy registered definitions, so ownership/cleanup is this fixture's job.</summary>
        public void Dispose()
        {
            if (disposed) return;
            disposed = true;

            UnityEngine.Object.DestroyImmediate(ActionPoints);
            UnityEngine.Object.DestroyImmediate(QuickJab);
            UnityEngine.Object.DestroyImmediate(HeavyJab);
        }

        private static ResourceDefinition CreateResourceDefinition(DefinitionId id, string displayName)
        {
            var definition = ScriptableObject.CreateInstance<ResourceDefinition>();
            SetPrivateField(definition, "id", id.Value);
            SetPrivateField(definition, "displayName", displayName);
            return definition;
        }

        private static AbilityDefinition CreateAbilityDefinition(DefinitionId id, string displayName, ResourceDefinition resource, int cost)
        {
            var definition = ScriptableObject.CreateInstance<AbilityDefinition>();
            SetPrivateField(definition, "id", id.Value);
            SetPrivateField(definition, "displayName", displayName);

            var targeting = new TargetingDefinition();
            SetPrivateField(targeting, "mode", TargetMode.Self);
            SetPrivateField(targeting, "relation", TargetRelation.Self);
            SetPrivateField(targeting, "minTargets", 1);
            SetPrivateField(targeting, "maxTargets", 1);
            SetPrivateField(targeting, "range", 0f);
            SetPrivateField(definition, "targeting", targeting);

            object boxedCost = new AbilityCost();
            SetPrivateField(boxedCost, "resource", resource);
            SetPrivateField(boxedCost, "amount", cost);
            SetPrivateField(definition, "costs", new System.Collections.Generic.List<AbilityCost> { (AbilityCost)boxedCost });

            // "effects" is deliberately left at its default empty list -- a pure resource-spend action, no
            // combat/damage machinery needed to prove the economy (see plan's "if something doesn't fit" note).
            return definition;
        }

        private static void SetPrivateField(object target, string fieldName, object value)
        {
            var type = target.GetType();
            while (type != null)
            {
                var field = type.GetField(fieldName, BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.DeclaredOnly);
                if (field != null)
                {
                    field.SetValue(target, value);
                    return;
                }

                type = type.BaseType;
            }

            throw new InvalidOperationException($"Field '{fieldName}' not found on {target.GetType().Name} or its base types.");
        }
    }
}
