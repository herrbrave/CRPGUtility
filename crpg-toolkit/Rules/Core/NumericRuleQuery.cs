using System;
using System.Collections.Generic;
using System.Linq;

namespace CRPG.Rules
{
    /// <summary>
    /// Reusable resolution for the extremely common shape: a base number adjusted by a bag of
    /// Add/Subtract/Multiply/Override/Minimum/Maximum modifiers (armor class, attack bonus, a DC, a skill
    /// total, ...). Concrete queries just supply a base value and gather modifiers from race/class/features/
    /// conditions/equipment/spells/environment (see spec ("Derived-stat rule engine")); this class doesn't
    /// need to know where they came from.
    /// </summary>
    public class NumericRuleQuery : RuleQuery<double>
    {
        private readonly List<RuleModifier> modifiers = new();
        private readonly List<RuleModifierTraceEntry> trace = new();

        public NumericRuleQuery(object subject, double baseValue) : base(subject)
        {
            BaseValue = baseValue;
        }

        public double BaseValue { get; }

        /// <summary>Populated by the most recent <see cref="Evaluate"/> call; drives the runtime debug window.</summary>
        public IReadOnlyList<RuleModifierTraceEntry> Trace => trace;

        public void AddModifier(RuleModifier modifier) => modifiers.Add(modifier);

        public override double Evaluate()
        {
            trace.Clear();
            var running = BaseValue;
            trace.Add(new RuleModifierTraceEntry("Base", RuleModifierOperation.Add, BaseValue, running));

            running = ApplyAdditive(running, RuleModifierOperation.Add, sign: 1);
            running = ApplyAdditive(running, RuleModifierOperation.Subtract, sign: -1);

            foreach (var modifier in modifiers.Where(m => m.Operation == RuleModifierOperation.Multiply).OrderBy(m => m.Priority))
            {
                running *= modifier.Value;
                trace.Add(new RuleModifierTraceEntry(modifier.Source, modifier.Operation, modifier.Value, running));
            }

            foreach (var modifier in modifiers.Where(m => m.Operation == RuleModifierOperation.Minimum).OrderBy(m => m.Priority))
            {
                running = Math.Max(running, modifier.Value);
                trace.Add(new RuleModifierTraceEntry(modifier.Source, modifier.Operation, modifier.Value, running));
            }

            foreach (var modifier in modifiers.Where(m => m.Operation == RuleModifierOperation.Maximum).OrderBy(m => m.Priority))
            {
                running = Math.Min(running, modifier.Value);
                trace.Add(new RuleModifierTraceEntry(modifier.Source, modifier.Operation, modifier.Value, running));
            }

            // Override wins outright, last: it supersedes every additive/multiplicative/clamped result above
            // (e.g. Petrified fixing AC to an exact number regardless of armor, shields, or spell bonuses).
            var overrides = modifiers.Where(m => m.Operation == RuleModifierOperation.Override)
                .OrderBy(m => m.Priority).ToList();
            if (overrides.Count > 0)
            {
                var chosen = overrides[^1];
                running = chosen.Value;
                trace.Add(new RuleModifierTraceEntry(chosen.Source, chosen.Operation, chosen.Value, running));
            }

            return running;
        }

        private double ApplyAdditive(double running, RuleModifierOperation operation, int sign)
        {
            foreach (var modifier in modifiers.Where(m => m.Operation == operation).OrderBy(m => m.Priority))
            {
                running += sign * modifier.Value;
                trace.Add(new RuleModifierTraceEntry(modifier.Source, modifier.Operation, modifier.Value, running));
            }

            return running;
        }
    }
}
