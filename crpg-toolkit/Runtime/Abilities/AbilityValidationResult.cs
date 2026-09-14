namespace CRPG.Abilities
{
    /// <summary>Task 4.2: every reason Validate() can reject an ability/target/caster combination. Shared with
    /// AbilityExecutionResult -- Execute's internal re-Validate() converts a rejection here straight into an
    /// AbilityExecutionResult.Rejected, so there is exactly one vocabulary for "why not," not two.</summary>
    public enum AbilityRejectionReason
    {
        None,
        Incapacitated,
        AbilityNotKnown,
        InsufficientResources,
        InvalidTargetCount,
        DuplicateTarget,
        InvalidTarget,
        WrongRelation,
        OutOfRange,
        NoLineOfSight,
        MissingRequiredEffectService,
        RitualTagRequired,
        UpcastSlotBelowSpellLevel
    }

    public enum AbilityValidationOutcome { Valid, Rejected }

    /// <summary>
    /// Pure preflight result of AbilityResolutionService.Validate() -- never a side effect of calling it, and
    /// no "Faulted" case: an unexpected implementation error is Execute's concern (AbilityExecutionResult),
    /// not a preflight validation concern. Confirmed as an intentional design property by
    /// IAbilityResolutionService's own doc comment: a UI/presentation layer is meant to call this "as often as
    /// it needs while the player is still choosing," which only holds if it's genuinely side-effect-free.
    /// </summary>
    public sealed class AbilityValidationResult
    {
        private AbilityValidationResult(AbilityValidationOutcome outcome, AbilityRejectionReason reason, string message)
        {
            Outcome = outcome;
            Reason = reason;
            Message = message;
        }

        public AbilityValidationOutcome Outcome { get; }

        /// <summary>Meaningful only when <see cref="Outcome"/> is <see cref="AbilityValidationOutcome.Rejected"/>.</summary>
        public AbilityRejectionReason Reason { get; }

        public string Message { get; }

        public bool IsValid => Outcome == AbilityValidationOutcome.Valid;

        public static AbilityValidationResult Ok() => new(AbilityValidationOutcome.Valid, AbilityRejectionReason.None, null);

        public static AbilityValidationResult Reject(AbilityRejectionReason reason, string message = null) =>
            new(AbilityValidationOutcome.Rejected, reason, message);
    }
}
