using System;
using CRPG.Rules;
using UnityEngine;

namespace CRPG.Actors
{
    /// <summary>
    /// One modifier a <see cref="FeatureDefinition"/> or <see cref="ConditionDefinition"/> contributes to a
    /// numeric query -- e.g. "Shield: Add 2 to ArmorClass". <see cref="QueryKey"/> is a bare string rather than
    /// a reference to an actual query type, because concrete queries (ArmorClassQuery, AttackBonusQuery, ...)
    /// are ruleset-authored and this assembly must stay ignorant of them. A ruleset's query implementation
    /// gathers every FeatureModifier tagged with its own key (see <see cref="FeatureModifierCollector"/>).
    /// </summary>
    [Serializable]
    public struct FeatureModifier
    {
        [SerializeField] private string queryKey;
        [SerializeField] private RuleModifierOperation operation;
        [SerializeField] private float value;
        [SerializeField] private int priority;

        public readonly string QueryKey => queryKey;

        /// <summary>`source` is filled in by the collector (the feature/condition's own display name) rather
        /// than authored per-modifier, since it's a trace label for the "why is this value 18?" debug view.</summary>
        public readonly RuleModifier ToRuleModifier(string source) => new(operation, value, source, priority);
    }
}
