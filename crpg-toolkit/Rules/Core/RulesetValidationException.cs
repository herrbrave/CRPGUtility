using System;

namespace CRPG.Rules
{
    /// <summary>Thrown by <see cref="PolicyRegistry.ValidateRequired"/> when one or more required policy
    /// interfaces were never registered by a ruleset provider -- a session must not start in that state
    /// (Docs/Design/Production-Foundation.md's ownership invariant: "required missing dependencies are
    /// validation failures, not silent no-ops").</summary>
    public sealed class RulesetValidationException : Exception
    {
        public RulesetValidationException(string message) : base(message)
        {
        }
    }
}
