using System;

namespace CRPG.Abilities
{
    public enum AbilityExecutionOutcome { Succeeded, Rejected, Faulted }

    /// <summary>
    /// The actual outcome of AbilityResolutionService.Execute() -- an "ability attempt" result, not a preflight
    /// validation (see AbilityValidationResult's own remarks on why the two are separate types). Transaction
    /// boundary: Rejected is always zero-mutation (no resource touched, no event published), whether the
    /// rejection came from Execute's own internal re-Validate() or was already true before Execute was ever
    /// called. Faulted is different in kind -- it only happens after resources were already spent and
    /// AbilityStarted already published, when one of the ability's effects threw an unexpected exception. No
    /// rollback on Faulted (matches a real tabletop cast not un-happening because of a later mishap) -- this is
    /// "validate before commit, then spend exactly once," not "every effect is atomic."
    /// </summary>
    public sealed class AbilityExecutionResult
    {
        private AbilityExecutionResult(AbilityExecutionOutcome outcome, AbilityRejectionReason rejectionReason, string message)
        {
            Outcome = outcome;
            RejectionReason = rejectionReason;
            Message = message;
        }

        public AbilityExecutionOutcome Outcome { get; }

        /// <summary>Meaningful only when <see cref="Outcome"/> is <see cref="AbilityExecutionOutcome.Rejected"/>.</summary>
        public AbilityRejectionReason RejectionReason { get; }

        /// <summary>Populated for Rejected (human-readable) and Faulted (exception detail); null for Succeeded.</summary>
        public string Message { get; }

        public bool Succeeded => Outcome == AbilityExecutionOutcome.Succeeded;

        public static AbilityExecutionResult Ok() => new(AbilityExecutionOutcome.Succeeded, AbilityRejectionReason.None, null);

        /// <summary>Converts a Validate() rejection straight into an execution-level rejection -- the single
        /// place a validation failure becomes an execution failure, so the two never drift into separate
        /// vocabularies.</summary>
        public static AbilityExecutionResult FromRejection(AbilityValidationResult validation) =>
            new(AbilityExecutionOutcome.Rejected, validation.Reason, validation.Message);

        public static AbilityExecutionResult Fault(string message) =>
            new(AbilityExecutionOutcome.Faulted, AbilityRejectionReason.None, message);

        public static AbilityExecutionResult Fault(Exception exception) => Fault(exception.Message);
    }
}
