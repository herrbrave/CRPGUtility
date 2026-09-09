namespace CRPG.Rules
{
    /// <summary>One resolved step of a RuleQuery's modifier pipeline, for the runtime "why is this value 18?" debug view.</summary>
    public readonly struct RuleModifierTraceEntry
    {
        public RuleModifierTraceEntry(string source, RuleModifierOperation operation, double value, double resultingValue)
        {
            Source = source;
            Operation = operation;
            Value = value;
            ResultingValue = resultingValue;
        }

        public string Source { get; }
        public RuleModifierOperation Operation { get; }
        public double Value { get; }
        public double ResultingValue { get; }
    }
}
