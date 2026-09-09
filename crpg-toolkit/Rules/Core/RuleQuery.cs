namespace CRPG.Rules
{
    /// <summary>
    /// Base for a single evaluation of "what is this value, right now, for this subject" -- e.g. an SRD5
    /// ArmorClassQuery. <see cref="Subject"/> is untyped here so CRPG.Rules stays ignorant of what a
    /// "character" or "item" actually is; concrete queries live in the ruleset module.
    /// </summary>
    public abstract class RuleQuery<TResult>
    {
        protected RuleQuery(object subject)
        {
            Subject = subject;
        }

        public object Subject { get; }

        public abstract TResult Evaluate();
    }
}
