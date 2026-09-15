using CRPG.Abilities;

namespace CRPG.UI
{
    /// <summary>Task 4.5: one ability-bar button's worth of data -- plain data, no UI Toolkit dependency.</summary>
    public sealed class AbilityBarEntrySummary
    {
        public AbilityBarEntrySummary(AbilityDefinition ability, bool canAttempt, string disabledReason)
        {
            Ability = ability;
            CanAttempt = canAttempt;
            DisabledReason = disabledReason;
        }

        public AbilityDefinition Ability { get; }

        /// <summary>Target-independent readiness only (see <see cref="IAbilityResolutionService.CanAttempt"/>)
        /// -- true here does not guarantee a chosen target will validate; it only means the button is worth
        /// enabling at all before targeting begins.</summary>
        public bool CanAttempt { get; }

        /// <summary>Meaningful only when <see cref="CanAttempt"/> is false.</summary>
        public string DisabledReason { get; }
    }
}
