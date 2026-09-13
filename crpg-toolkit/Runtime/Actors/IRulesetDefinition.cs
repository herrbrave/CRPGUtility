namespace CRPG.Actors
{
    /// <summary>
    /// A pluggable ruleset's entrypoint (Docs/Design/Production-Foundation.md sec.3/6: "Introduce a validated
    /// RulesetDefinition/provider... Engine code never constructs SRD defaults."). A concrete implementation
    /// (e.g. <c>CRPG.Rules.SRD5.SRD5RulesetProvider</c>) registers its content and policy instances against the
    /// builder it's handed; nothing in the generic engine ever constructs a ruleset's concrete rules itself.
    /// </summary>
    public interface IRulesetDefinition
    {
        /// <summary>Stable identity for diagnostics/save compatibility (e.g. "srd5e2014") -- not currently
        /// persisted anywhere; a save-format concern for a later task if/when multiple rulesets coexist.</summary>
        string RulesetId { get; }

        void Register(IRulesetRegistrationBuilder builder);
    }
}
