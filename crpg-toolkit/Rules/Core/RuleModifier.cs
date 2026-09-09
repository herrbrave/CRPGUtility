using System;

namespace CRPG.Rules
{
    [Serializable]
    public readonly struct RuleModifier
    {
        public RuleModifier(RuleModifierOperation operation, double value, string source, int priority = 0)
        {
            Operation = operation;
            Value = value;
            Source = source;
            Priority = priority;
        }

        public RuleModifierOperation Operation { get; }
        public double Value { get; }

        /// <summary>Human-readable origin ("Shield", "Chain Mail", "Rage") shown in the "why is this value 18?" trace.</summary>
        public string Source { get; }

        /// <summary>Resolution order within an operation group; lower runs first. Matters mainly for Override, where the last one wins.</summary>
        public int Priority { get; }
    }
}
