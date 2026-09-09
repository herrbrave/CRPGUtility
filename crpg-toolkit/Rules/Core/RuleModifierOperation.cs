namespace CRPG.Rules
{
    /// <summary>
    /// Shared vocabulary of modifier operations a feature, item or condition can contribute to a RuleQuery.
    /// Add/Subtract/Multiply/Override/Minimum/Maximum are resolved generically by <see cref="NumericRuleQuery"/>.
    /// The rest are tags consumed by more specialized systems added in later phases:
    /// <list type="bullet">
    /// <item>Advantage/Disadvantage -&gt; combined via CRPG.Core.AdvantageStateExtensions.Combine before a roll (Abilities/Combat).</item>
    /// <item>GrantProficiency/GrantExpertise -&gt; the proficiency system (Abilities/Actors).</item>
    /// <item>Resistance/Immunity/Vulnerability -&gt; the damage pipeline (Combat).</item>
    /// <item>CriticalRange -&gt; attack resolution (Combat).</item>
    /// <item>DiceBonus -&gt; folded into a DiceExpression before a roll is requested (Combat/Abilities).</item>
    /// </list>
    /// </summary>
    public enum RuleModifierOperation
    {
        Add,
        Subtract,
        Multiply,
        Override,
        Minimum,
        Maximum,
        Advantage,
        Disadvantage,
        GrantProficiency,
        GrantExpertise,
        Resistance,
        Immunity,
        Vulnerability,
        CriticalRange,
        DiceBonus,

        /// <summary>Consumed by SavingThrowEffect: the target fails this saving throw automatically, without
        /// rolling (e.g. Paralyzed/Unconscious/Stunned auto-failing Strength and Dexterity saves).</summary>
        AutoFailSave
    }
}
